package com.gamelauncher.companion

import android.graphics.Bitmap
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.google.zxing.client.android.Intents
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import java.util.Locale

class MainActivity : ComponentActivity() {
    private lateinit var model: CompanionModel
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        model = ViewModelProvider(this)[CompanionModel::class.java]
        setContent { LauncherTheme { CompanionApp(model) } }
    }
    override fun onResume() { super.onResume(); model.resume() }
    override fun onPause() { model.pause(); super.onPause() }
}

@Composable
private fun CompanionApp(model: CompanionModel) {
    val sessions by model.sessions.collectAsStateWithLifecycle()
    val storageError by model.storageError.collectAsStateWithLifecycle()
    var showPair by rememberSaveable { mutableStateOf(false) }
    var removing by remember { mutableStateOf<PcSession?>(null) }
    var resetting by remember { mutableStateOf(false) }
    val pager = rememberPagerState(pageCount = { sessions.size })
    val scope = rememberCoroutineScope()
    Surface(Modifier.fillMaxSize()) {
        Column(Modifier.safeDrawingPadding().fillMaxSize()) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Image(painterResource(R.drawable.brand_mark), contentDescription = null, modifier = Modifier.height(28.dp))
                Spacer(Modifier.width(10.dp))
                Text("Launchpad", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                TextButton(onClick = { model.pairingError.value = null; showPair = true }, enabled = storageError == null) { Text("Add PC") }
            }
            if (storageError != null) {
                Column(Modifier.padding(16.dp)) {
                    Text(storageError!!, color = MaterialTheme.colorScheme.error)
                    TextButton(onClick = { resetting = true }) { Text("Reset local pairings") }
                }
            }
            if (sessions.isEmpty()) {
                Column(Modifier.weight(1f).padding(24.dp), verticalArrangement = Arrangement.Center) {
                    Image(painterResource(R.drawable.brand_mark), contentDescription = null, modifier = Modifier.height(96.dp))
                    Spacer(Modifier.height(24.dp))
                    Text("Your PCs. Your games.", style = MaterialTheme.typography.headlineMedium)
                    Spacer(Modifier.height(12.dp))
                    Text("Open Companion on your Windows launcher, enable the LAN server, and request a pairing code. Connect this phone to the same trusted network.")
                    Spacer(Modifier.height(20.dp))
                    Button(onClick = { showPair = true }, enabled = storageError == null) { Text("Pair a PC") }
                }
            } else {
                LazyRow(contentPadding = PaddingValues(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(sessions, key = { it.saved.device.deviceId }) { session ->
                        val index = sessions.indexOf(session)
                        FilterChip(selected = pager.currentPage == index,
                            onClick = { scope.launch { pager.animateScrollToPage(index) } },
                            label = { Text(session.saved.device.name, maxLines = 1) })
                    }
                }
                HorizontalPager(state = pager, key = { sessions[it].saved.device.deviceId }, modifier = Modifier.weight(1f)) { page ->
                    val session = sessions[page]
                    PcPage(session, reconnect = { model.reconnect(session) }, remove = { removing = session })
                }
            }
        }
    }
    if (showPair) PairDialog(model, dismiss = { showPair = false })
    removing?.let { session ->
        AlertDialog(onDismissRequest = { removing = null }, title = { Text("Remove ${session.saved.device.name}?") },
            text = { Text("This deletes this phone’s saved token and connection. It does not remove games or revoke the token on the PC. Revoke it on the PC if this phone is lost or untrusted.") },
            confirmButton = { TextButton(onClick = { model.remove(session); removing = null }) { Text("Remove") } },
            dismissButton = { TextButton(onClick = { removing = null }) { Text("Cancel") } })
    }
    if (resetting) AlertDialog(onDismissRequest = { resetting = false }, title = { Text("Reset all local pairings?") },
        text = { Text("You will need a new code from every PC. No games will be removed.") },
        confirmButton = { TextButton(onClick = { model.reset(); resetting = false }) { Text("Reset") } },
        dismissButton = { TextButton(onClick = { resetting = false }) { Text("Cancel") } })
}

