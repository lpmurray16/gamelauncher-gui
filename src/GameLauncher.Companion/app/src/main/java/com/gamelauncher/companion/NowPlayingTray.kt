package com.gamelauncher.companion

import android.graphics.Bitmap
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CancellationException

private data class TrayArtwork(val bitmap: Bitmap? = null, val loading: Boolean = false)

/** One pinned tray per PC. Explicit selectors avoid competing with the outer PC-swipe gesture. */
@Composable
fun NowPlayingTray(session: PcSession, ui: PcUi, games: List<Game>, modifier: Modifier = Modifier) {
    if (games.isEmpty()) return
    var selectedId by rememberSaveable(session.saved.device.deviceId) { mutableStateOf(games.first().id) }
    val index = games.indexOfFirst { it.id == selectedId }.coerceAtLeast(0)
    val game = games[index]
    LaunchedEffect(game.id) { selectedId = game.id }
    Surface(modifier, shape = RoundedCornerShape(18.dp), color = LauncherColors.SurfaceRaised,
        border = BorderStroke(1.dp, LauncherColors.SuccessBorder), shadowElevation = 12.dp) {
        Column {
            key(game.id) { NowPlayingContent(session, ui, game) }
            if (games.size > 1) Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = { selectedId = games[(index - 1 + games.size) % games.size].id }) { Text("Previous") }
                Text("${index + 1} / ${games.size}", style = MaterialTheme.typography.labelMedium, color = LauncherColors.Muted)
                TextButton(onClick = { selectedId = games[(index + 1) % games.size].id }) { Text("Next") }
            }
        }
    }
}

@Composable
private fun NowPlayingContent(session: PcSession, ui: PcUi, game: Game) {
    val artwork by produceState(TrayArtwork(), session, game.id, game.heroUrl, ui.online to ui.generation) {
        value = TrayArtwork(loading = ui.online && game.heroUrl != null)
        if (ui.online && game.heroUrl != null) {
            val bitmap = try { session.hero(game) }
                catch (e: CancellationException) { throw e }
                catch (_: Exception) { null }
            value = TrayArtwork(bitmap = bitmap)
        }
    }
    var showStop by remember { mutableStateOf(false) }
    val canStop = ui.online && game.canTrackStatus && game.status == "Running" && game.id !in ui.commanding
    LaunchedEffect(canStop) { if (!canStop) showStop = false }
    if (showStop && canStop) StopGameDialog(session, game, dismiss = { showStop = false })
    Box(Modifier.fillMaxWidth().heightIn(min = 132.dp).background(LauncherColors.SurfaceRaised)) {
        artwork.bitmap?.let { bitmap ->
            Image(bitmap.asImageBitmap(), contentDescription = null, contentScale = ContentScale.Crop,
                modifier = Modifier.matchParentSize())
        }
        Box(Modifier.matchParentSize().background(Brush.horizontalGradient(
            listOf(Color.Black.copy(alpha = 0.88f), Color.Black.copy(alpha = 0.55f)))))
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(when {
                !ui.online -> "OFFLINE • LAST KNOWN GAME"
                game.status == "Stopping" -> "CLOSING…"
                else -> "● NOW PLAYING"
            }, color = LauncherColors.SuccessText, style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold)
            Text(game.name, color = LauncherColors.Text, style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f).padding(end = 8.dp)) {
                    if (artwork.loading) Row(verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(Modifier.size(14.dp), color = LauncherColors.Muted, strokeWidth = 2.dp)
                        Spacer(Modifier.width(8.dp))
                        Text("Loading background…", color = LauncherColors.Muted, style = MaterialTheme.typography.bodySmall)
                    }
                    else if (artwork.bitmap == null) Text(
                        if (!ui.online) "Artwork unavailable offline" else "No background artwork available",
                        color = LauncherColors.Muted, style = MaterialTheme.typography.bodySmall)
                }
                Button(onClick = { showStop = true }, enabled = canStop, shape = RoundedCornerShape(8.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = LauncherColors.DangerBg, contentColor = LauncherColors.DangerText)) {
                    Text(if (game.id in ui.commanding) "Sending…" else "Stop game")
                }
            }
        }
    }
}

// Shared by the normal library card and pinned tray so neither bypasses force-stop confirmation.
@Composable
fun StopGameDialog(session: PcSession, game: Game, dismiss: () -> Unit) {
    var confirmForce by remember(session, game.id) { mutableStateOf(false) }
    AlertDialog(onDismissRequest = dismiss,
        title = { Text(if (confirmForce) "Force stop ${game.name}?" else "Stop ${game.name}?") },
        text = { Column {
            Text(if (confirmForce) "Unsaved progress will be lost. This terminates only the tracked game process, not its child processes or bundled apps."
                else "Ask the game to close normally. A save or exit dialog may still need your attention on the PC.")
            if (!confirmForce) TextButton(onClick = { confirmForce = true }) {
                Text("Force stop instead…", color = MaterialTheme.colorScheme.error)
            }
        } },
        confirmButton = { TextButton(onClick = { session.stopGame(game, force = confirmForce); dismiss() }) {
            Text(if (confirmForce) "Force stop" else "Close normally")
        } },
        dismissButton = { TextButton(onClick = dismiss) { Text("Cancel") } })
}
