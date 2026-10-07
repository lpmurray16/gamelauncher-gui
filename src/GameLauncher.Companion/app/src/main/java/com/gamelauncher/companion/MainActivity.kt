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
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
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
    var showTips by rememberSaveable { mutableStateOf(false) }
    var removing by remember { mutableStateOf<PcSession?>(null) }
    var resetting by remember { mutableStateOf(false) }
    val pager = rememberPagerState(pageCount = { sessions.size })
    val scope = rememberCoroutineScope()
    Surface(Modifier.fillMaxSize()) {
        Column(Modifier.safeDrawingPadding().fillMaxSize()) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Image(painterResource(R.drawable.brand_mark), contentDescription = null, modifier = Modifier.height(28.dp))
                Spacer(Modifier.width(10.dp))
                Text("Launchpad Companion", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                TextButton(onClick = { showTips = true }) { Text("Tips") }
                OutlinedIconButton(onClick = { model.pairingError.value = null; showPair = true }, enabled = storageError == null) {
                    Icon(painterResource(R.drawable.ic_add), contentDescription = "Add PC")
                }
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
                    Text("Pair a PC to get started. Need help? Open Tips.")
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
    if (showTips) TipsDialog(dismiss = { showTips = false })
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
    val sorted = remember(ui.games) { ui.games.sortedWith(compareBy<Game> { it.name.lowercase(Locale.ROOT) }.thenBy { it.id }) }
    val running = remember(sorted) { sorted.filter { it.status == "Running" || it.status == "Stopping" } }
    var actionsExpanded by remember(session) { mutableStateOf(false) }
    var trayHeightPx by remember(session) { mutableStateOf(0) }
    val density = LocalDensity.current
    val traySpace = if (running.isEmpty()) 0.dp else with(density) { trayHeightPx.toDp() }
    Column(Modifier.fillMaxSize()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(ui.device.name, style = MaterialTheme.typography.headlineSmall,
                        maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                    if (ui.online) Icon(painterResource(R.drawable.ic_connected), contentDescription = "Connected",
                        tint = LauncherColors.SuccessText, modifier = Modifier.size(22.dp))
                }
                Box {
                    OutlinedIconButton(onClick = { actionsExpanded = true }) {
                        Icon(painterResource(R.drawable.ic_more), contentDescription = "PC actions")
                    }
                    DropdownMenu(expanded = actionsExpanded, onDismissRequest = { actionsExpanded = false }) {
                        DropdownMenuItem(text = { Text("Reconnect") }, onClick = { actionsExpanded = false; reconnect() })
                        DropdownMenuItem(text = { Text("Remove PC", color = MaterialTheme.colorScheme.error) },
                            onClick = { actionsExpanded = false; remove() })
                    }
                }
            }
            if (!ui.online) Text(ui.connection, color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.labelLarge)
            PcPowerControls(session, ui)
            if (!ui.online && ui.games.isNotEmpty()) Text("Last known library • controls disabled", style = MaterialTheme.typography.bodySmall)
            ui.message?.let { Text(it, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(bottom = 8.dp)) }
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (sorted.isEmpty()) {
                Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
                    Text(if (ui.online) "No games yet. Add games in the Windows launcher." else "Connect to this PC to load its library.")
                }
            } else {
                LazyVerticalGrid(columns = GridCells.Fixed(2), modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(start = 12.dp, top = 12.dp, end = 12.dp, bottom = traySpace + 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    items(sorted, key = { it.id }) { game -> GameCard(session, ui, game) }
                }
            }
            if (running.isNotEmpty()) NowPlayingTray(session, ui, running,
                Modifier.align(Alignment.BottomCenter).fillMaxWidth().onSizeChanged { trayHeightPx = it.height }.padding(12.dp))
        }
    }
}

@Composable
private fun PcPowerControls(session: PcSession, ui: PcUi) {
    val power = ui.power ?: return
    var confirmShutdown by remember(session) { mutableStateOf(false) }
    val canShutdown = ui.online && power.remoteShutdownAllowed && !power.pending && !power.dispatching && !ui.powerBusy
    LaunchedEffect(canShutdown) { if (!canShutdown) confirmShutdown = false }
    if (confirmShutdown && canShutdown) AlertDialog(
        onDismissRequest = { confirmShutdown = false },
        title = { Text("Shut down ${ui.device.name}?") },
        text = { Text("Starts a 15-second countdown on this PC. Save your work first. You can cancel during the countdown. Apps will not be forced closed and may block shutdown. Keep Launchpad running; once sent to Windows, this app cannot cancel it or confirm power-off.") },
        confirmButton = { TextButton(onClick = { confirmShutdown = false; session.shutdownPc() }) { Text("Shut down PC") } },
        dismissButton = { TextButton(onClick = { confirmShutdown = false }) { Text("Keep PC on") } })
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        if (power.pending) {
            Text(if (ui.online) "Shutdown in ~${power.remainingSeconds}s" else "Shutdown may still be pending",
                modifier = Modifier.weight(1f), style = MaterialTheme.typography.labelLarge)
            OutlinedButton(enabled = ui.online && !ui.powerBusy, onClick = { session.shutdownPc(cancel = true) }) {
                Text(if (ui.powerBusy) "Sending…" else "Cancel shutdown")
            }
        } else {
            OutlinedButton(enabled = canShutdown, onClick = { confirmShutdown = true }) {
                Icon(painterResource(R.drawable.ic_power), contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text(if (ui.powerBusy) "Sending…" else "Shut down PC")
            }
        }
    }
    if (ui.online && !power.remoteShutdownAllowed && !power.pending)
        Text("Setup required — see Tips → PC shutdown.", style = MaterialTheme.typography.bodySmall)
    if (power.revision > 0) Text(if (ui.online) power.message else
        "Power state is unconfirmed while offline. Check the PC; a lost connection does not prove shutdown or cancellation.",
        style = MaterialTheme.typography.bodySmall)
}

@Composable
private fun GameCard(session: PcSession, ui: PcUi, game: Game) {
    val cover by produceState<Bitmap?>(null, session, game.coverUrl, ui.online to ui.generation) {
        if (ui.online) {
            value = try { session.cover(game) } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
        }
    }
    var showStop by remember(session, game.id) { mutableStateOf(false) }
    val canStop = ui.online && game.canTrackStatus && game.status == "Running" && game.id !in ui.commanding
    LaunchedEffect(canStop) { if (!canStop) showStop = false }
    if (showStop && canStop) StopGameDialog(session, game, dismiss = { showStop = false })
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
