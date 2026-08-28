using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KyeForge.App.Models;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

public partial class KeyTestView : UserControl
{
    private readonly AppState _state = StateHub.State;
    private const double KeyUnit = 48;
    private const double KeyGap = 6;

    private readonly Dictionary<int, List<Border>> _usageToKeys = new();
    private readonly HashSet<int> _tested = new();
    private readonly HashSet<int> _pressed = new();
    private readonly HashSet<int> _hidPressed = new();
    private int _pressCount;

    private static readonly Brush BoardBrush = BrushFrom("#0B1015");
    private static readonly Brush KeyBrush = BrushFrom("#161F27");
    private static readonly Brush TestedBrush = BrushFrom("#20303A");
    private static readonly Brush PressedBrush = BrushFrom("#28D7B7");
    private static readonly Brush KeyBorderBrush = BrushFrom("#33414D");
    private static readonly Brush PressedBorderBrush = BrushFrom("#28D7B7");
    private static readonly Brush KeyTextBrush = BrushFrom("#F3F5FA");
    private static readonly Brush DarkTextBrush = BrushFrom("#091012");

    public KeyTestView()
    {
        InitializeComponent();
        _state.PropertyChanged += OnStateChanged;
        Loc.LanguageChanged += UpdateCountText;
        Customization.Changed += () => Application.Current?.Dispatcher.Invoke(RepaintAllKeys);
        RenderBoard();
    }

    private void UpdateCountText()
        => CountText.Text = Loc.T("t_keytest_count", _pressCount);

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
    {
        if (e == null || e.PropertyName == nameof(AppState.Layout) || e.PropertyName == nameof(AppState.Definition))
            Application.Current.Dispatcher.Invoke(RenderBoard);
    }

