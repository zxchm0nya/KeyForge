using System.Collections.ObjectModel;

namespace KyeForge.App.Services;

/// <summary>Maps QMK keycodes (byte 0 = basic HID, high byte = modifiers) to display names.</summary>
public static class KeycodeMap
{
    public static readonly IReadOnlyDictionary<byte, string> Basic = new Dictionary<byte, string>
    {
        {0x00, "NO"}, {0x01, "ROLLOVER"}, {0x02, "PF"},
        {0x04,"A"},{0x05,"B"},{0x06,"C"},{0x07,"D"},{0x08,"E"},{0x09,"F"},{0x0A,"G"},
        {0x0B,"H"},{0x0C,"I"},{0x0D,"J"},{0x0E,"K"},{0x0F,"L"},{0x10,"M"},{0x11,"N"},
        {0x12,"O"},{0x13,"P"},{0x14,"Q"},{0x15,"R"},{0x16,"S"},{0x17,"T"},{0x18,"U"},
        {0x19,"V"},{0x1A,"W"},{0x1B,"X"},{0x1C,"Y"},{0x1D,"Z"},
        {0x1E,"1"},{0x1F,"2"},{0x20,"3"},{0x21,"4"},{0x22,"5"},{0x23,"6"},
        {0x24,"7"},{0x25,"8"},{0x26,"9"},{0x27,"0"},
        {0x28,"Enter"},{0x29,"Esc"},{0x2A,"Bksp"},{0x2B,"Tab"},{0x2C,"Space"},
        {0x2D,"-_"},{0x2E,"=+"},{0x2F,"[{ "},{0x30,"] }"},{0x31,"\\|"},{0x33,";:"},
        {0x34,"'\""},{0x35,"`~"},{0x36,",<"},{0x37,".>"},{0x38,"/?"},
        {0x39,"Caps"},
        {0x3A,"F1"},{0x3B,"F2"},{0x3C,"F3"},{0x3D,"F4"},{0x3E,"F5"},{0x3F,"F6"},
        {0x40,"F7"},{0x41,"F8"},{0x42,"F9"},{0x43,"F10"},{0x44,"F11"},{0x45,"F12"},
        {0x46,"PrtSc"},{0x47,"ScrLk"},{0x48,"Pause"},
        {0x49,"Ins"},{0x4A,"Home"},{0x4B,"PgUp"},{0x4C,"Del"},
        {0x4D,"End"},{0x4E,"PgDn"},{0x4F,"→"},{0x50,"←"},{0x51,"↓"},{0x52,"↑"},
        {0x53,"NumLck"},{0x54,"/"},{0x55,"*"},{0x56,"-"},{0x57,"+"},{0x58,"Enter"},
        {0x59,"1"},{0x5A,"2"},{0x5B,"3"},{0x5C,"4"},{0x5D,"5"},{0x5E,"6"},
        {0x5F,"7"},{0x60,"8"},{0x61,"9"},{0x62,"0"},{0x63,"."},
        {0x64,"App"},{0x65,"Power"},{0x66,"="},
        {0x70,"Macro off"},{0x71,"Macro on"},
        {0xD4,"F13"},{0xD5,"F14"},{0xD6,"F15"},{0xD7,"F16"},{0xD8,"F17"},{0xD9,"F18"},
        {0xDA,"F19"},{0xDB,"F20"},{0xDC,"F21"},{0xDD,"F22"},{0xDE,"F23"},{0xDF,"F24"},
        {0xE0,"L Ctrl"},{0xE1,"L Shift"},{0xE2,"L Alt"},{0xE3,"L GUI"},
        {0xE4,"R Ctrl"},{0xE5,"R Shift"},{0xE6,"R Alt"},{0xE7,"R GUI"},
    };

    public static readonly IReadOnlyDictionary<byte, string> Media = new Dictionary<byte, string>
    {
        {0x80,"Mute"},{0x81,"Vol+"},{0x82,"Vol-"},{0x83,"Prev"},{0x84,"Play/Pause"},{0x85,"Next"},
    };

    /// <summary>Custom keycodes from the loaded config, mapped to 0xF0+ (VIA custom range).</summary>
    private static readonly Dictionary<byte, string> Custom = new();

    public static IReadOnlyDictionary<byte, string> CustomEntries => Custom;

    public static void RegisterCustom(IEnumerable<KeyValuePair<byte, string>> entries)
    {
        Custom.Clear();
        foreach (var kv in entries)
            if (kv.Key >= 0xF0)
                Custom[kv.Key] = kv.Value;
    }

    public static string Name(uint keycode)
    {
        if (keycode <= 0xFF)
        {
            byte b = (byte)keycode;
            if (Custom.TryGetValue(b, out var cn)) return cn;
            return Basic.TryGetValue(b, out var n) ? n : $"0x{b:X2}";
        }
        // Modifier combos: QMK mods in high byte
        ushort hk = (ushort)keycode;
        byte low = (byte)(hk & 0xFF);
        byte mods = (byte)((hk >> 8) & 0x1F);
        if (Basic.TryGetValue(low, out var baseName))
        {
            if (mods == 0) return baseName;
            var modsList = new List<string>();
            if ((mods & 0x01) != 0) modsList.Add("Ctrl");
            if ((mods & 0x02) != 0) modsList.Add("Shift");
            if ((mods & 0x04) != 0) modsList.Add("Alt");
            if ((mods & 0x08) != 0) modsList.Add("GUI");
            if ((mods & 0x10) != 0) modsList.Add("Ctl");
            return string.Join("+", modsList) + "+" + baseName;
        }
        return $"0x{keycode:X4}";
    }
}