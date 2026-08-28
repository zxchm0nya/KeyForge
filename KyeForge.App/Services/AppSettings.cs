using System.IO;
using System.Text.Json;

namespace KyeForge.App.Services;

/// <summary>Persists the app's remembered keyboard + config between sessions.</summary>
public class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KyeForge");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public string? LastConfigPath { get; set; }
    public string? LastDevicePath { get; set; }
    public string LastDeviceName { get; set; } = "";
    public string LastConfigName { get; set; } = "";

    /// <summary>UI language code: "en" or "ru". Defaults to system language if unset.</summary>
    public string Language { get; set; } = "ru";

    public bool RememberLastDevice { get; set; } = true;
    public bool RememberLastConfig { get; set; } = true;

    // ---- Appearance customization (empty = theme default) ----
    public string AccentColor { get; set; } = "";
    public string BgColor { get; set; } = "";
    public string PanelColor { get; set; } = "";
    public string CardColor { get; set; } = "";
    public string TextColor { get; set; } = "";

    /// <summary>Custom background image path (png/jpg/bmp). Empty = no image.</summary>
    public string BackgroundImagePath { get; set; } = "";

    /// <summary>Background dim overlay strength, 0..90 (percent).</summary>
    public double BackgroundDim { get; set; } = 55;

    /// <summary>Background blur radius, 0..60 (px).</summary>
    public double BackgroundBlur { get; set; } = 0;

    // ---- Last saved lighting profile (mirrors keyboard EEPROM) ----
    public int? LightingEffect { get; set; }
    public double? LightingBrightness { get; set; }
    public double? LightingSpeed { get; set; }
    public string LightingColor { get; set; } = "";

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }
}
