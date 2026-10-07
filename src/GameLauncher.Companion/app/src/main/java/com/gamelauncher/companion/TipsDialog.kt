package com.gamelauncher.companion

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp

private val companionTips = listOf(
    "Pairing & connection" to "On your PC, open Launchpad → Settings → Manage companion access. Enable LAN access, save, and restart Launchpad. Generate a pairing QR / code.\n\nIn Launchpad Companion, tap + → Scan QR, or enter the connection details manually. Confirm the PC name/address and trusted network before pairing. Keep Launchpad running and both devices on the same trusted private network.\n\nTraffic is not encrypted. Do not use public Wi-Fi or expose the companion port to the internet.",
    "Switching & managing PCs" to "Tap a computer chip or swipe sideways to switch PCs. A green check beside the computer name means it is connected.\n\nOpen the three-dot PC actions menu for Reconnect or Remove PC. Remove only forgets this phone’s pairing; it does not delete games or revoke access on the PC. Revoke pairings in Launchpad if a phone is lost or untrusted.",
    "Games & Now playing" to "Add games and artwork in Launchpad on your PC. Tap Launch in Launchpad Companion to request a game launch. The library stays alphabetical.\n\nRunning games appear in the bottom Now playing tray, using background artwork. Previous/Next switches between active games. Close normally requests a clean exit; Force stop may lose unsaved progress and requires confirmation.\n\nFor shortcut or Steam games, open the game’s Edit page in Launchpad and set the tracking executable to the actual game executable—not Steam. Tracking enables Playing status and Stop controls.",
    "PC shutdown" to "On your PC, open Launchpad → Settings → PC power. Enable ‘Allow shutdown from Launchpad Companion’ and select ‘Save power preferences’. This is inside Launchpad, not Windows Settings.\n\nThe Shut down PC button becomes available when connected and permission is enabled. Save your work, then confirm to start the 15-second countdown. Cancel shutdown is available on either device during the countdown.\n\nDisconnecting or closing Launchpad Companion does not cancel the countdown. Closing Launchpad on the PC before dispatch does. Apps are not forced closed and may block shutdown. Once the request is sent to Windows, Launchpad cannot cancel it or confirm power-off.",
    "Troubleshooting & offline use" to "If disconnected, check that Launchpad is running with LAN access enabled, both devices are on the same trusted network, and the configured companion port is allowed through the PC firewall. Do not disable the firewall. Try Reconnect from the PC actions menu.\n\nOffline artwork and game status are last-known information; controls are disabled. A lost connection does not prove a game stopped or the PC shut down.\n\nIf shutdown controls are missing, update both Launchpad and Launchpad Companion. If shutdown is disabled while connected, check the permission under PC shutdown above. Authentication errors may require pairing again."
)

@Composable
fun TipsDialog(dismiss: () -> Unit) {
    var expanded by rememberSaveable { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = dismiss,
        title = { Text("Tips") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                companionTips.forEach { (title, body) ->
                    val open = expanded == title
                    OutlinedButton(
                        onClick = { expanded = if (open) null else title },
                        modifier = Modifier.fillMaxWidth().semantics { stateDescription = if (open) "Expanded" else "Collapsed" }
                    ) { Text(title, modifier = Modifier.weight(1f)); Text(if (open) "−" else "+") }
                    if (open) Text(body, modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.bodyMedium)
                }
            }
        },
        confirmButton = { TextButton(onClick = dismiss) { Text("Done") } }
    )
}
