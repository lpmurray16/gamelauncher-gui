package com.gamelauncher.companion

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.TransportEnum
import io.reactivex.rxjava3.core.Completable
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.IOException
import java.net.Proxy
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.random.Random

private suspend fun Completable.awaitCompletion() = suspendCancellableCoroutine<Unit> { c ->
    val subscription = subscribe({ if (c.isActive) c.resume(Unit) }, { if (c.isActive) c.resumeWithException(it) })
    c.invokeOnCancellation { subscription.dispose() }
}

data class PcUi(
    val device: Device,
    val endpoint: String,
    val online: Boolean = false,
    val connection: String = "Offline",
    val games: List<Game> = emptyList(),
    val commanding: Set<String> = emptySet(),
    val message: String? = null,
    val power: PowerStatus? = null,
    val powerBusy: Boolean = false,
    val generation: Long = 0
)

/** One independent foreground connection/retry loop per PC. Mutable fields are Main-thread confined. */
class PcSession(val saved: SavedPc, private val scope: CoroutineScope) {
    private val mutable = MutableStateFlow(PcUi(saved.device, "${saved.host}:${saved.port}"))
    val state = mutable.asStateFlow()
    private var loop: Job? = null
    private var epoch = 0L
    private var endpoint: LanEndpoint? = null
    private var failure: CompletableDeferred<Unit>? = null
    private var refreshing = false
    private val buffered = mutableListOf<StatusEvent>()
    private val revisions = mutableMapOf<String, Long>()

    fun reconnect() {
        stop()
        val current = epoch
        loop = scope.launch {
            var attempt = 0
            while (isActive && current == epoch) {
                mutable.value = mutable.value.copy(online = false, connection = "Connecting…")
                val disconnected = CompletableDeferred<Unit>()
                failure = disconnected
                var hub: com.microsoft.signalr.HubConnection? = null
                var powerPoll: Job? = null
                try {
                    val api = LanEndpoint.resolve(saved.host, saved.port)
                    val device = api.device() // No token is sent until identity and protocol match.
                    if (device.deviceId != saved.device.deviceId) throw IdentityChanged()
                    ensureActive()
                    endpoint = api
                    // REST auth preflight gives a clear 401/re-pair state even if the hub rejects its upgrade.
                    val initialGames = api.games(saved.token)
                    mutable.value = mutable.value.copy(games = initialGames, power = api.power(saved.token))
                    revisions.clear()
                    buffered.clear()
                    refreshing = true
                    val connection = HubConnectionBuilder.create(api.base + "/hubs/games")
                        .withTransport(TransportEnum.WEBSOCKETS)
                        // Direct WebSocket avoids SignalR negotiate redirects to arbitrary hosts.
                        .shouldSkipNegotiate(true)
                        .withHeader("Authorization", "Bearer ${saved.token}")
                        .withHandshakeResponseTimeout(10000)
                        .withServerTimeout(30000)
                        .withKeepAliveInterval(15000)
                        .setHttpClientBuilderCallback { builder ->
                            builder.proxy(Proxy.NO_PROXY).dns(api.client.dns)
                                .followRedirects(false).followSslRedirects(false)
                                .connectTimeout(8, TimeUnit.SECONDS)
                                .addInterceptor { chain ->
                                    val url = chain.request().url
                                    val expected = okhttp3.HttpUrl.Builder().scheme("http")
                                        .host(java.net.URI(api.base).host).port(saved.port).build()
                                    if (url.host != expected.host || url.port != expected.port || url.scheme != "http") {
                                        throw IOException("Hub endpoint changed")
                                    }
                                    chain.proceed(chain.request())
                                }
                        }.build()
                    hub = connection
                    connection.on("GameStatusChanged", { event: StatusEvent ->
                        scope.launch {
                            if (current == epoch && failure === disconnected && !disconnected.isCompleted) receive(event)
                        }
                    }, StatusEvent::class.java)
                    connection.onClosed { _ ->
                        disconnected.complete(Unit)
                        scope.launch {
                            if (current == epoch && failure === disconnected) {
                                mutable.value = mutable.value.copy(online = false, connection = "Offline • retrying")
                            }
                        }
                    }
                    withTimeout(20000) { connection.start().awaitCompletion() }
                    refresh(api)
                    if (disconnected.isCompleted) throw IOException("Live connection closed")
                    mutable.value = mutable.value.copy(device = device, online = true, connection = "Connected", message = null, generation = current)
                    attempt = 0
                    if (mutable.value.power != null) powerPoll = launch {
                        try {
                            while (isActive && current == epoch && failure === disconnected) {
                                delay(1000)
                                val status = api.power(saved.token)
                                if (current == epoch && failure === disconnected) applyPower(status)
                            }
                        } catch (e: CancellationException) { throw e
                        } catch (_: Exception) {
                            if (current == epoch && failure === disconnected) {
                                mutable.value = mutable.value.copy(online = false,
                                    message = "Power status lost. A shutdown countdown may still be active. Check the PC; disconnection does not confirm shutdown.")
                                disconnected.complete(Unit)
                            }
                        }
                    }
                    while (isActive) {
                        // A close interrupts the wait; library edits are reflected every 30 seconds.
                        if (withTimeoutOrNull(30000) { disconnected.await(); true } == true) {
                            throw IOException("Live connection closed")
                        }
                        refresh(api)
                    }
                } catch (e: Exception) {
                    if (e is CancellationException && e !is TimeoutCancellationException) throw e
                    val terminal = e is IdentityChanged || (e is ApiError && e.code == 401)
                    mutable.value = mutable.value.copy(online = false,
                        connection = if (terminal) "Pairing required" else "Offline • retrying",
                        message = when {
                            e is IdentityChanged -> "A different PC answered at this address. Remove this PC and pair again. No saved token was sent."
                            e is ApiError && e.code == 401 -> "Pairing was revoked. Remove this PC and pair again."
                            else -> "Connection unavailable. Check the PC, companion server, firewall, and Wi-Fi."
                        })
                    if (terminal) break
                } finally {
                    powerPoll?.cancel()
                    disconnected.complete(Unit)
                    if (current == epoch) {
                        failure = null
                        endpoint = null
                        refreshing = false
                        buffered.clear()
                        mutable.value = mutable.value.copy(online = false, commanding = emptySet(), powerBusy = false)
                    }
                    withContext(NonCancellable) {
                        withTimeoutOrNull(3000) { runCatching { hub?.stop()?.awaitCompletion() } }
                    }
                }
                val delayMs = (1000L shl attempt.coerceAtMost(5)).coerceAtMost(30000L)
                attempt++
                delay(delayMs + Random.nextLong(0, 500))
            }
        }
    }

