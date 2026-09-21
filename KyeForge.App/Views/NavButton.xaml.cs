using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KyeForge.App.Views;

public partial class NavButton : UserControl
{
    private static readonly Dictionary<string, string> IconFiles = new()
    {
        ["devices"] = "Assets/icons/devices.png",
        ["keymap"] = "Assets/icons/keymap.png",
        ["lighting"] = "Assets/icons/lighting.png",
        ["keytest"] = "Assets/icons/keytest.png",
        ["settings"] = "Assets/icons/settings.png",
        // legacy glyph keys
        ["W1"] = "Assets/icons/devices.png",
        ["W2"] = "Assets/icons/keymap.png",
        ["W3"] = "Assets/icons/lighting.png",
        ["W4"] = "Assets/icons/keytest.png",
        ["W5"] = "Assets/icons/settings.png",
    };

    private static string NormalizeIconKey(string key) => key switch
    {
        "W1" => "devices",
        "W2" => "keymap",
        "W3" => "lighting",
        "W4" => "keytest",
        "W5" => "settings",
        _ => key
    };

    private sealed class IconSet
    {
        public bool IsGif;
        public BitmapSource? StaticNormal;
        public BitmapSource? StaticSelected;
        public List<BitmapSource>? GifNormal;
        public List<BitmapSource>? GifSelected;
        public List<TimeSpan>? Delays;
    }

    private static readonly Dictionary<string, IconSet> IconCache = new();
    private static readonly List<WeakReference<NavButton>> Instances = new();

    private IconSet? _iconSet;
    private string _iconKey = "";
    private int _gifIndex;
    private DispatcherTimer? _gifTimer;
    private bool _gifPlaying;

    public NavButton()
    {
        InitializeComponent();
        lock (Instances) Instances.Add(new WeakReference<NavButton>(this));
        Loaded += (_, _) =>
        {
            IsSelected = _selected;
            SetupIcon(_iconKey);
            Services.Customization.Changed += OnCustomizationChanged;
        };
        Unloaded += (_, _) =>
        {
            Services.Customization.Changed -= OnCustomizationChanged;
            StopGif();
        };
    }

    /// <summary>Pauses all nav GIFs (called when the window goes inactive).</summary>
    public static void PauseIconAnimations()
    {
        lock (Instances)
        {
            foreach (var wr in Instances)
                if (wr.TryGetTarget(out var btn))
                    btn.PauseGif();
        }
    }

    /// <summary>Resumes all nav GIFs (called when the window activates).</summary>
    public static void ResumeIconAnimations()
    {
        lock (Instances)
        {
            foreach (var wr in Instances)
                if (wr.TryGetTarget(out var btn))
                    btn.ResumeGif();
        }
    }

    private void OnCustomizationChanged()
    {
        // Accent may have changed -> rebuild selected-state bitmaps.
        lock (IconCache) IconCache.Clear();
        if (IsLoaded) SetupIcon(_iconKey);
        UpdateVisuals(false);
    }