@Composable
private fun PairDialog(model: CompanionModel, dismiss: () -> Unit) {
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("") }
    var code by rememberSaveable { mutableStateOf("") }
    var trusted by rememberSaveable { mutableStateOf(false) }
    var expectedDeviceId by rememberSaveable { mutableStateOf<String?>(null) }
    var deviceName by rememberSaveable { mutableStateOf<String?>(null) }
    var scanMessage by rememberSaveable { mutableStateOf<String?>(null) }
    val busy by model.busy.collectAsStateWithLifecycle()
    val error by model.pairingError.collectAsStateWithLifecycle()
    val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        trusted = false
        expectedDeviceId = null
        deviceName = null
        code = ""
        model.pairingError.value = null
        val contents = result.contents
        if (contents == null) {
            scanMessage = if (result.originalIntent?.getBooleanExtra(Intents.Scan.MISSING_CAMERA_PERMISSION, false) == true)
                "Camera permission was denied. Enter manually, or enable Camera in Android app settings and scan again."
            else "Scan cancelled or camera unavailable. Try again or enter manually."
        } else {
            try {
                require(result.formatName == ScanOptions.QR_CODE) { "Only pairing QR codes are supported" }
                val qr = PairingQr.parse(contents)
                host = qr.host
                port = qr.port.toString()
                code = qr.code
                expectedDeviceId = qr.deviceId
                deviceName = qr.deviceName
                scanMessage = null
            } catch (_: Exception) {
                // Do not display/log the raw payload or parser exception (it can contain the code).
                scanMessage = "Not a valid supported Launchpad pairing QR. Scan a fresh QR from Windows, or enter manually."
            }
        }
    }
    fun enterManually() {
        expectedDeviceId = null
        deviceName = null
        trusted = false
        scanMessage = null
        model.pairingError.value = null
    }
    AlertDialog(onDismissRequest = { if (!busy) dismiss() }, title = { Text("Pair a Windows PC") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Use the host, port, and new 8-digit code from the Windows Companion page. Enter a hostname or IPv4 address, not a URL.")
                Text("Scan uses this phone’s camera only; no Google services or internet connection is needed. Camera access is optional for manual entry.", style = MaterialTheme.typography.bodySmall)
                Row {
                    TextButton(enabled = !busy, onClick = {
                        enterManually()
                        code = ""
                        try {
                            scanner.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                                .setPrompt("Scan the pairing QR on your Windows Companion page")
                                .setBeepEnabled(false).setBarcodeImageEnabled(false).setOrientationLocked(false)
                                .addExtra(Intents.Scan.SHOW_MISSING_CAMERA_PERMISSION_DIALOG, false))
                        } catch (_: Exception) {
                            scanMessage = "Camera scanner could not open. Enter the pairing details manually."
                        }
                    }) { Text("Scan QR") }
                    TextButton(enabled = !busy, onClick = { enterManually(); code = "" }) { Text("Enter manually") }
                }
                if (expectedDeviceId != null) {
                    Text("Confirm this computer", fontWeight = FontWeight.SemiBold)
                    Text(deviceName.orEmpty().ifEmpty { "Unnamed PC" })
                    Text("$host:$port")
                    Text("Check the name and address against Windows, then confirm your trusted network and tap Pair. Scanning alone never pairs.", style = MaterialTheme.typography.bodySmall)
                }
                scanMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                OutlinedTextField(value = host, onValueChange = { enterManually(); host = it }, label = { Text("PC hostname or IPv4") }, singleLine = true, enabled = !busy)
                OutlinedTextField(value = port, onValueChange = { enterManually(); port = it }, label = { Text("Port shown on PC") }, singleLine = true, enabled = !busy,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number))
                OutlinedTextField(value = code, onValueChange = { enterManually(); code = it }, label = { Text("8-digit pairing code") }, singleLine = true, enabled = !busy,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword))
                Text("HTTP is not encrypted. Anyone controlling this network can intercept codes, tokens, or commands. Use only your trusted private LAN; never public Wi-Fi or port forwarding.", style = MaterialTheme.typography.bodySmall)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = trusted, onCheckedChange = { trusted = it }, enabled = !busy)
                    Text("I trust this network", style = MaterialTheme.typography.bodyMedium)
                }
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                if (busy) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = { TextButton(enabled = !busy && trusted, onClick = { model.pair(host, port, code, expectedDeviceId, dismiss) }) { Text("Pair") } },
        dismissButton = { TextButton(enabled = !busy, onClick = dismiss) { Text("Cancel") } })
}

@Composable
private fun PcPage(session: PcSession, reconnect: () -> Unit, remove: () -> Unit) {
    val ui by session.state.collectAsStateWithLifecycle()
    val sorted = remember(ui.games) { ui.games.sortedWith(compareBy<Game> { if (it.status == "Running") 0 else 1 }
        .thenBy { it.name.lowercase(Locale.ROOT) }.thenBy { it.id }) }
    Column(Modifier.fillMaxSize()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
            Text(ui.device.name, style = MaterialTheme.typography.headlineSmall)
            Text(ui.endpoint, style = MaterialTheme.typography.bodySmall)
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(ui.connection, color = if (ui.online) LauncherColors.SuccessText else MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f), style = MaterialTheme.typography.labelLarge)
                TextButton(onClick = reconnect) { Text("Reconnect") }
                TextButton(onClick = remove) { Text("Remove") }
            }
            if (!ui.online && ui.games.isNotEmpty()) Text("Last known library • controls disabled", style = MaterialTheme.typography.bodySmall)
            ui.message?.let { Text(it, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(bottom = 8.dp)) }
            if (ui.games.any { !it.canTrackStatus }) Text("For shortcut/Steam games, set the actual game’s tracking executable in Windows Edit to enable Playing and Stop.",
                style = MaterialTheme.typography.bodySmall, color = LauncherColors.Muted)
        }
        if (sorted.isEmpty()) {
            Box(Modifier.weight(1f).fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
                Text(if (ui.online) "No games yet. Add games in the Windows launcher." else "Connect to this PC to load its library.")
            }
        } else {
            LazyVerticalGrid(columns = GridCells.Fixed(2), modifier = Modifier.weight(1f),
                contentPadding = PaddingValues(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                items(sorted, key = { it.id }) { game -> GameCard(session, ui, game) }
            }
        }
    }
}

