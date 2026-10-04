package com.gamelauncher.companion

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.Proxy
import java.util.UUID
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

data class Device(val deviceId: String, val name: String, val version: String)
data class SavedPc(val device: Device, val host: String, val port: Int, val token: String)
data class Game(val id: String, val name: String, val coverUrl: String?, val status: String, val canTrackStatus: Boolean)
// Java SignalR's Gson deserializer uses these public, default-initialized fields.
class StatusEvent {
    @JvmField var gameId: String = ""
    @JvmField var status: String = "Stopped"
    @JvmField var revision: Long = 0
}
val statuses = setOf("Stopped", "Starting", "Running", "Stopping")
fun validId(id: String): String {
    require(Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").matches(id)) { "Invalid device or game ID" }
    return UUID.fromString(id).toString()
}
fun parseDevice(json: JSONObject): Device {
    require(json.getInt("protocolVersion") == 1) { "This PC uses an unsupported protocol" }
    return Device(validId(json.getString("deviceId")), json.getString("name"), json.getString("version"))
}

class ApiError(val code: Int, message: String) : IOException(message)

/** Resolve once per connection attempt, then pin every REST/WebSocket request to that local IPv4. */
class LanEndpoint private constructor(val base: String, val client: OkHttpClient) {
    companion object {
        suspend fun resolve(host: String, port: Int): LanEndpoint = withContext(Dispatchers.IO) {
            require(port in 1..65535) { "Port must be between 1 and 65535" }
            require(host.length <= 253 && host.split('.').all {
                it.length in 1..63 && Regex("[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?").matches(it)
            }) { "Enter only a hostname or IPv4 address, not a URL" }
            val address = InetAddress.getAllByName(host).firstOrNull { address ->
                if (address !is Inet4Address) false else {
                    val b = address.address.map { it.toInt() and 255 }
                    b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] in 16..31) ||
                        (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
                }
            } ?: throw IOException("Host must resolve to a private, link-local, or loopback IPv4 address")
            val ip = requireNotNull(address.hostAddress)
            val client = OkHttpClient.Builder().proxy(Proxy.NO_PROXY)
                .followRedirects(false).followSslRedirects(false).retryOnConnectionFailure(false)
                .dns(object : Dns {
                    // OkHttp 4's Dns is not a Kotlin fun interface, so a lambda cannot be SAM-converted.
                    override fun lookup(hostname: String): List<InetAddress> =
                        if (hostname == ip) listOf(address) else throw IOException("Endpoint changed")
                })
                .connectTimeout(8, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS)
                .callTimeout(20, TimeUnit.SECONDS).build()
            LanEndpoint("http://$ip:$port", client)
        }
    }
    suspend fun bytes(path: String, token: String? = null, body: String? = null): ByteArray {
        require(path.startsWith("/api/") && !path.contains("..") && !path.contains('#'))
        val builder = Request.Builder().url(base + path)
        if (token != null) builder.header("Authorization", "Bearer $token")
        if (body != null) builder.post(body.toRequestBody("application/json".toMediaType()))
        return client.newCall(builder.build()).awaitBytes()
    }
    suspend fun device() = parseDevice(JSONObject(String(bytes("/api/device"), Charsets.UTF_8)))
    suspend fun games(token: String): List<Game> {
        val array = JSONArray(String(bytes("/api/games", token), Charsets.UTF_8))
        return (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            val status = item.getString("status")
            require(status in statuses) { "Unsupported game status" }
            Game(validId(item.getString("id")), item.getString("name"),
                if (item.isNull("coverUrl")) null else item.getString("coverUrl"),
                status, item.getBoolean("canTrackStatus"))
        }.distinctBy { it.id }
    }
}

private suspend fun Call.awaitBytes(): ByteArray = suspendCancellableCoroutine { continuation ->
    continuation.invokeOnCancellation { cancel() }
    enqueue(object : Callback {
        override fun onFailure(call: Call, e: IOException) {
            if (continuation.isActive) continuation.resumeWithException(e)
        }
        override fun onResponse(call: Call, response: Response) {
            try {
                val bytes = response.use {
                    // Bound both covers and JSON; do not trust a Content-Length from the LAN.
                    val source = it.body?.source() ?: throw IOException("Empty response")
                    if (source.request(8L * 1024 * 1024 + 1)) throw IOException("Response too large")
                    val data = source.readByteArray()
                    if (!it.isSuccessful) {
                        val message = runCatching { JSONObject(String(data, Charsets.UTF_8)).getString("message") }
                            .getOrDefault("PC returned HTTP ${it.code}")
                        throw ApiError(it.code, message)
                    }
                    data
                }
                if (continuation.isActive) continuation.resume(bytes)
            } catch (e: Exception) {
                if (continuation.isActive) continuation.resumeWithException(e)
            }
        }
    })
}
