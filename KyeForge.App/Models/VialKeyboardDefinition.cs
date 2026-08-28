using System.Text.Json.Serialization;

namespace KyeForge.App.Models;

/// <summary>A parsed VIA keyboard definition (the *.json you load into the app).</summary>
public class VialKeyboardDefinition
{
    public string Name { get; set; } = "Custom Keyboard";

    [JsonPropertyName("vendorId")]
    public string VendorId { get; set; } = "0x0000";

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = "0x0000";

    [JsonPropertyName("matrix")]
    public MatrixInfo? Matrix { get; set; }

    [JsonPropertyName("layouts")]
    public LayoutsInfo? Layouts { get; set; }

    [JsonPropertyName("lighting")]
    public LightingInfo? Lighting { get; set; }

    [JsonPropertyName("communityLayouts")]
    public Dictionary<string, object>? CommunityLayouts { get; set; }

    [JsonPropertyName("customKeycodes")]
    public List<QkCustomKeycode> CustomKeycodes { get; set; } = new();

    /// <summary>Parsed QK-style lighting menu (id_qmk_* controls). Set by ViaConfigParser.</summary>
    public QkLighting? QkLighting { get; set; }

    public ushort VendorIdValue => ParseHex(VendorId);
    public ushort ProductIdValue => ParseHex(ProductId);

    private static ushort ParseHex(string v)
    {
        var s = v.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var r) ? r : (ushort)0;
    }
}

public class MatrixInfo
{
    public int Rows { get; set; }
    public int Cols { get; set; }
}

public class LayoutsInfo
{
    [JsonPropertyName("labels")]
    public List<object?> Labels { get; set; } = new();

    [JsonPropertyName("keymap")]
    public List<object> Keymap { get; set; } = new();
}

public class LightingInfo
{
    [JsonPropertyName("extends")]
    public string Extends { get; set; } = "";

    [JsonPropertyName("keycodes")]
    public string Keycodes { get; set; } = "";

    [JsonPropertyName("underglowEffects")]
    public List<List<object>>? UnderglowEffects { get; set; }

    [JsonPropertyName("supportedLightingValues")]
    public List<string>? SupportedLightingValues { get; set; }
}

/// <summary>A custom keycode entry from the config's "customKeycodes" section (QK boards).</summary>
public class QkCustomKeycode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = "";
}

/// <summary>Lighting controls parsed from the config's "menus" section (QK/VIA id_qmk_* items).</summary>
public class QkLighting
{
    public int BrightnessMin { get; set; } = 0;
    public int BrightnessMax { get; set; } = 255;
    public int SpeedMin { get; set; } = 0;
    public int SpeedMax { get; set; } = 255;

    public byte Group { get; set; } = 3;
    public byte BrightnessSub { get; set; } = 1;
    public byte EffectSub { get; set; } = 2;
    public byte SpeedSub { get; set; } = 3;
    public byte ColorSub { get; set; } = 4;

    public List<QkEffectOption> Effects { get; } = new();
    public bool HasEffects => Effects.Count > 0;
}

public class QkEffectOption
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
