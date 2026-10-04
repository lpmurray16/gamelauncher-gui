package com.gamelauncher.companion

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Mirrors the Windows launcher palette in src/GameLauncher/wwwroot/site.css.
object LauncherColors {
    val Background = Color(0xFF101113)      // --bg
    val Sidebar = Color(0xFF141517)         // --sidebar
    val Surface = Color(0xFF191B1E)         // --surface
    val SurfaceRaised = Color(0xFF202226)   // --surface-raised
    val Selected = Color(0xFF25272A)        // .tab.selected
    val Line = Color(0xFF303237)            // --line
    val LineStrong = Color(0xFF5C5D61)      // .secondary:hover border
    val Muted = Color(0xFFA0A2AA)           // --muted
    val Text = Color(0xFFF1F0ED)            // --text
    val Accent = Color(0xFFFF8A47)          // --accent
    val AccentHover = Color(0xFFFFA46E)     // --accent-hover
    val AccentSoft = Color(0xFF302219)      // --accent-soft
    val OnAccent = Color(0xFF24160E)        // .primary text
    val LaunchBg = Color(0xFF2E261F)        // .launch
    val LaunchText = Color(0xFFFFB07C)      // .launch text
    val LaunchBorder = Color(0xFF55402E)    // .launch border
    val Danger = Color(0xFFFF9A92)          // --danger
    val DangerBg = Color(0xFF352421)        // .danger
    val DangerText = Color(0xFFFFB5AB)      // .danger text
    val SuccessBg = Color(0xFF15231C)       // .success-panel
    val SuccessBorder = Color(0xFF315443)   // .success-panel border
    val SuccessText = Color(0xFF98E0AC)     // .success-panel text
}

private val LauncherColorScheme = darkColorScheme(
    primary = LauncherColors.Accent,
    onPrimary = LauncherColors.OnAccent,
    primaryContainer = LauncherColors.LaunchBg,
    onPrimaryContainer = LauncherColors.LaunchText,
    secondary = LauncherColors.AccentHover,
    onSecondary = LauncherColors.OnAccent,
    secondaryContainer = LauncherColors.AccentSoft,
    onSecondaryContainer = LauncherColors.LaunchText,
    tertiary = LauncherColors.SuccessText,
    onTertiary = LauncherColors.SuccessBg,
    background = LauncherColors.Background,
    onBackground = LauncherColors.Text,
    surface = LauncherColors.Background,
    onSurface = LauncherColors.Text,
    surfaceVariant = LauncherColors.SurfaceRaised,
    onSurfaceVariant = LauncherColors.Muted,
    surfaceContainerLowest = LauncherColors.Background,
    surfaceContainerLow = LauncherColors.Sidebar,
    surfaceContainer = LauncherColors.Surface,
    surfaceContainerHigh = LauncherColors.SurfaceRaised,
    surfaceContainerHighest = LauncherColors.Selected,
    outline = LauncherColors.LineStrong,
    outlineVariant = LauncherColors.Line,
    error = LauncherColors.Danger,
    onError = LauncherColors.OnAccent,
    errorContainer = LauncherColors.DangerBg,
    onErrorContainer = LauncherColors.DangerText,
)

@Composable
fun LauncherTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = LauncherColorScheme, content = content)
}
