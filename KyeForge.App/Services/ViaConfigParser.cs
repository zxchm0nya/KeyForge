using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KyeForge.App.Models;

namespace KyeForge.App.Services;

public static class ViaConfigParser
{
    // Q W E R T Y U I O P
    private static readonly int[] Row2Alphas = { 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13 };
    // A S D F G H J K L
    private static readonly int[] Row3Alphas = { 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F };
    // Z X C V B N M ,< .> /?
    private static readonly int[] Row4Alphas = { 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10, 0x36, 0x37, 0x38 };
    /// <summary>
    /// Loads and parses a VIA/QK JSON definition file into a KeyboardLayout.
    /// Parsing is fully tolerant: unknown fields, arrays where objects are expected
    /// (e.g. "communityLayouts"), numeric VID/PID, or missing "name" no longer break loading.
    /// Returns an error description when the file cannot be used at all.
    /// </summary>
    public static (VialKeyboardDefinition? Definition, KeyboardLayout? Layout, string? Error) Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json,
                    new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
            }
            catch (JsonException ex)
            {
                return (null, null, ex.Message);
            }

            if (root is not JsonObject)
                return (null, null, "root");

            var def = BuildDefinition(root);

            // QK-style configs expose lighting through "menus" (id_qmk_* controls)
            def.QkLighting = ParseQkLighting(json);

            // Boards without "menus" still expose lighting via lighting.extends
            // (qmk_rgblight / qmk_rgb_matrix / qmk_backlight) - map to the VIA channel.
            if (def.QkLighting == null)
            {
                var ext = (def.Lighting?.Extends ?? "").ToLowerInvariant();
                byte channel = (byte)(ext.Contains("rgb_matrix") ? 3
                    : ext.Contains("rgblight") ? 2
                    : ext.Contains("led_matrix") ? 5
                    : ext.Contains("backlight") ? 1
                    : 3);
                def.QkLighting = new QkLighting { Group = channel };
            }

            var layout = ParseLayout(def, root);
            if (layout == null)
                return (def, null, "layout");
            return (def, layout, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }
    }

    /// <summary>Builds the definition manually from JSON nodes so that any type
    /// mismatch in unrelated sections (numbers as strings, arrays vs objects, ...)
    /// can never abort loading the whole file.</summary>
    private static VialKeyboardDefinition BuildDefinition(JsonNode root)
    {
        var def = new VialKeyboardDefinition();

        var usb = root["usb"] as JsonObject;
        var usbVid = usb?["vid"];
        var usbPid = usb?["pid"];
        def.Name = Str(root["name"]) ?? Str(root["keyboard_name"]) ?? Str(root["title"]) ?? def.Name;

        // VID/PID may live at top level ("vendorId"/"productId") or in "usb" (QMK style)
        def.VendorId = HexStr(root["vendorId"]) ?? HexStr(usbVid) ?? def.VendorId;
        def.ProductId = HexStr(root["productId"]) ?? HexStr(usbPid) ?? def.ProductId;

        if (root["matrix"] is JsonObject mx)
        {
            def.Matrix = new MatrixInfo
            {
                Rows = Int(mx["rows"]) ?? 0,
                Cols = Int(mx["cols"]) ?? 0
            };
        }
        else if (root["matrix_pins"] is JsonObject mp)
        {
            // QMK info.json fallback: derive sizes from pin list lengths
            var rows = mp["rows"] as JsonArray;
            var cols = mp["cols"] as JsonArray;
            if (rows != null || cols != null)
                def.Matrix = new MatrixInfo { Rows = rows?.Count ?? 0, Cols = cols?.Count ?? 0 };
        }

        if (root["layouts"] is JsonObject lo)
        {
            def.Layouts = new LayoutsInfo();
            if (lo["labels"] is JsonArray labels)
            {
                foreach (var l in labels)
                    def.Layouts.Labels.Add(LabelFromNode(l));
            }
        }

        if (root["lighting"] is JsonObject li)
        {
            var info = new LightingInfo
            {
                Extends = Str(li["extends"]) ?? "",
                Keycodes = Str(li["keycodes"]) ?? ""
            };
            if (li["underglowEffects"] is JsonArray ue)
            {
                info.UnderglowEffects = new List<List<object>>();
                foreach (var entry in ue)
                {
                if (entry is JsonArray pair && pair.Count >= 2)
                    info.UnderglowEffects.Add(new List<object> { Raw(pair[0]) ?? "", Raw(pair[1]) ?? 0 });
                }
            }
            if (li["supportedLightingValues"] is JsonArray sv)
            {
                info.SupportedLightingValues = new List<string>();
                foreach (var v in sv)
                {
                    var s = Str(v);
                    if (s != null) info.SupportedLightingValues.Add(s);
                }
            }
            def.Lighting = info;
        }

        // "communityLayouts" may be an array OR an object - accepted either way, content unused.
        // "menus", "customKeycodes", ... are read defensively below.

        if (root["customKeycodes"] is JsonArray cks)
        {
            foreach (var ck in cks)
            {
                if (ck is not JsonObject o) continue;
                def.CustomKeycodes.Add(new QkCustomKeycode
                {
                    Name = Str(o["name"]) ?? "",
                    Title = Str(o["title"]) ?? "",
                    ShortName = Str(o["shortName"]) ?? ""
                });
            }
        }

        return def;
    }

    private static string? Str(JsonNode? node)
    {
        if (node == null) return null;
        try { return node.ToString().Trim(); } catch { return null; }
    }

    /// <summary>Reads a hex identifier; numbers (e.g. 51984) are converted to "0xCA10".</summary>
    private static string? HexStr(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue v && node.GetValueKind() == JsonValueKind.Number &&
            v.TryGetValue<long>(out var num) && num is >= 0 and <= 0xFFFF)
            return $"0x{num:X4}";
        var s = Str(node);
        return s != null && s.Length > 0 ? s : null;
    }

    private static int? Int(JsonNode? node)
    {
        if (node == null) return null;
        try { return node.GetValue<int>(); }
        catch
        {
            var s = Str(node);
            return s != null && int.TryParse(s, out var v) ? v : null;
        }
    }

    private static object? Raw(JsonNode? node)
    {
        if (node == null) return null;
        try { return JsonSerializer.SerializeToElement(node); } catch { return node.ToString(); }
    }

    private static string? LabelFromNode(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonArray arr)
        {
            var parts = new List<string>();
            foreach (var child in arr)
            {
                var s = Str(child);
                if (!string.IsNullOrEmpty(s)) parts.Add(s);
            }
            return parts.Count > 0 ? string.Join(" / ", parts) : arr.ToJsonString();
        }
        return Str(node);
    }

    private static KeyboardLayout? ParseLayout(VialKeyboardDefinition def, JsonNode root)
    {
        var layout = new KeyboardLayout();
        foreach (var label in def.Layouts?.Labels ?? new List<object?>())
            layout.MatrixLabels.Add(LabelToText(label));

        // Use the parsed node to preserve raw layout entries (QK geometry markers + "r,c" cells)
        var keymapNode = root["layouts"]?["keymap"];

        if (keymapNode is JsonObject keymapObj)
        {
            // Some VIA configs store keymap as an object keyed by layout name: {"Default": [rows...]}
            foreach (var kvp in keymapObj)
            {
                if (kvp.Value is JsonArray layerRows && layerRows.Count > 0)
                {
                    try { ParseRows(layerRows, layout); } catch { }
                    break;
                }
            }
        }
        else if (keymapNode is JsonArray layers && layers.Count > 0)
        {
            try
            {
                // QK/Keychron configs: keymap is directly an array of rows.
                // Standard VIA configs: keymap is an array of layers, each an array of rows.
                var first = layers[0];
                if (first is JsonArray firstArr && firstArr.Count > 0 && firstArr[0] is JsonArray)
                {
                    foreach (var layer in layers)
                    {
                        if (layer is JsonArray larr) { ParseRows(larr, layout); break; }
                    }
                }
                else
                {
                    ParseRows(layers, layout);
                }
            }
            catch { }
        }
        else if (root["layers"] is JsonArray exported && exported.Count > 0)
        {
            // KyeForge-exported keymap files: {"layers":[[ [keycode, ...], ... ]]}
            try
            {
                foreach (var layerNode in exported)
                {
                    if (layerNode is JsonArray layerRows)
                    {
                        ParseExportedLayer(layerRows, layout);
                        break;
                    }
                }
            }
            catch { }
        }

        if (layout.Keys.Count == 0 && def.Matrix != null && def.Matrix.Rows > 0 && def.Matrix.Cols > 0)
            BuildMatrixFallback(def.Matrix, layout);

        layout.MaxX = layout.Keys.Count > 0 ? (int)Math.Ceiling(layout.Keys.Max(k => k.X + k.W)) : 0;
        layout.MaxY = layout.Keys.Count > 0 ? (int)Math.Ceiling(layout.Keys.Max(k => k.Y + k.H)) : 0;
        return layout.Keys.Count > 0 ? layout : null;
    }

    private static void BuildMatrixFallback(MatrixInfo matrix, KeyboardLayout layout)
    {
        for (var r = 0; r < matrix.Rows; r++)
        {
            for (var c = 0; c < matrix.Cols; c++)
            {
                layout.Keys.Add(new LayoutKey
                {
                    Row = r,
                    Col = c,
                    MatrixRow = r,
                    MatrixCol = c,
                    X = c,
                    Y = r,
                    W = 1,
                    H = 1,
                    Legend = $"{r},{c}",
                    Keycode = 0
                });
            }
        }
    }

    /// <summary>Parses a KyeForge-exported keymap: rows of raw keycode numbers.</summary>
    private static void ParseExportedLayer(JsonArray rows, KeyboardLayout layout)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r] is not JsonArray cols) continue;
            for (var c = 0; c < cols.Count; c++)
            {
                if (cols[c] is not JsonValue val) continue;
                int kc = 0;
                try
                {
                    kc = val.GetValueKind() == JsonValueKind.Number
                        ? val.GetValue<int>()
                        : int.TryParse(val.GetValue<string>(), out var parsed) ? parsed : 0;
                }
                catch { }

                var keycode = (ushort)Math.Max(0, Math.Min(kc, 0xFFFF));
                layout.Keys.Add(new LayoutKey
                {
                    Row = r,
                    Col = c,
                    MatrixRow = r,
                    MatrixCol = c,
                    X = c,
                    Y = r,
                    W = 1,
                    H = 1,
                    Legend = KeycodeMap.Name(keycode),
                    Keycode = keycode
                });
            }
        }
    }

    private static string? LabelToText(object? label)
    {
        if (label == null) return null;
        if (label is string s) return s;
        if (label is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            if (el.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var child in el.EnumerateArray())
                    if (child.ValueKind == JsonValueKind.String)
                        parts.Add(child.GetString() ?? "");
                return parts.Count > 0 ? string.Join(" / ", parts.Where(p => p.Length > 0)) : el.ToString();
            }
            return el.ToString();
        }
        return label.ToString();
    }

    /// <summary>
    /// Parses one keymap layer (a list of rows).
    /// Supports both classic VIA rows (objects with x/y/w/h/text) and QK/Keychron-style rows
    /// (geometry/color markers followed by "row,col" matrix reference strings).
    /// </summary>
    private static void ParseRows(JsonArray rows, KeyboardLayout layout)
    {
        double cursorX = 0, cursorY = 0;
        // QK marker state: geometry of the marker applies to the NEXT key cell
        double pendingW = 1, pendingH = 1, pendingX = 0;

        for (int r = 0; r < rows.Count; r++)
        {
            var rowNode = rows[r];
            if (rowNode is JsonObject rowObj)
            {
                // Classic VIA row-level object: {"x":..,"y":..}
                cursorX = rowObj["x"]?.GetValue<double>() ?? 0;
                if (rowObj.ContainsKey("y"))
                    cursorY += rowObj["y"]!.GetValue<double>(); // KLE: y is a downward delta
                continue;
            }
            if (rowNode is not JsonArray rowArr) continue;

            cursorX = 0;
            pendingW = 1; pendingH = 1; pendingX = 0;
            int colIndex = 0;

            for (int c = 0; c < rowArr.Count; c++)
            {
                var cell = rowArr[c];

                if (cell is JsonObject obj)
                {
                    // Classic VIA key object: contains text legend (optionally x/y/w/h)
                    var text = obj["text"]?["t"] ?? obj["text"];
                    if (text != null)
                    {
                        double kx = cursorX, ky = cursorY, kw = 1, kh = 1;
                        if (obj.ContainsKey("x")) kx = obj["x"]!.GetValue<double>();
                        if (obj.ContainsKey("y")) ky = obj["y"]!.GetValue<double>();
                        if (obj.ContainsKey("w")) kw = obj["w"]!.GetValue<double>();
                        if (obj.ContainsKey("h")) kh = obj["h"]!.GetValue<double>();
                        var legend = text.GetValue<string>();

                        layout.Keys.Add(new LayoutKey
                        {
                            Row = r, Col = c, MatrixRow = r, MatrixCol = c,
                            X = kx, Y = ky, W = kw, H = kh, Legend = legend
                        });
                        cursorX = kx + kw;
                        cursorY = ky;
                    }
                    else
                    {
                        // QK geometry/color marker: x is a relative offset, y a downward delta (KLE).
                        if (obj.ContainsKey("x")) pendingX = obj["x"]!.GetValue<double>();
                        if (obj.ContainsKey("y")) cursorY += obj["y"]!.GetValue<double>();
                        if (obj.ContainsKey("w")) pendingW = obj["w"]!.GetValue<double>();
                        if (obj.ContainsKey("h")) pendingH = obj["h"]!.GetValue<double>();
                        // "c" (color) and "fa" (font alignment) are informational - ignore
                    }
                    continue;
                }

                if (cell is not JsonValue sval) continue;

                var s = sval.GetValue<string>();
                var parts = s.Split('\n');
                bool isEncoder = false;
                foreach (var part in parts)
                {
                    var t = part.Trim();
                    if (t.Length >= 2 && t[0] == 'e' && int.TryParse(t.AsSpan(1), out _)) { isEncoder = true; break; }
                }

                int mr = r, mc = c;
                ParseMatrix(parts, ref mr, ref mc);

                double x = cursorX + pendingX;
                double y = cursorY;

                layout.Keys.Add(new LayoutKey
                {
                    Row = r, Col = c, MatrixRow = mr, MatrixCol = mc,
                    X = x, Y = y, W = pendingW, H = pendingH,
                    Legend = isEncoder ? "" : DefaultLegend(r, colIndex),
                    Keycode = isEncoder ? (ushort)0 : (ushort)DefaultKeycode(r, colIndex),
                    IsEncoder = isEncoder
                });

                colIndex++;
                cursorX = x + pendingW;
                cursorY = y;
                pendingW = 1; pendingH = 1; pendingX = 0;
            }
            cursorY += 1;
        }
    }

    private static void ParseMatrix(string[] parts, ref int row, ref int col)
    {
        foreach (var part in parts)
        {
            var p = part.Trim().Split(',');
            if (p.Length == 2 && int.TryParse(p[0], out var rr) && int.TryParse(p[1], out var cc))
            {
                row = rr; col = cc;
                return;
            }
        }
    }

    /// <summary>
    /// Default keycode by layout position (row index, key index inside the row).
    /// Used to show readable legends for QK-style configs whose keymap has no keycodes.
    /// </summary>
    private static int DefaultKeycode(int row, int idx)
    {
        switch (row)
        {
            case 0: // Esc, F1..F12, Del...
                if (idx == 0) return 0x29;
                if (idx >= 1 && idx <= 12) return 0x3A + idx - 1;
                return 0x4C;
            case 1: // `~, 1..0, -_, =+, Bksp
                if (idx == 0) return 0x35;
                if (idx >= 1 && idx <= 10) return 0x1E + idx - 1;
                if (idx == 11) return 0x2D;
                if (idx == 12) return 0x2E;
                return 0x2A;
            case 2: // Tab, Q..P, [{, ]}, \|
                if (idx == 0) return 0x2B;
                if (idx >= 1 && idx <= 10) return Row2Alphas[idx - 1];
                if (idx == 11) return 0x2F;
                if (idx == 12) return 0x30;
                return 0x31;
            case 3: // Caps, A..L, ;:, '", Enter
                if (idx == 0) return 0x39;
                if (idx >= 1 && idx <= 9) return Row3Alphas[idx - 1];
                if (idx == 10) return 0x33;
                if (idx == 11) return 0x34;
                return 0x28;
            case 4: // Shift, Z..M, ,<, .>, /?, RShift, ↑
                if (idx == 0) return 0xE1;
                if (idx >= 1 && idx <= 10) return Row4Alphas[idx - 1];
                if (idx == 11) return 0xE5;
                return 0x52;
            case 5: // Ctrl, Win, Alt, Space, Fn, Ctrl, ←, ↓, →
                switch (idx)
                {
                    case 0: return 0xE0;
                    case 1: return 0xE3;
                    case 2: return 0xE2;
                    case 3: return 0x2C;
                    case 4: return 0x71; // Fn placeholder
                    case 5: return 0xE4;
                    case 6: return 0x50;
                    case 7: return 0x51;
                    case 8: return 0x4F;
                    default: return 0;
                }
            default:
                return 0;
        }
    }

    private static string DefaultLegend(int row, int idx)
    {
        int kc = DefaultKeycode(row, idx);
        return kc == 0 ? "" : KeycodeMap.Name((uint)kc);
    }

    /// <summary>Parses the QK "menus" tree, extracting id_qmk_* lighting controls.</summary>
    private static QkLighting? ParseQkLighting(string rawJson)
    {
        try
        {
            var root = JsonNode.Parse(rawJson);
            // "menus" may be a plain array (classic VIA) or an object {"menus":[...]} (V3 style)
            var menusNode = root?["menus"];
            var menus = menusNode as JsonArray ?? (menusNode as JsonObject)?["menus"]?.AsArray();
            if (menus == null) return null;

            var lighting = new QkLighting();
            bool found = false;

            foreach (var menuNode in menus)
            {
                WalkMenu(menuNode, lighting, ref found);
            }

            return found ? lighting : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WalkMenu(JsonNode? node, QkLighting lighting, ref bool found)
    {
        if (node is not JsonObject obj) return;

        var type = obj["type"]?.GetValue<string>();
        var content = obj["content"] as JsonArray;
        if (type != null && content != null && content.Count >= 3 &&
            content[0] is JsonValue idNode && idNode.GetValueKind() == JsonValueKind.String &&
            idNode.GetValue<string>().StartsWith("id_qmk_", StringComparison.Ordinal))
        {
            int.TryParse(content[1]?.ToString(), out int g);
            int.TryParse(content[2]?.ToString(), out int s);
            string idName = idNode.GetValue<string>();
            if (g > 0 && s > 0)
            {
                lighting.Group = (byte)g;
                found = true;

                if (idName.Contains("brightness", StringComparison.Ordinal))
                    lighting.BrightnessSub = (byte)s;
                else if (idName.Contains("effect_speed", StringComparison.Ordinal))
                    lighting.SpeedSub = (byte)s;
                else if (idName.Contains("effect", StringComparison.Ordinal))
                    lighting.EffectSub = (byte)s;
                else if (idName.Contains("color", StringComparison.Ordinal))
                    lighting.ColorSub = (byte)s;

                var options = obj["options"] as JsonArray;
                if (type == "range" && options != null && options.Count >= 2)
                {
                    int.TryParse(options[0]?.ToString(), out int min);
                    int.TryParse(options[1]?.ToString(), out int max);
                    if (idName.Contains("brightness", StringComparison.Ordinal))
                    { lighting.BrightnessMin = min; lighting.BrightnessMax = max; }
                    else if (idName.Contains("effect_speed", StringComparison.Ordinal))
                    { lighting.SpeedMin = min; lighting.SpeedMax = max; }
                }
                else if (type == "dropdown" && options != null)
                {
                    foreach (var o in options)
                    {
                        if (o is not JsonArray pair || pair.Count < 2) continue;
                        int.TryParse(pair[1]?.ToString(), out int v);
                        lighting.Effects.Add(new QkEffectOption
                        {
                            Name = pair[0]?.ToString() ?? "",
                            Value = v
                        });
                    }
                }
            }
        }

        var inner = obj["content"] as JsonArray;
        if (inner != null)
        {
            foreach (var child in inner)
                WalkMenu(child, lighting, ref found);
        }
    }
}
