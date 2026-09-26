using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KyeForge.App.Services;

/// <summary>
/// Live theme customization: publishes brush resources from Themes/Colors.xaml
/// so every DynamicResource reference in the app updates. Color changes can
/// crossfade smoothly (a timer writes interpolated brushes per tick).
/// Also broadcasts changes (background image etc.) via <see cref="Changed"/>.
/// </summary>
public static class Customization
{
    public static event Action? Changed;

    public enum BackgroundKind { None, Image, Gif }

    /// <summary>Current custom background path ("" = none). May be an image or GIF.</summary>
    public static string BackgroundPath { get; private set; } = "";

    /// <summary>What kind of background <see cref="BackgroundPath"/> is.</summary>
    public static BackgroundKind Kind { get; private set; } = BackgroundKind.None;

    // Video files are not supported as background: selecting one keeps
    // the current background instead of breaking it.
    private static readonly string[] UnsupportedVideoExtensions =
        { ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".m2v",
          ".m2ts", ".mts", ".ts", ".mkv", ".webm", ".flv", ".f4v", ".3gp", ".3g2", ".ogv" };

    public static BackgroundKind DetectKind(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return BackgroundKind.None;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".gif")
            return BackgroundKind.Gif;
        if (((IList<string>)UnsupportedVideoExtensions).Contains(ext))
            return BackgroundKind.None;
        return BackgroundKind.Image;
    }

    /// <summary>Background dim overlay strength 0..90 (percent).</summary>
    public static double BackgroundDim { get; private set; } = 55;

    /// <summary>Background blur radius 0..60 (px).</summary>
    public static double BackgroundBlur { get; private set; } = 0;

    private static readonly (string Key, string Hex)[] Defaults =
    {
        ("AccentBrush", "#28D7B7"),
        ("AccentGlowBrush", "#167B70"),
        ("BgDeepBrush", "#090B0F"),
        ("WindowBackgroundBrush", "#090B0F"),
        ("BgPanelBrush", "#101419"),
        ("BgCardBrush", "#151B22"),
        ("BgElevatedBrush", "#1B232B"),
        ("BgHoverBrush", "#222D35"),
        ("BorderBrush", "#2B353E"),
        ("BorderStrongBrush", "#46535D"),
        ("TextPrimaryBrush", "#F3F5FA"),
        ("TextSecondaryBrush", "#AEB8C2"),
        ("TextMutedBrush", "#78838E"),
    };

    // Light palette: only surfaces + text; accent stays user-defined.
    private static readonly (string Key, string Hex)[] LightDefaults =
    {
        ("BgDeepBrush", "#F4F6F9"),
        ("WindowBackgroundBrush", "#F4F6F9"),
        ("BgPanelBrush", "#EAEEF3"),
        ("BgCardBrush", "#FFFFFF"),
        ("BgElevatedBrush", "#F7F9FB"),
        ("BgHoverBrush", "#E8EDF2"),
        ("BorderBrush", "#D4DAE1"),
        ("BorderStrongBrush", "#B9C2CC"),
        ("TextPrimaryBrush", "#111820"),
        ("TextSecondaryBrush", "#4A5560"),
        ("TextMutedBrush", "#7A8590"),
    };

    // NOTE: brushes stored in Application.Resources are frozen by WPF on insert,
    // so they can never be animated in place. Crossfades run on a dispatcher
    // timer that writes an interpolated brush per tick; DynamicResource
    // consumers repaint every tick, producing a smooth transition.
    private static readonly TimeSpan AnimDuration = TimeSpan.FromMilliseconds(600);
    private static readonly IEasingFunction AnimEase =
        new CubicEase { EasingMode = EasingMode.EaseInOut };

    private sealed class ActiveAnim
    {
        public DateTime Start;
        public readonly List<(string Key, Color From, Color To)> Brushes = new();
        public bool HasGradient;
        public Color GradFrom0, GradTo0, GradFrom1, GradTo1;
    }

    private static ActiveAnim? _activeAnim;
    private static DispatcherTimer? _animTimer;

    /// <summary>Current UI theme: "dark" or "light".</summary>
    public static string Theme { get; private set; } = "dark";

    private static (string Key, string Hex)[] CurrentPalette =>
        Theme == "light" ? LightDefaults : Defaults;

    /// <summary>Palette hex for a brush key in the current theme (falls back to dark, then white).</summary>
    public static string PaletteHex(string key)
    {
        foreach (var (k, hex) in CurrentPalette)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return hex;
        foreach (var (k, hex) in Defaults)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return hex;
        return "#FFFFFF";
    }

    private static Color Pal(string key, Color fallback)
    {
        foreach (var (k, hex) in CurrentPalette)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && TryParse(hex, out var c))
                return c;
        return fallback;
    }

    /// <summary>Switches theme from stored settings; each theme keeps its own surface colors (animated by default).</summary>
    public static void SetTheme(AppSettings s, bool animate = true) => Apply(s, animate);

    /// <summary>Applies all stored customization from settings.</summary>
    /// <param name="s">Settings to apply.</param>
    /// <param name="animate">Animate brush color transitions instead of snapping.</param>
    public static void Apply(AppSettings s, bool animate = false)
    {
        Theme = s.Theme == "light" ? "light" : "dark";
        var hasImage = !string.IsNullOrEmpty(s.BackgroundImagePath) && File.Exists(s.BackgroundImagePath);

        // Collect brush targets first; commit once (instantly or as a crossfade).
        var targets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        // 0. Base palette (dark or light) before user overrides.
        var basePalette = Theme == "light" ? LightDefaults : Defaults;
        foreach (var (key, hex) in basePalette)
        {
            if (TryParse(hex, out var pc)) targets[key] = pc;
        }

        // 1. Accent
        if (!TryParse(s.AccentColor, out var accent))
            TryParse("#28D7B7", out accent);
        var gradTargets = AccentTargets(accent, targets);

        // Surface customs are stored per theme, so switching themes visibly
        // switches palettes while each theme remembers its own colors.
        var bgHex = Theme == "light" ? s.BgColorLight : s.BgColor;
        var panelHex = Theme == "light" ? s.PanelColorLight : s.PanelColor;
        var cardHex = Theme == "light" ? s.CardColorLight : s.CardColor;
        var textHex = Theme == "light" ? s.TextColorLight : s.TextColor;

        // 2. Bg (Window background / deep canvas background)
        var bg = TryParse(bgHex, out var customBg)
            ? customBg
            : Pal("BgDeepBrush", Color.FromRgb(0x09, 0x0B, 0x0F));
        targets["BgDeepBrush"] = hasImage ? WithAlpha(bg, 0x30) : bg;
        targets["WindowBackgroundBrush"] = bg;

        // 3. Panel (Sidebar, headers, large panels)
        var panel = TryParse(panelHex, out var customPanel)
            ? customPanel
            : Pal("BgPanelBrush", Color.FromRgb(0x10, 0x14, 0x19));
        targets["BgPanelBrush"] = hasImage ? WithAlpha(panel, 0xA6) : panel;

        // 4. Card & Elevated surfaces (all cards, items, badges, textboxes)
        var hasCustomCard = TryParse(cardHex, out var card);
        if (!hasCustomCard)
            card = Pal("BgCardBrush", Color.FromRgb(0x15, 0x1B, 0x22));

        Color elevated, hover, border, borderStrong;
        if (hasCustomCard)
        {
            elevated = Scale(card, 1.25);
            hover = Scale(card, 1.45);
            var isLightCard = Luma(card) > 128;
            border = isLightCard ? Scale(card, 0.75) : Scale(card, 1.9);
            borderStrong = isLightCard ? Scale(card, 0.55) : Scale(card, 2.7);
        }
        else
        {
            elevated = Pal("BgElevatedBrush", card);
            hover = Pal("BgHoverBrush", card);
            border = Pal("BorderBrush", card);
            borderStrong = Pal("BorderStrongBrush", card);
        }

        targets["BgCardBrush"] = hasImage ? WithAlpha(card, 0xBF) : card;
        targets["BgElevatedBrush"] = hasImage ? WithAlpha(elevated, 0xD9) : elevated;
        targets["BgHoverBrush"] = hasImage ? WithAlpha(hover, 0xE0) : hover;
        targets["BorderBrush"] = hasImage ? WithAlpha(border, 0x80) : border;
        targets["BorderStrongBrush"] = hasImage ? WithAlpha(borderStrong, 0xB0) : borderStrong;

        // 5. Text (palette text colors already collected in step 0 when not customized)
        if (TryParse(textHex, out var text))
        {
            targets["TextPrimaryBrush"] = text;
            targets["TextSecondaryBrush"] = Scale(text, 0.75);
            targets["TextMutedBrush"] = Scale(text, 0.55);
        }

        CommitTargets(targets, gradTargets, animate);

        BackgroundPath = s.BackgroundImagePath ?? "";
        Kind = DetectKind(BackgroundPath);
        if (Kind == BackgroundKind.None)
            BackgroundPath = "";
        BackgroundDim = Math.Clamp(s.BackgroundDim, 0, 90);
        BackgroundBlur = Math.Clamp(s.BackgroundBlur, 0, 60);

        Changed?.Invoke();
    }

    /// <summary>Applies an accent color (also derives gradient + glow shades).</summary>
    public static void ApplyAccent(Color accent, bool animate = false)
    {
        var targets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var grad = AccentTargets(accent, targets);
        CommitTargets(targets, grad, animate);
        Changed?.Invoke();
    }

    private static (Color To0, Color To1) AccentTargets(Color accent, Dictionary<string, Color> targets)
    {
        targets["AccentBrush"] = accent;
        targets["AccentGlowBrush"] = Scale(accent, 0.40);
        return (accent, Scale(accent, 0.72));
    }

    /// <summary>Restores the default theme colors (of the current theme).</summary>
    public static void ResetColors(bool animate = false)
    {
        BackgroundPath = "";
        Kind = BackgroundKind.None;
        var targets = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, hex) in CurrentPalette)
        {
            if (TryParse(hex, out var c)) targets[key] = c;
        }
        (Color To0, Color To1)? grad = null;
        if (TryParse("#28D7B7", out var accent)) grad = AccentTargets(accent, targets);
        CommitTargets(targets, grad, animate);
        Changed?.Invoke();
    }

    private static void CommitTargets(
        Dictionary<string, Color> targets, (Color To0, Color To1)? gradient, bool animate)
    {
        var app = Application.Current;
        var dispatcher = app?.Dispatcher;
        if (!animate || app == null || dispatcher == null
            || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            StopAnimTimer();
            CommitInstant(targets, gradient);
            return;
        }

        var res = app.Resources;
        var anim = new ActiveAnim { Start = DateTime.UtcNow };
        foreach (var (key, to) in targets)
        {
            var from = res[key] is SolidColorBrush b ? b.Color : to;
            if (from == to) continue;
            anim.Brushes.Add((key, from, to));
        }
        if (gradient is var (g0, g1))
        {
            var f0 = g0;
            var f1 = g1;
            if (res["AccentGradient"] is LinearGradientBrush old && old.GradientStops.Count >= 2)
            {
                f0 = old.GradientStops[0].Color;
                f1 = old.GradientStops[1].Color;
            }
            if (f0 != g0 || f1 != g1)
            {
                anim.HasGradient = true;
                anim.GradFrom0 = f0;
                anim.GradTo0 = g0;
                anim.GradFrom1 = f1;
                anim.GradTo1 = g1;
            }
        }

        if (anim.Brushes.Count == 0 && !anim.HasGradient)
        {
            CommitInstant(targets, gradient);
            return;
        }

        _activeAnim = anim;
        _animTimer ??= new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _animTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animTimer.Tick -= OnAnimTick;
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    private static void CommitInstant(Dictionary<string, Color> targets, (Color To0, Color To1)? gradient)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        foreach (var (key, to) in targets)
            res[key] = new SolidColorBrush(to);
        if (gradient is var (g0, g1))
            res["AccentGradient"] = new LinearGradientBrush(g0, g1, new Point(0, 0), new Point(1, 1));
    }

    private static void StopAnimTimer()
    {
        _animTimer?.Stop();
        _activeAnim = null;
    }

    private static void OnAnimTick(object? sender, EventArgs e)
    {
        var anim = _activeAnim;
        var res = Application.Current?.Resources;
        if (anim == null || res == null)
        {
            StopAnimTimer();
            return;
        }
        var p = (DateTime.UtcNow - anim.Start).TotalMilliseconds / AnimDuration.TotalMilliseconds;
        if (p >= 1) p = 1;
        var t = AnimEase.Ease(p);
        foreach (var (key, from, to) in anim.Brushes)
            res[key] = new SolidColorBrush(Lerp(from, to, t));
        if (anim.HasGradient)
            res["AccentGradient"] = new LinearGradientBrush(
                Lerp(anim.GradFrom0, anim.GradTo0, t),
                Lerp(anim.GradFrom1, anim.GradTo1, t),
                new Point(0, 0), new Point(1, 1));
        if (p >= 1)
        {
            StopAnimTimer();
            // Refresh holders that cached a mid-flight brush instance (e.g. NavButton icons).
            Changed?.Invoke();
        }
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        static byte L(byte x, byte y, double t) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A, t), L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t));
    }

    private static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Scale(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255),
        (byte)Math.Clamp(c.G * f, 0, 255),
        (byte)Math.Clamp(c.B * f, 0, 255));

    public static bool TryParse(string? hex, out Color color)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
        }
        catch { }
        color = default;
        return false;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
