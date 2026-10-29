using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameLauncher.Services;

public sealed record ThemeColorField(string Key, string Label, string CssVariable, string DefaultValue);

public sealed class ThemeSettings
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Colors { get; set; } = ThemePreferences.ColorFields
        .ToDictionary(x => x.Key, x => x.DefaultValue);
    public int PanelRadius { get; set; } = 14;
    public int ControlRadius { get; set; } = 8;
}

// Only a fixed palette and bounded radii are accepted, never user-supplied CSS or paths.
public sealed class ThemePreferences
{
    public static IReadOnlyList<ThemeColorField> ColorFields { get; } = Array.AsReadOnly(new[]
    {
        new ThemeColorField("Background", "Main background", "--bg", "#101113"),
        new ThemeColorField("Header", "Header background", "--theme-header-bg", "#101113"),
        new ThemeColorField("Surface", "Panels and cards", "--surface", "#191b1e"),
        new ThemeColorField("Raised", "Raised surfaces", "--surface-raised", "#202226"),
        new ThemeColorField("Accent", "Accent", "--accent", "#ff8a47"),
        new ThemeColorField("Text", "Primary text", "--text", "#f1f0ed"),
        new ThemeColorField("Muted", "Secondary text", "--muted", "#a0a2aa"),
        new ThemeColorField("Border", "Borders", "--line", "#303237")
    });

    private readonly object _gate = new();
    private readonly string _path;
    private ThemeSettings _current = new();
    public string? LoadWarning { get; private set; }

    public ThemePreferences(AppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "appearance.json");
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 16384) throw new ArgumentException("The theme file is too large.");
            var loaded = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(_path))
                ?? throw new ArgumentException("The theme file is empty.");
            Validate(loaded);
            _current = Copy(loaded);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // A corrupt theme must never prevent opening the app or reaching Restore defaults.
            LoadWarning = "Saved appearance could not be loaded; using defaults. " + error.Message;
        }
    }

    public ThemeSettings Get()
    {
        lock (_gate) return Copy(_current);
    }

    public static void Validate(ThemeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Version != 1) throw new ArgumentException("This theme version is not supported.");
        if (value.Colors is null || value.Colors.Count != ColorFields.Count)
            throw new ArgumentException("The theme must contain every supported color.");
        foreach (var field in ColorFields)
            if (!value.Colors.TryGetValue(field.Key, out var color) || color is null ||
                !Regex.IsMatch(color, @"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant))
                throw new ArgumentException($"{field.Label} must be a six-digit hex color, such as {field.DefaultValue}.");
        if (value.PanelRadius is < 0 or > 32 || value.ControlRadius is < 0 or > 24)
            throw new ArgumentException("Card corners must be 0–32 px; control corners must be 0–24 px.");
    }

    public void Save(ThemeSettings value)
    {
        Validate(value);
        var snapshot = Copy(value);
        lock (_gate)
        {
            var temporary = _path + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, _path, overwrite: true);
                if (File.ReadAllText(_path) != json)
                    throw new IOException("The appearance file could not be verified. Please save again.");
                _current = snapshot;
                LoadWarning = null;
            }
            finally
            {
                // Only our temporary configuration file; never touch library/game files.
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public string GetCss()
    {
        var theme = Get();
        var css = new StringBuilder(":root{");
        foreach (var field in ColorFields)
            css.Append(field.CssVariable).Append(':').Append(theme.Colors[field.Key]).Append(';');
        var accent = theme.Colors["Accent"];
        var background = theme.Colors["Background"];
        css.Append("--accent-hover:").Append(accent == "#ff8a47" ? "#ffa46e" : Mix(accent, "#ffffff", .2)).Append(';');
        css.Append("--accent-soft:").Append(accent == "#ff8a47" && background == "#101113" ? "#302219" : Mix(background, accent, .16)).Append(';');
        css.Append("--on-accent:").Append(Luminance(accent) > .179 ? "#16110e" : "#ffffff").Append(';');
        css.Append("--radius:").Append(theme.PanelRadius.ToString(CultureInfo.InvariantCulture)).Append("px;");
        css.Append("--control-radius:").Append(theme.ControlRadius.ToString(CultureInfo.InvariantCulture)).Append("px;");
        css.Append("color-scheme:").Append(Luminance(background) > .179 ? "light" : "dark").Append(';');
        return css.Append('}').ToString();
    }

    private static ThemeSettings Copy(ThemeSettings value) => new()
    {
        Version = value.Version,
        Colors = value.Colors.ToDictionary(x => x.Key, x => x.Value.ToLowerInvariant()),
        PanelRadius = value.PanelRadius,
        ControlRadius = value.ControlRadius
    };

    private static int Channel(string color, int start) => int.Parse(color.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static string Mix(string from, string to, double amount) => "#" + string.Concat(new[] { 1, 3, 5 }
        .Select(index => ((int)Math.Floor(Channel(from, index) * (1 - amount) + Channel(to, index) * amount + .5)).ToString("x2", CultureInfo.InvariantCulture)));
    private static double Luminance(string color) => new[] { 1, 3, 5 }.Select((index, channel) =>
    {
        var value = Channel(color, index) / 255d;
        var linear = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        return linear * new[] { .2126, .7152, .0722 }[channel];
    }).Sum();
}
