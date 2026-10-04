package com.gamelauncher.companion

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** Entire saved-PC document is AES-GCM encrypted; the key never leaves Android Keystore. */
class PcStore(context: Context) {
    private val prefs = context.getSharedPreferences("paired_pcs", Context.MODE_PRIVATE)
    private val alias = "game_launcher_pc_tokens_v1"
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(alias, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true).build())
        }.generateKey()
    }
    fun load(): List<SavedPc> {
        val encoded = prefs.getString("encrypted", null) ?: return emptyList()
        val packed = Base64.decode(encoded, Base64.NO_WRAP)
        require(packed.size > 28) { "Invalid saved pairing data" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, packed.copyOfRange(0, 12)))
        cipher.updateAAD(alias.toByteArray(Charsets.UTF_8))
        val array = JSONArray(String(cipher.doFinal(packed.copyOfRange(12, packed.size)), Charsets.UTF_8))
        return (0 until array.length()).map { index ->
            val pc = array.getJSONObject(index)
            SavedPc(parseDevice(pc.getJSONObject("device")), pc.getString("host"), pc.getInt("port"), pc.getString("token"))
        }.distinctBy { it.device.deviceId }
    }
    fun save(pcs: List<SavedPc>) {
        val array = JSONArray()
        pcs.forEach { pc ->
            array.put(JSONObject().put("host", pc.host).put("port", pc.port).put("token", pc.token)
                .put("device", JSONObject().put("deviceId", pc.device.deviceId).put("name", pc.device.name)
                    .put("version", pc.device.version).put("protocolVersion", 1)))
        }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        cipher.updateAAD(alias.toByteArray(Charsets.UTF_8))
        val encrypted = cipher.iv + cipher.doFinal(array.toString().toByteArray(Charsets.UTF_8))
        check(prefs.edit().putString("encrypted", Base64.encodeToString(encrypted, Base64.NO_WRAP)).commit()) {
            "Could not save paired PCs"
        }
    }
    fun reset() {
        check(prefs.edit().clear().commit()) { "Could not clear saved pairings" }
        // Recover from key invalidation as well as damaged ciphertext.
        KeyStore.getInstance("AndroidKeyStore").apply { load(null); deleteEntry(alias) }
    }
}