    private static Brush Res(string key, string fallbackHex)
        => Application.Current?.TryFindResource(key) as SolidColorBrush
           ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavButton), new PropertyMetadata("", OnLabelChanged));
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NavButton)d).LabelText.Text = (string)e.NewValue;

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavButton), new PropertyMetadata("", OnIconChanged));
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var btn = (NavButton)d;
        btn._iconKey = NormalizeIconKey((string)e.NewValue);
        if (btn.IsLoaded) btn.SetupIcon(btn._iconKey);
    }

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            if (IsLoaded) UpdateVisuals(true);
        }
    }

    public event MouseButtonEventHandler? NavClicked;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var scale = RenderTransform as ScaleTransform;
        if (scale != null)
        {
            var sb = new Storyboard();
            var sx = new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sx, scale);
            Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
            var sy = new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sy, scale);
            Storyboard.SetTargetProperty(sy, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(sx); sb.Children.Add(sy);
            sb.Begin(this);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var scale = RenderTransform as ScaleTransform;
        if (scale != null)
        {
            var sb = new Storyboard();
            var sx = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sx, scale);
            Storyboard.SetTargetProperty(sx, new PropertyPath(ScaleTransform.ScaleXProperty));
            var sy = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sy, scale);
            Storyboard.SetTargetProperty(sy, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(sx); sb.Children.Add(sy);
            sb.Begin(this);
        }
        if (_iconKey == "settings") SpinGear();
        NavClicked?.Invoke(this, e);
    }

    /// <summary>Spins the settings gear 360° on click.</summary>
    private void SpinGear()
    {
        try
        {
            var rotate = new RotateTransform(0);
            IconImage.RenderTransform = rotate;
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        catch { }
    }

    // ---------------- Icons ----------------

    private void SetupIcon(string key)
    {
        try
        {
            StopGif();
            if (string.IsNullOrEmpty(key) || !IconFiles.TryGetValue(key, out var file))
            {
                _iconSet = null;
                IconImage.Source = null;
                return;
            }

            lock (IconCache)
            {
                if (!IconCache.TryGetValue(key, out var set))
                {
                    set = LoadIconSet(file);
                    if (set != null) IconCache[key] = set;
                }
                _iconSet = set;
            }

            if (_iconSet == null) return;
            _gifIndex = 0;
            RefreshIconState();

            if (_iconSet.IsGif && _iconSet.Delays is { Count: > 1 })
            {
                _gifTimer = new DispatcherTimer { Interval = _iconSet.Delays[0] };
                _gifTimer.Tick += (_, _) => AdvanceGifFrame();
                _gifTimer.Start();
                _gifPlaying = true;
            }
        }
        catch { }
    }

    private void RefreshIconState()
    {
        if (_iconSet == null) return;
        try
        {
            if (_iconSet.IsGif)
            {
                var frames = _selected ? _iconSet.GifSelected : _iconSet.GifNormal;
                if (frames != null && frames.Count > 0)
                    IconImage.Source = frames[Math.Min(_gifIndex, frames.Count - 1)];
            }
            else
            {
                IconImage.Source = _selected ? _iconSet.StaticSelected : _iconSet.StaticNormal;
            }
        }
        catch { }
    }

    private void AdvanceGifFrame()
    {
        if (_iconSet?.GifNormal == null || _iconSet.GifNormal.Count == 0) return;
        _gifIndex = (_gifIndex + 1) % _iconSet.GifNormal.Count;
        RefreshIconState();
        if (_gifTimer != null && _iconSet.Delays != null)
            _gifTimer.Interval = _iconSet.Delays[_gifIndex];
    }

    private void StopGif()
    {
        try { _gifTimer?.Stop(); } catch { }
        _gifTimer = null;
        _gifPlaying = false;
    }

    private void PauseGif()
    {
        if (_gifPlaying)
        {
            try { _gifTimer?.Stop(); } catch { }
        }
    }

    private void ResumeGif()
    {
        if (_gifPlaying && _gifTimer != null)
        {
            try { _gifTimer.Start(); } catch { }
        }
    }

    private static IconSet? LoadIconSet(string file)
    {
        try
        {
            // Assembly-qualified: the short form resolves against the entry
            // assembly and fails (e.g. single-file publish / test hosts).
            var uri = new Uri("pack://application:,,,/KyeForge;component/" + file);
            bool isGif = file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

            var normal = Res("TextSecondaryBrush", "#AEB8C2") is SolidColorBrush nb ? nb.Color : Color.FromRgb(0xAE, 0xB8, 0xC2);
            var selected = Res("AccentBrush", "#28D7B7") is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x28, 0xD7, 0xB7);

            if (!isGif)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = uri;
                img.EndInit();
                img.Freeze();
                return new IconSet
                {
                    IsGif = false,
                    StaticNormal = Recolor(img, normal),
                    StaticSelected = Recolor(img, selected),
                };
            }

            var decoder = new GifBitmapDecoder(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var set = new IconSet
            {
                IsGif = true,
                GifNormal = new List<BitmapSource>(decoder.Frames.Count),
                GifSelected = new List<BitmapSource>(decoder.Frames.Count),
                Delays = new List<TimeSpan>(decoder.Frames.Count),
            };
            foreach (var f in decoder.Frames)
            {
                set.GifNormal.Add(Recolor(f, normal));
                set.GifSelected.Add(Recolor(f, selected));
                set.Delays.Add(GetGifFrameDelay(f));
            }
            return set;
        }
        catch { return null; }
    }

    private static TimeSpan GetGifFrameDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctlext/Delay"))
            {
                var delay = Convert.ToUInt16(meta.GetQuery("/grctlext/Delay"));
                return TimeSpan.FromMilliseconds(Math.Clamp(delay * 10, 20, 1000));
            }
        }
        catch { }
        return TimeSpan.FromMilliseconds(100);
    }

    /// <summary>
    /// Recolors a black icons8 glyph to the target color, preserving per-pixel
    /// alpha (premultiplied) so edges stay smooth on the dark theme.
    /// </summary>
    private static BitmapSource Recolor(BitmapSource src, Color target)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        int[] pixels = new int[w * h];
        conv.CopyPixels(pixels, w * 4, 0);
        for (int i = 0; i < pixels.Length; i++)
        {
            int a = (pixels[i] >> 24) & 0xFF;
            if (a == 0) continue;
            pixels[i] = (a << 24)
                        | ((target.R * a / 255) << 16)
                        | ((target.G * a / 255) << 8)
                        | (target.B * a / 255);
        }
        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        wb.Freeze();
        return wb;
    }

    private void UpdateVisuals(bool animate)
    {
        // The sliding pill in MainWindow draws the selection; buttons only tint text + icon.
        Container.Background = Brushes.Transparent;
        Container.BorderBrush = Brushes.Transparent;
        Container.BorderThickness = new Thickness(0);

        if (!_selected)
        {
            LabelText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }
        else
        {
            LabelText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        }
        RefreshIconState();
    }
}
