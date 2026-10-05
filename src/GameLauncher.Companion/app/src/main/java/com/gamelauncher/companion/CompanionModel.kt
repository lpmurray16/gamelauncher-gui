package com.gamelauncher.companion

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONObject

class CompanionModel(application: Application) : AndroidViewModel(application) {
    private val store = PcStore(application)
    private val writes = Mutex()
    private val mutableSessions = MutableStateFlow<List<PcSession>>(emptyList())
    val sessions = mutableSessions.asStateFlow()
    val busy = MutableStateFlow(false)
    val pairingError = MutableStateFlow<String?>(null)
    val storageError = MutableStateFlow<String?>(null)
    private var foreground = false

    init {
        viewModelScope.launch {
            writes.withLock {
                try {
                    val saved = withContext(Dispatchers.IO) { store.load() }
                    mutableSessions.value = saved.map { PcSession(it, viewModelScope) }
                    if (foreground) mutableSessions.value.forEach { it.reconnect() }
                } catch (e: Exception) {
                    storageError.value = "Saved pairings cannot be decrypted. Reset local pairings and pair again."
                }
            }
        }
    }
    fun resume() {
        if (foreground) return
        foreground = true
        mutableSessions.value.forEach { it.reconnect() }
    }
    fun pause() {
        foreground = false
        mutableSessions.value.forEach { it.stop() }
    }
    fun reconnect(session: PcSession) { if (foreground) session.reconnect() }

    fun pair(host: String, portText: String, code: String, expectedDeviceId: String?, completed: () -> Unit) {
        if (busy.value || storageError.value != null) return
        busy.value = true
        pairingError.value = null
        viewModelScope.launch {
            try {
                require(Regex("[0-9]{8}").matches(code)) { "Enter the 8-digit code shown on the PC" }
                val port = portText.toIntOrNull() ?: throw IllegalArgumentException("Enter a valid port")
                val expected = expectedDeviceId?.let { validId(it) }
                if (expected != null) {
                    PairingQr.requirePrivateIpv4(host)
                    require(port in 1024..65535) { "QR pairing port must be between 1024 and 65535" }
                }
                val api = LanEndpoint.resolve(host.trim(), port)
                val discovered = api.device()
                // Never send the one-time code to a different installation at the scanned address.
                require(expected == null || discovered.deviceId == expected) {
                    "This address belongs to a different PC than the QR. No code was sent. Scan a fresh QR on the intended PC."
                }
                val response = JSONObject(String(api.bytes("/api/pair", body = JSONObject().put("code", code).toString()), Charsets.UTF_8))
                val device = parseDevice(response.getJSONObject("device"))
                require(device.deviceId == discovered.deviceId) { "PC identity changed while pairing" }
                val token = response.getString("token")
                require(token.isNotBlank() && token.length <= 8192 && token.none { it.code < 33 || it.code > 126 }) { "Invalid pairing response" }
                val saved = SavedPc(device, host.trim(), port, token)
                writes.withLock {
                    val old = mutableSessions.value.firstOrNull { it.saved.device.deviceId == device.deviceId }
                    val remaining = mutableSessions.value.filterNot { it === old }
                    withContext(Dispatchers.IO) { store.save(remaining.map { it.saved } + saved) }
                    old?.stop()
                    val session = PcSession(saved, viewModelScope)
                    mutableSessions.value = remaining + session
                    if (foreground) session.reconnect()
                }
                completed()
            } catch (e: CancellationException) { throw e
            } catch (e: Exception) {
                pairingError.value = when (e) {
                    is IllegalArgumentException, is ApiError -> e.message ?: "Pairing failed"
                    else -> "Pairing failed. Check the address, port, code, trusted Wi-Fi, and PC firewall."
                }
            } finally { busy.value = false }
        }
    }
    fun remove(session: PcSession) {
        viewModelScope.launch {
            writes.withLock {
                val remaining = mutableSessions.value.filterNot { it === session }
                try {
                    withContext(Dispatchers.IO) { store.save(remaining.map { it.saved }) }
                    session.stop()
                    mutableSessions.value = remaining
                } catch (e: Exception) { storageError.value = "Could not save changes to local pairings. Try again or reset local pairings." }
            }
        }
    }
    fun reset() {
        viewModelScope.launch {
            writes.withLock {
                try {
                    withContext(Dispatchers.IO) { store.reset() }
                    mutableSessions.value.forEach { it.stop() }
                    mutableSessions.value = emptyList()
                    storageError.value = null
                } catch (e: Exception) { storageError.value = "Could not reset local pairings." }
            }
        }
    }
    override fun onCleared() {
        mutableSessions.value.forEach { it.stop() }
        super.onCleared()
    }
}