    fun stop() {
        epoch++
        loop?.cancel()
        loop = null
        endpoint = null
        failure?.complete(Unit)
        mutable.value = mutable.value.copy(online = false, connection = "Offline", commanding = emptySet(), powerBusy = false)
    }

    private suspend fun refresh(api: LanEndpoint) {
        refreshing = true
        buffered.clear()
        val games = api.games(saved.token)
        // REST has no revision. Replay every accepted event received during this GET over its snapshot.
        var merged = games
        buffered.forEach { event -> merged = merged.map { if (it.id == event.gameId) it.copy(status = event.status) else it } }
        buffered.clear()
        refreshing = false
        mutable.value = mutable.value.copy(games = merged)
    }

    private fun receive(event: StatusEvent) {
        val id = runCatching { validId(event.gameId) }.getOrNull() ?: return
        if (event.status !in statuses || event.revision <= (revisions[id] ?: -1L)) return
        event.gameId = id
        revisions[id] = event.revision
        if (refreshing) buffered.add(event)
        else mutable.value = mutable.value.copy(games = mutable.value.games.map {
            if (it.id == id) it.copy(status = event.status) else it
        })
    }

    private fun applyPower(status: PowerStatus?) {
        if (status == null || status.revision >= (mutable.value.power?.revision ?: -1L))
            mutable.value = mutable.value.copy(power = status)
    }