@Composable
private fun GameCard(session: PcSession, ui: PcUi, game: Game) {
    val cover by produceState<Bitmap?>(null, session, game.coverUrl, ui.online to ui.generation) {
        if (ui.online) {
            value = try { session.cover(game) } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
        }
    }
    var showStop by remember(session, game.id) { mutableStateOf(false) }
    var confirmForce by remember(session, game.id) { mutableStateOf(false) }
    val canStop = ui.online && game.canTrackStatus && game.status == "Running" && game.id !in ui.commanding
    LaunchedEffect(canStop) { if (!canStop) { showStop = false; confirmForce = false } }
    if (showStop && canStop) AlertDialog(
        onDismissRequest = { showStop = false; confirmForce = false },
        title = { Text(if (confirmForce) "Force stop ${game.name}?" else "Stop ${game.name}?") },
        text = { Column {
            Text(if (confirmForce) "Unsaved progress will be lost. This terminates only the tracked game process, not its child processes or bundled apps."
                else "Ask the game to close normally. A save or exit dialog may still need your attention on the PC.")
            if (!confirmForce) TextButton(onClick = { confirmForce = true }) { Text("Force stop instead…", color = MaterialTheme.colorScheme.error) }
        } },
        confirmButton = { TextButton(onClick = {
            session.stopGame(game, force = confirmForce)
            showStop = false
            confirmForce = false
        }) { Text(if (confirmForce) "Force stop" else "Close normally") } },
        dismissButton = { TextButton(onClick = { showStop = false; confirmForce = false }) { Text("Cancel") } })
    val playing = game.status == "Running"
    // Only active states are shown; "Stopped" just means not running, so it gets no label.
    val badge = when (game.status) {
        "Running" -> if (ui.online) "● Playing" else "● Was playing"
        "Starting" -> "Starting…"
        "Stopping" -> "Closing…"
        else -> null
    }
    // Windows .entry-card: surface fill, 1px line border; running games pick up the success border.
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = LauncherColors.Surface),
        border = BorderStroke(1.dp, if (playing) LauncherColors.SuccessBorder else LauncherColors.Line)) {
        Box(Modifier.fillMaxWidth().aspectRatio(2f / 3f).background(LauncherColors.SurfaceRaised), contentAlignment = Alignment.Center) {
            if (cover != null) Image(cover!!.asImageBitmap(), contentDescription = "${game.name} cover",
                modifier = Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
            else Text(game.name, modifier = Modifier.padding(12.dp), maxLines = 4, overflow = TextOverflow.Ellipsis,
                color = LauncherColors.LaunchText, style = MaterialTheme.typography.titleMedium)
            if (badge != null) Surface(modifier = Modifier.align(Alignment.TopStart).padding(8.dp),
                color = if (playing) LauncherColors.SuccessBg else LauncherColors.AccentSoft,
                shape = MaterialTheme.shapes.small,
                border = BorderStroke(1.dp, if (playing) LauncherColors.SuccessBorder else LauncherColors.LaunchBorder)) {
                Text(badge, color = if (playing) LauncherColors.SuccessText else LauncherColors.LaunchText, fontWeight = FontWeight.Bold,
                    style = MaterialTheme.typography.labelMedium, modifier = Modifier.padding(horizontal = 8.dp, vertical = 5.dp))
            }
        }
        Column(Modifier.padding(10.dp)) {
            Text(game.name, maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.titleSmall)
            // Windows .launch button: soft orange fill with a warm border rather than a solid accent block.
            Button(onClick = { if (canStop) showStop = true else session.launch(game) }, modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                shape = RoundedCornerShape(8.dp),
                colors = ButtonDefaults.buttonColors(containerColor = LauncherColors.LaunchBg, contentColor = LauncherColors.LaunchText,
                    disabledContainerColor = LauncherColors.SurfaceRaised, disabledContentColor = LauncherColors.Muted),
                border = BorderStroke(1.dp, LauncherColors.LaunchBorder),
                enabled = ui.online && game.id !in ui.commanding && (game.status == "Stopped" || canStop)) {
                Text(when {
                    game.id in ui.commanding -> "Sending…"
                    game.status == "Running" -> "Stop game"
                    game.status == "Starting" -> "Starting…"
                    game.status == "Stopping" -> "Closing…"
                    else -> "Launch"
                })
            }
        }
    }
}
