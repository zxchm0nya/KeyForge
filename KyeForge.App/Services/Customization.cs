using System.IO;
using System.Windows;
using System.Windows.Media;

namespace KyeForge.App.Services;

/// <summary>
/// Live theme customization: updates shared brush instances from Themes/Colors.xaml
/// so every DynamicResource reference in the app updates instantly. Also broadcasts
/// changes (background image etc.) via <see cref="Changed"/>.
/// </summary>
public static class Customization
{
    public static event Action? Changed;

    /// <summary>Current custom background image path ("" = none).</summary>
    public static string BackgroundPath { get; private set; } = "";

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

    /// <summary>Applies all stored customization from settings.</summary>
    public static void Apply(AppSettings s)
    {
        var hasImage = !string.IsNullOrEmpty(s.BackgroundImagePath) && File.Exists(s.BackgroundImagePath);

        // 1. Accent
        if (TryParse(s.AccentColor, out var accent))
            ApplyAccentInternal(accent);
        else if (TryParse("#28D7B7", out var defAccent))
            ApplyAccentInternal(defAccent);

        // 2. Bg (Window background / deep canvas background)
        var bg = TryParse(s.BgColor, out var customBg) ? customBg : Color.FromRgb(0x09, 0x0B, 0x0F);
        SetBrush("BgDeepBrush", hasImage ? Color.FromArgb(0x30, bg.R, bg.G, bg.B) : bg);
        SetBrush("WindowBackgroundBrush", bg);

        // 3. Panel (Sidebar, headers, large panels)
        var panel = TryParse(s.PanelColor, out var customPanel) ? customPanel : Color.FromRgb(0x10, 0x14, 0x19);
        SetBrush("BgPanelBrush", hasImage ? Color.FromArgb(0xA6, panel.R, panel.G, panel.B) : panel);

        // 4. Card & Elevated surfaces (all cards, items, badges, textboxes)
        var card = TryParse(s.CardColor, out var customCard) ? customCard : Color.FromRgb(0x15, 0x1B, 0x22);
        var elevated = Scale(card, 1.25);
        var hover = Scale(card, 1.45);
        SetBrush("BgCardBrush", hasImage ? Color.FromArgb(0xBF, card.R, card.G, card.B) : card);
        SetBrush("BgElevatedBrush", hasImage ? Color.FromArgb(0xD9, elevated.R, elevated.G, elevated.B) : elevated);
        SetBrush("BgHoverBrush", hasImage ? Color.FromArgb(0xE0, hover.R, hover.G, hover.B) : hover);

        // Derive border colors from card tone
        var isLightCard = (0.299 * card.R + 0.587 * card.G + 0.114 * card.B) > 128;
        var border = isLightCard ? Scale(card, 0.75) : Scale(card, 1.9);
        var borderStrong = isLightCard ? Scale(card, 0.55) : Scale(card, 2.7);
        SetBrush("BorderBrush", hasImage ? Color.FromArgb(0x80, border.R, border.G, border.B) : border);
        SetBrush("BorderStrongBrush", hasImage ? Color.FromArgb(0xB0, borderStrong.R, borderStrong.G, borderStrong.B) : borderStrong);

        // 5. Text
        if (TryParse(s.TextColor, out var text))
        {
            SetBrush("TextPrimaryBrush", text);
            SetBrush("TextSecondaryBrush", Scale(text, 0.75));
            SetBrush("TextMutedBrush", Scale(text, 0.55));
        }
        else
        {
            SetBrush("TextPrimaryBrush", Color.FromRgb(0xF3, 0xF5, 0xFA));
            SetBrush("TextSecondaryBrush", Color.FromRgb(0xAE, 0xB8, 0xC2));
            SetBrush("TextMutedBrush", Color.FromRgb(0x78, 0x83, 0x8E));
        }

        BackgroundPath = s.BackgroundImagePath ?? "";
        BackgroundDim = Math.Clamp(s.BackgroundDim, 0, 90);
        BackgroundBlur = Math.Clamp(s.BackgroundBlur, 0, 60);

        Changed?.Invoke();
    }

    /// <summary>Applies an accent color (also derives gradient + glow shades).</summary>
    public static void ApplyAccent(Color accent)
    {
        ApplyAccentInternal(accent);
        Changed?.Invoke();
    }

    private static void ApplyAccentInternal(Color accent)
    {
        SetBrush("AccentBrush", accent);
        SetBrush("AccentGlowBrush", Scale(accent, 0.40));
        var grad = new LinearGradientBrush(accent, Scale(accent, 0.72), new Point(0, 0), new Point(1, 1));
        if (Application.Current?.Resources != null)
        {
            Application.Current.Resources["AccentGradient"] = grad;
        }
    }

    /// <summary>Restores the default theme colors.</summary>
    public static void ResetColors()
    {
        foreach (var (key, hex) in Defaults)
        {
            if (TryParse(hex, out var c)) SetBrush(key, c);
        }
        if (TryParse("#28D7B7", out var accent)) ApplyAccentInternal(accent);
        Changed?.Invoke();
    }

    private static void SetBrush(string key, Color c)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        if (res[key] is SolidColorBrush b && !b.IsFrozen)
        {
            b.Color = c;
        }
        else
        {
            res[key] = new SolidColorBrush(c);
        }
    }

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

