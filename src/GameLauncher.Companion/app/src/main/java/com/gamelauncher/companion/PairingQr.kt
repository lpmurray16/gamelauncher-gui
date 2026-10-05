package com.gamelauncher.companion

import android.util.JsonReader
import android.util.JsonToken
import java.io.StringReader

/** Only data, never a URL/deep link. Parsing does not connect or exchange the code. */
data class PairingQr(val host: String, val port: Int, val code: String, val deviceId: String, val deviceName: String) {
    companion object {
        fun parse(text: String): PairingQr {
            require(text.length in 1..4096) { "Pairing QR is empty or too large" }
            val strings = mutableMapOf<String, String>()
            val integers = mutableMapOf<String, Int>()
            val stringFields = setOf("format", "host", "code", "deviceId", "deviceName")
            val integerFields = setOf("formatVersion", "port", "protocolVersion")
            val seen = mutableSetOf<String>()
            // JsonReader in strict mode rejects non-JSON syntax. Check tokens before reading:
            // nextString/nextInt alone can coerce values, as can JSONObject getters.
            JsonReader(StringReader(text)).use { reader ->
                reader.isLenient = false
                reader.beginObject()
                while (reader.hasNext()) {
                    val key = reader.nextName()
                    require(seen.add(key)) { "Duplicate pairing QR field" }
                    when (key) {
                        in stringFields -> {
                            require(reader.peek() == JsonToken.STRING) { "Invalid pairing QR field type" }
                            strings[key] = reader.nextString()
                        }
                        in integerFields -> {
                            require(reader.peek() == JsonToken.NUMBER) { "Invalid pairing QR field type" }
                            val number = reader.nextString()
                            require(Regex("0|[1-9][0-9]*").matches(number)) { "Pairing QR requires integer values" }
                            integers[key] = number.toIntOrNull() ?: throw IllegalArgumentException("Invalid pairing QR integer")
                        }
                        else -> throw IllegalArgumentException("Unknown pairing QR field")
                    }
                }
                reader.endObject()
                require(reader.peek() == JsonToken.END_DOCUMENT) { "Unexpected data after pairing QR" }
            }
            require(seen == stringFields + integerFields) { "Incomplete pairing QR" }
            require(strings.getValue("format") == "gamelauncher-pair" && integers.getValue("formatVersion") == 1) {
                "Not a supported Game Launcher pairing QR"
            }
            require(integers.getValue("protocolVersion") == 1) { "This PC uses an unsupported protocol" }
            val host = strings.getValue("host")
            requirePrivateIpv4(host)
            val port = integers.getValue("port")
            require(port in 1024..65535) { "Pairing QR port must be between 1024 and 65535" }
            val code = strings.getValue("code")
            require(Regex("[0-9]{8}").matches(code)) { "Pairing QR must contain an 8-digit code" }
            val name = strings.getValue("deviceName")
            require(name.length <= 256) { "Pairing QR computer name is too long" }
            return PairingQr(host, port, code, validId(strings.getValue("deviceId")), name)
        }

        fun requirePrivateIpv4(host: String) {
            val parts = host.split('.')
            require(parts.size == 4 && parts.all {
                Regex("0|[1-9][0-9]{0,2}").matches(it) && (it.toIntOrNull() ?: 256) in 0..255
            }) { "Pairing QR must contain a private IPv4 address" }
            val bytes = parts.map { it.toInt() }
            require(bytes[0] == 10 || (bytes[0] == 172 && bytes[1] in 16..31) ||
                (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254)) {
                "Pairing QR must contain a private or link-local IPv4 address, not loopback"
            }
        }
    }
}