    fun shutdownPc(cancel: Boolean = false) {
        val api = endpoint ?: return
        val power = mutable.value.power ?: return
        if (!mutable.value.online || mutable.value.powerBusy) return
        if (cancel) { if (!power.pending) return }
        else if (!power.remoteShutdownAllowed || power.pending || power.dispatching) return
        val current = epoch
        val connection = failure
        fun stillCurrent() = current == epoch && endpoint === api && failure === connection
        mutable.value = mutable.value.copy(powerBusy = true, message = null)
        scope.launch {
            try {
                val action = if (cancel) "cancel" else "shutdown"
                val result = parsePower(org.json.JSONObject(String(api.bytes("/api/power/$action", saved.token, "", timeoutSeconds = 4), Charsets.UTF_8)))
                if (stillCurrent()) { applyPower(result); mutable.value = mutable.value.copy(message = result.message) }
            } catch (e: CancellationException) { throw e
            } catch (e: Exception) {
                if (stillCurrent()) {
                    mutable.value = mutable.value.copy(message = if (e is ApiError) e.message else
                        "Power response lost. The countdown may still be active; cancellation or power-off is not confirmed. Check the PC before retrying.")
                    if (e !is ApiError || e.code == 401) connection?.complete(Unit)
                }
            } finally {
                if (stillCurrent()) mutable.value = mutable.value.copy(powerBusy = false)
            }
        }
    }

    fun launch(game: Game) = command(game, "launch")

    fun stopGame(game: Game, force: Boolean = false) = command(game, if (force) "force-stop" else "stop")

    private fun command(game: Game, action: String) {
        val api = endpoint ?: return
        val latest = mutable.value.games.firstOrNull { it.id == game.id } ?: return
        if (!mutable.value.online || game.id in mutable.value.commanding) return
        if (action == "launch") {
            if (latest.status != "Stopped") return
        } else if (!latest.canTrackStatus || latest.status != "Running") return
        val current = epoch
        val connection = failure
        fun stillCurrent() = current == epoch && endpoint === api && failure === connection
        mutable.value = mutable.value.copy(commanding = mutable.value.commanding + game.id, message = null)
        scope.launch {
            try {
                val result = org.json.JSONObject(String(api.bytes("/api/games/${validId(game.id)}/$action", saved.token, ""), Charsets.UTF_8))
                if (stillCurrent()) mutable.value = mutable.value.copy(message = result.getString("message"))
            } catch (e: CancellationException) { throw e
            } catch (e: Exception) {
                if (stillCurrent()) {
                    mutable.value = mutable.value.copy(message = when (e) {
                        is ApiError -> e.message
                        else -> "Command response lost. Check the PC before retrying; it may already have taken effect."
                    })
                    if (e !is ApiError || e.code == 401) connection?.complete(Unit)
                }
            } finally {
                if (stillCurrent()) mutable.value = mutable.value.copy(commanding = mutable.value.commanding - game.id)
            }
        }
    }

    suspend fun cover(game: Game): Bitmap? = artwork(game, hero = false)
    suspend fun hero(game: Game): Bitmap? = artwork(game, hero = true)

    private suspend fun artwork(game: Game, hero: Boolean): Bitmap? {
        val api = endpoint ?: return null
        if (!mutable.value.online) return null
        val path = (if (hero) game.heroUrl else game.coverUrl) ?: return null
        // Each image type is restricted to this game's exact authenticated route.
        val expected = "/api/games/${game.id}/${if (hero) "hero" else "cover"}"
        if (path != expected && !Regex(Regex.escape(expected) + "\\?v=[A-Za-z0-9._~%-]+").matches(path)) return null
        val bytes = api.bytes(path, saved.token)
        return withContext(Dispatchers.Default) {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return@withContext null
            // inSampleSize defaults to 0, so start at 1 before using it as a divisor.
            val options = BitmapFactory.Options().apply { inSampleSize = 1 }
            val maxWidth = if (hero) 1280 else 720
            val maxHeight = if (hero) 720 else 1080
            while (bounds.outWidth / options.inSampleSize > maxWidth || bounds.outHeight / options.inSampleSize > maxHeight) {
                options.inSampleSize *= 2
            }
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)
        }
    }
    private class IdentityChanged : IOException()
}
