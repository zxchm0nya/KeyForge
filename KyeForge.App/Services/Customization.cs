using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KyeForge.App.Services;

/// <summary>
/// Live theme customization: updates shared brush instances from Themes/Colors.xaml
/// so every DynamicResource (and StaticResource) reference in the app updates instantly.
/// Brush color changes can be animated for a smooth theme/color transition.
/// Also broadcasts changes (background image etc.) via <see cref="Changed"/>.
/// </summary>
public static class Customization
{
    public static event Action? Changed;

    public enum BackgroundKind { None, Image, Gif, Video }

    /// <summary>Current custom background path ("" = none). May be an image, GIF or video.</summary>
    public static string BackgroundPath { get; private set; } = "";

    /// <summary>What kind of background <see cref="BackgroundPath"/> is.</summary>
    public static BackgroundKind Kind { get; private set; } = BackgroundKind.None;

    private static readonly string[] VideoExtensions =
        { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm", ".m4v" };

    public static BackgroundKind DetectKind(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return BackgroundKind.None;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".gif")
            return BackgroundKind.Gif;
        if (((IList<string>)VideoExtensions).Contains(ext))
            return BackgroundKind.Video;
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

    private static readonly Duration Transition = new Duration(TimeSpan.FromMilliseconds(450));
    private static readonly IEasingFunction TransitionEase =
        new CubicEase { EasingMode = EasingMode.EaseInOut };

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

        // 0. Base palette (dark or light) before user overrides.
        var basePalette = Theme == "light" ? LightDefaults : Defaults;
        foreach (var (key, hex) in basePalette)
        {
            if (TryParse(hex, out var pc)) SetBrush(key, pc, animate);
        }

        // 1. Accent
        if (TryParse(s.AccentColor, out var accent))
            ApplyAccentInternal(accent, animate);
        else if (TryParse("#28D7B7", out var defAccent))
            ApplyAccentInternal(defAccent, animate);

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
        SetBrush("BgDeepBrush", hasImage ? WithAlpha(bg, 0x30) : bg, animate);
        SetBrush("WindowBackgroundBrush", bg, animate);

        // 3. Panel (Sidebar, headers, large panels)
        var panel = TryParse(panelHex, out var customPanel)
            ? customPanel
            : Pal("BgPanelBrush", Color.FromRgb(0x10, 0x14, 0x19));
        SetBrush("BgPanelBrush", hasImage ? WithAlpha(panel, 0xA6) : panel, animate);

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

        SetBrush("BgCardBrush", hasImage ? WithAlpha(card, 0xBF) : card, animate);
        SetBrush("BgElevatedBrush", hasImage ? WithAlpha(elevated, 0xD9) : elevated, animate);
        SetBrush("BgHoverBrush", hasImage ? WithAlpha(hover, 0xE0) : hover, animate);
        SetBrush("BorderBrush", hasImage ? WithAlpha(border, 0x80) : border, animate);
        SetBrush("BorderStrongBrush", hasImage ? WithAlpha(borderStrong, 0xB0) : borderStrong, animate);

        // 5. Text (palette text colors already applied in step 0 when not customized)
        if (TryParse(textHex, out var text))
        {
            SetBrush("TextPrimaryBrush", text, animate);
            SetBrush("TextSecondaryBrush", Scale(text, 0.75), animate);
            SetBrush("TextMutedBrush", Scale(text, 0.55), animate);
        }

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
        ApplyAccentInternal(accent, animate);
        Changed?.Invoke();
    }

    private static void ApplyAccentInternal(Color accent, bool animate)
    {
        SetBrush("AccentBrush", accent, animate);
        SetBrush("AccentGlowBrush", Scale(accent, 0.40), animate);

        var res = Application.Current?.Resources;
        if (res == null) return;
        var target2 = Scale(accent, 0.72);
        if (res["AccentGradient"] is LinearGradientBrush grad && !grad.IsFrozen && grad.GradientStops.Count >= 2)
        {
            AnimateStop(grad.GradientStops[0], accent, animate);
            AnimateStop(grad.GradientStops[1], target2, animate);
        }
        else
        {
            res["AccentGradient"] = new LinearGradientBrush(accent, target2, new Point(0, 0), new Point(1, 1));
        }
    }

    /// <summary>Restores the default theme colors (of the current theme).</summary>
    public static void ResetColors(bool animate = false)
    {
        BackgroundPath = "";
        Kind = BackgroundKind.None;
        foreach (var (key, hex) in CurrentPalette)
        {
            if (TryParse(hex, out var c)) SetBrush(key, c, animate);
        }
        if (TryParse("#28D7B7", out var accent)) ApplyAccentInternal(accent, animate);
        Changed?.Invoke();
    }

    private static void SetBrush(string key, Color c, bool animate = false)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        if (res[key] is SolidColorBrush b && !b.IsFrozen)
        {
            var from = b.Color;
            b.BeginAnimation(SolidColorBrush.ColorProperty, null);
            b.Color = c;
            if (!animate || from == c) return;
            b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, c, Transition)
            {
                EasingFunction = TransitionEase,
                FillBehavior = FillBehavior.Stop,
            });
        }
        else
        {
            res[key] = new SolidColorBrush(c);
        }
    }

    private static void AnimateStop(GradientStop stop, Color c, bool animate)
    {
        var from = stop.Color;
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = c;
        if (!animate || from == c) return;
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(from, c, Transition)
        {
            EasingFunction = TransitionEase,
            FillBehavior = FillBehavior.Stop,
        });
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