    private void RenderBoard()
    {
        _usageToKeys.Clear();
        TestCanvas.Content = null;

        var keys = BuildVisualKeys();
        var canvas = new Canvas { Background = Brushes.Transparent, Margin = new Thickness(0) };

        foreach (var key in keys)
        {
            var border = CreateKey(key);
            Canvas.SetLeft(border, key.X * KeyUnit + KeyGap / 2);
            Canvas.SetTop(border, key.Y * KeyUnit + KeyGap / 2);
            canvas.Children.Add(border);

            if (key.Usage > 0)
            {
                if (!_usageToKeys.TryGetValue(key.Usage, out var list))
                {
                    list = new List<Border>();
                    _usageToKeys[key.Usage] = list;
                }
                list.Add(border);
            }
        }

        canvas.Width = Math.Max(720, keys.Max(k => k.X + k.W) * KeyUnit + KeyGap);
        canvas.Height = Math.Max(240, keys.Max(k => k.Y + k.H) * KeyUnit + KeyGap);
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = canvas
        };
        viewbox.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth")
        {
            Source = BoardShell,
            Converter = new WidthMinusConverter(),
            ConverterParameter = 24d
        });
        TestCanvas.Content = viewbox;
        RepaintAllKeys();
        UpdateCountText();
    }

    private Border CreateKey(TestKey key)
    {
        var label = new TextBlock
        {
            Text = key.Label,
            FontSize = key.Label.Length > 8 ? 10 : 11.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var b = new Border
        {
            Tag = key.Usage,
            Width = Math.Max(30, key.W * KeyUnit - KeyGap),
            Height = Math.Max(30, key.H * KeyUnit - KeyGap),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Child = label
        };
        b.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        return b;
    }

    public void ReportKey(byte usbUsage, bool isDown = true)
    {
        if (usbUsage == 0) return;
        Application.Current.Dispatcher.Invoke(() => SetUsageState(usbUsage, isDown));
    }

    public void ReportHidReport(byte[] report)
    {
        if (report.Length < 8) return;

        var next = new HashSet<int>();
        var modifier = report[1];
        for (var bit = 0; bit < 8; bit++)
        {
            if ((modifier & (1 << bit)) != 0)
                next.Add(0xE0 + bit);
        }

        foreach (var usage in report.Skip(2).Where(k => k != 0))
            next.Add(usage);

        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var usage in _hidPressed.Except(next).ToList())
                SetUsageState(usage, false, countPress: false);
            foreach (var usage in next.Except(_hidPressed).ToList())
                SetUsageState(usage, true);

            _hidPressed.Clear();
            foreach (var usage in next)
                _hidPressed.Add(usage);
        });
    }

    private void SetUsageState(int usage, bool isDown, bool countPress = true)
    {
        if (isDown)
        {
            if (_pressed.Add(usage) && countPress)
            {
                _tested.Add(usage);
                _pressCount++;
                EventLog.Text = $"{KeycodeMap.Name((uint)usage)} / USB 0x{usage:X2}";
                UpdateCountText();
            }
        }
        else
        {
            _pressed.Remove(usage);
        }

        RepaintUsage(usage);
    }

    private void RepaintAllKeys()
    {
        foreach (var usage in _usageToKeys.Keys.ToList())
            RepaintUsage(usage);
    }

    private void RepaintUsage(int usage)
    {
        if (!_usageToKeys.TryGetValue(usage, out var keys)) return;
        var isPressed = _pressed.Contains(usage);
        var isTested = _tested.Contains(usage);

        var accent = Application.Current?.Resources["AccentBrush"] as Brush ?? PressedBrush;
        var hover = Application.Current?.Resources["BgHoverBrush"] as Brush ?? TestedBrush;
        var card = Application.Current?.Resources["BgCardBrush"] as Brush ?? KeyBrush;
        var borderStrong = Application.Current?.Resources["BorderStrongBrush"] as Brush ?? KeyBorderBrush;
        var textPrimary = Application.Current?.Resources["TextPrimaryBrush"] as Brush ?? KeyTextBrush;

        foreach (var key in keys)
        {
            key.Background = isPressed ? accent : isTested ? hover : card;
            key.BorderBrush = isPressed ? accent : borderStrong;
            if (key.Child is TextBlock text)
                text.Foreground = isPressed ? DarkTextBrush : textPrimary;
        }
    }

    private List<TestKey> BuildVisualKeys()
    {
        var loaded = BuildFromLoadedLayout();
        return loaded.Count > 0 ? loaded : BuildStandardBoard();
    }

    private List<TestKey> BuildFromLoadedLayout()
    {
        var layout = _state.Layout;
        if (layout == null || layout.Keys.Count == 0) return new List<TestKey>();

        // Same duplicate-geometry filter as the keymap view so both tabs
        // render the identical board.
        var seen = new HashSet<(double X, double Y, double W, double H)>();
        var keys = layout.Keys
            .Where(k => !k.IsEncoder)
            .Where(k => seen.Add((k.X, k.Y, k.W, k.H)))
            .GroupBy(k => (k.MatrixRow, k.MatrixCol))
            .Select(g => g
                .OrderBy(k => k.Y)
                .ThenBy(k => k.X)
                .ThenByDescending(k => k.W * k.H)
                .First())
            .Select(k => new TestKey(
                LegendFor(k),
                UsageFromLayoutKey(k),
                k.X,
                k.Y,
                Math.Max(0.75, k.W),
                Math.Max(0.75, k.H)))
            .Where(k => !string.IsNullOrWhiteSpace(k.Label))
            .ToList();

        // VIA keycodes above 0xFF (layer taps, macros, ...) have no direct USB usage.
        // Fall back to the standard board's usage at the same physical position so
        // those keys still light up when pressed.
        var standard = BuildStandardBoard();
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Usage == 0)
                keys[i] = keys[i] with { Usage = UsageFromPosition(standard, keys[i]) };
        }

        return RemoveOverlaps(keys);
    }

    private static int UsageFromPosition(List<TestKey> standard, TestKey key)
    {
        TestKey? best = null;
        var bestDist = double.MaxValue;
        foreach (var s in standard)
        {
            if (s.Usage == 0) continue;
            var d = Math.Abs(s.X - key.X) + Math.Abs(s.Y - key.Y);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }
        return bestDist <= 0.9 && best != null ? best.Usage : 0;
    }

    private static List<TestKey> RemoveOverlaps(List<TestKey> keys)
    {
        var accepted = new List<TestKey>();
        foreach (var key in keys
            .OrderBy(k => k.Y)
            .ThenBy(k => k.X)
            .ThenByDescending(k => k.W * k.H))
        {
            var keyRect = RectOf(key);
            var overlaps = accepted.Any(existing =>
            {
                var existingRect = RectOf(existing);
                existingRect.Intersect(keyRect);
                if (existingRect.IsEmpty) return false;

                var minArea = Math.Min(key.W * key.H, existing.W * existing.H);
                return minArea > 0 && existingRect.Width * existingRect.Height / minArea > 0.35;
            });

            if (!overlaps)
                accepted.Add(key);
        }

        return accepted;
    }

    private static Rect RectOf(TestKey key)
        => new(key.X, key.Y, key.W, key.H);

    private static int UsageFromLayoutKey(LayoutKey key)
    {
        if (key.Keycode > 0 && key.Keycode <= 0xFF)
            return key.Keycode;

        var legend = (key.Keycode > 0 ? KeycodeMap.Name(key.Keycode) : key.Legend ?? "").Trim();
        return UsageFromLegend(legend);
    }

    private static int UsageFromLegend(string legend)
    {
        if (legend.Length == 1)
        {
            var ch = char.ToUpperInvariant(legend[0]);
            if (ch is >= 'A' and <= 'Z') return 0x04 + ch - 'A';
            if (ch is >= '1' and <= '9') return 0x1E + ch - '1';
            if (ch == '0') return 0x27;
        }

        if (legend.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(legend[1..], out var fn) && fn is >= 1 and <= 24)
            return fn <= 12 ? 0x3A + fn - 1 : 0xD4 + fn - 13;

        return legend.ToLowerInvariant() switch
        {
            "enter" => 0x28,
            "esc" or "escape" => 0x29,
            "bksp" or "backspace" => 0x2A,
            "tab" => 0x2B,
            "space" => 0x2C,
            "-_" or "-" => 0x2D,
            "=+" or "=" => 0x2E,
            "[{ " or "[{" or "[" => 0x2F,
            "] }" or "]}" or "]" => 0x30,
            "\\|" => 0x31,
            ";:" or ";" => 0x33,
            "'\"" or "'" => 0x34,
            "`~" or "`" => 0x35,
            ",<" or "," => 0x36,
            ".>" or "." => 0x37,
            "/?" or "/" => 0x38,
            "caps" or "caps lock" => 0x39,
            "prtsc" or "print screen" => 0x46,
            "scrlk" or "scroll lock" => 0x47,
            "pause" => 0x48,
            "ins" or "insert" => 0x49,
            "home" => 0x4A,
            "pgup" or "page up" => 0x4B,
            "del" or "delete" => 0x4C,
            "end" => 0x4D,
            "pgdn" or "page down" => 0x4E,
            "right" or "→" => 0x4F,
            "left" or "←" => 0x50,
            "down" or "↓" => 0x51,
            "up" or "↑" => 0x52,
            "numlck" or "num lock" => 0x53,
            "l ctrl" or "ctrl" => 0xE0,
            "l shift" or "shift" => 0xE1,
            "l alt" or "alt" => 0xE2,
            "l gui" or "win" => 0xE3,
            "r ctrl" => 0xE4,
            "r shift" => 0xE5,
            "r alt" => 0xE6,
            "r gui" => 0xE7,
            _ => 0
        };
    }

    private string LegendFor(LayoutKey key)
    {
        if (key.Keycode == 0) return key.Legend ?? "";
        return KeycodeMap.Name(key.Keycode);
    }

    private static List<TestKey> BuildStandardBoard()
    {
        var keys = new List<TestKey>();
        void Add(string label, int usage, double x, double y, double w = 1, double h = 1)
            => keys.Add(new TestKey(label, usage, x, y, w, h));

        Add("Esc", 0x29, 0, 0);
        for (var i = 1; i <= 12; i++)
            Add($"F{i}", 0x3A + i - 1, 1.5 + i + (i > 4 ? 0.35 : 0) + (i > 8 ? 0.35 : 0), 0);
        Add("Prt\nSc", 0x46, 15.2, 0); Add("Scr\nLk", 0x47, 16.25, 0); Add("Pause", 0x48, 17.3, 0);

        var row1 = new[] { "`~", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-_", "=+" };
        for (var i = 0; i < row1.Length; i++) Add(row1[i], UsageFromLegend(row1[i]), i, 1.25);
        Add("Backspace", 0x2A, 13, 1.25, 2);
        Add("Ins", 0x49, 15.2, 1.25); Add("Home", 0x4A, 16.25, 1.25); Add("Pg\nUp", 0x4B, 17.3, 1.25);
        Add("Num\nLock", 0x53, 18.65, 1.25); Add("/", 0x54, 19.7, 1.25); Add("*", 0x55, 20.75, 1.25); Add("-", 0x56, 21.8, 1.25);

        var row2 = new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[{", "]}", "\\|" };
        Add("Tab", 0x2B, 0, 2.3, 1.5);
        for (var i = 0; i < row2.Length; i++) Add(row2[i], UsageFromLegend(row2[i]), 1.5 + i, 2.3);
        Add("Del", 0x4C, 15.2, 2.3); Add("End", 0x4D, 16.25, 2.3); Add("Pg\nDn", 0x4E, 17.3, 2.3);
        Add("7", 0x5F, 18.65, 2.3); Add("8", 0x60, 19.7, 2.3); Add("9", 0x61, 20.75, 2.3); Add("+", 0x57, 21.8, 2.3, 1, 2.05);

        var row3 = new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";:", "'\"" };
        Add("Caps", 0x39, 0, 3.35, 1.75);
        for (var i = 0; i < row3.Length; i++) Add(row3[i], UsageFromLegend(row3[i]), 1.75 + i, 3.35);
        Add("Enter", 0x28, 12.75, 3.35, 2.25);
        Add("4", 0x5C, 18.65, 3.35); Add("5", 0x5D, 19.7, 3.35); Add("6", 0x5E, 20.75, 3.35);

        var row4 = new[] { "Z", "X", "C", "V", "B", "N", "M", ",<", ".>", "/?" };
        Add("Shift", 0xE1, 0, 4.4, 2.25);
        for (var i = 0; i < row4.Length; i++) Add(row4[i], UsageFromLegend(row4[i]), 2.25 + i, 4.4);
        Add("Shift", 0xE5, 12.25, 4.4, 2.75);
        Add("↑", 0x52, 16.25, 4.4);
        Add("1", 0x59, 18.65, 4.4); Add("2", 0x5A, 19.7, 4.4); Add("3", 0x5B, 20.75, 4.4); Add("Enter", 0x58, 21.8, 4.4, 1, 2.05);

        Add("Ctrl", 0xE0, 0, 5.45, 1.25); Add("Win", 0xE3, 1.25, 5.45, 1.25); Add("Alt", 0xE2, 2.5, 5.45, 1.25);
        Add("", 0x2C, 3.75, 5.45, 6.25); Add("Alt", 0xE6, 10, 5.45, 1.25); Add("Menu", 0x65, 11.25, 5.45, 1.25); Add("Ctrl", 0xE4, 12.5, 5.45, 1.25);
        Add("←", 0x50, 15.2, 5.45); Add("↓", 0x51, 16.25, 5.45); Add("→", 0x4F, 17.3, 5.45);
        Add("0", 0x62, 18.65, 5.45, 2.05); Add(".", 0x63, 20.75, 5.45);

        return keys;
    }

    private void BtnClearTest_Click(object sender, RoutedEventArgs e)
    {
        _pressCount = 0;
        _tested.Clear();
        _pressed.Clear();
        _hidPressed.Clear();
        EventLog.SetResourceReference(TextBlock.TextProperty, "t_keytest_no_events");
        UpdateCountText();
        RepaintAllKeys();
    }

    private static SolidColorBrush BrushFrom(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    private sealed record TestKey(string Label, int Usage, double X, double Y, double W, double H);
}

public class WidthMinusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var width = value is double d ? d : 0;
        var minus = parameter is double pd ? pd : 0;
        return Math.Max(240, width - minus);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => Binding.DoNothing;
}
