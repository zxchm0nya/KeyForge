using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KyeForge.App.Views;

public partial class ColorPickerDialog : Window
{
    public Color SelectedColor { get; private set; }

    private double _h;   // 0..360
    private double _s;   // 0..1
    private double _v;   // 0..1
    private bool _dragging;

    public ColorPickerDialog(Color initial)
    {
        InitializeComponent();
        RgbToHsv(initial, out _h, out _s, out _v);
        HueSlider.Value = _h;
        SvArea.SizeChanged += (_, _) => UpdateAll();
        UpdateAll();
        UiAnimations.FadeSlideIn(this);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            UiAnimations.AnimatedClose(this, false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !HexBox.IsFocused)
        {
            OK_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject d)
        {
            if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(d) != null) return;
            if (FindAncestor<TextBox>(d) != null) return;
            if (FindAncestor<Slider>(d) != null) return;
            if (FindAncestorFrom(d, SvArea)) return;
        }
        try { DragMove(); } catch { }
    }

    private static bool FindAncestorFrom(DependencyObject? start, DependencyObject? ancestor)
    {
        if (ancestor == null) return false;
        var cur = start;
        while (cur != null)
        {
            if (ReferenceEquals(cur, ancestor)) return true;
            try
            {
                if (cur is Visual or System.Windows.Media.Media3D.Visual3D)
                    cur = VisualTreeHelper.GetParent(cur);
                else if (cur is FrameworkContentElement fce)
                    cur = fce.Parent;
                else
                    break;
            }
            catch
            {
                break;
            }
        }
        return false;
    }

    // ---------------- Shade field ----------------

    private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = true;
        SvArea.CaptureMouse();
        PickSv(e.GetPosition(SvArea));
    }

    private void Sv_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true;
            PickSv(e.GetPosition(SvArea));
        }
    }

    private void Sv_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true;
            _dragging = false;
            SvArea.ReleaseMouseCapture();
        }
    }

    private void Sv_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragging = false;
    }

    private void PickSv(Point p)
    {
        double w = SvArea.ActualWidth > 0 ? SvArea.ActualWidth : 1;
        double h = SvArea.ActualHeight > 0 ? SvArea.ActualHeight : 1;
        _s = Math.Clamp(p.X / w, 0, 1);
        _v = 1 - Math.Clamp(p.Y / h, 0, 1);
        UpdateAll();
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _h = Math.Clamp(HueSlider.Value, 0, 360);
        UpdateAll();
    }

    // ---------------- Hex input ----------------

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHex();
            e.Handled = true;
        }
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        var text = HexBox.Text.Trim().TrimStart('#');
        if (text.Length is not (6 or 8)) return;
        if (!Customization.TryParse("#" + text, out var c)) return;
        RgbToHsv(c, out _h, out _s, out _v);
        HueSlider.Value = _h;
        UpdateAll();
    }

    // ---------------- State sync ----------------

    private void UpdateAll()
    {
        var hueColor = HsvToColor(_h, 1, 1);
        SvHue.Background = new SolidColorBrush(hueColor);
        SelectedColor = HsvToColor(_h, _s, _v);
        Preview.Background = new SolidColorBrush(SelectedColor);

        double w = SvArea.ActualWidth > 0 ? SvArea.ActualWidth : 336;
        double h = SvArea.ActualHeight > 0 ? SvArea.ActualHeight : 200;
        double x = _s * w;
        double y = (1 - _v) * h;
        Canvas.SetLeft(SvThumb, Math.Clamp(x - 9, -9, w - 9));
        Canvas.SetTop(SvThumb, Math.Clamp(y - 9, -9, h - 9));

        if (!HexBox.IsFocused)
        {
            HexBox.Text = "#" + SelectedColor.R.ToString("X2") + SelectedColor.G.ToString("X2") + SelectedColor.B.ToString("X2");
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ApplyHex();
        UpdateAll();
        UiAnimations.AnimatedClose(this, true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => UiAnimations.AnimatedClose(this, false);

    // ---------------- HSV helpers ----------------

    public static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0.00001)
        {
            if (Math.Abs(max - r) < 0.00001)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 0.00001)
            {
                h = 60 * ((b - r) / delta + 2);
            }
            else
            {
                h = 60 * ((r - g) / delta + 4);
            }
        }
        if (h < 0) h += 360;
        if (h >= 360) h = 0;

        s = max <= 0.00001 ? 0 : delta / max;
        v = max;

        h = Math.Clamp(h, 0, 360);
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
    }

    public static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255));
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var cur = start;
        while (cur != null)
        {
            if (cur is T t) return t;
            try
            {
                if (cur is Visual or System.Windows.Media.Media3D.Visual3D)
                    cur = VisualTreeHelper.GetParent(cur);
                else if (cur is FrameworkContentElement fce)
                    cur = fce.Parent;
                else
                    break;
            }
            catch
            {
                break;
            }
        }
        return null;
    }
}
