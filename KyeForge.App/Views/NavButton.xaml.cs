using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KyeForge.App.Views;

public partial class NavButton : UserControl
{
    private static readonly Brush BgSelected = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#123129"));
    private static readonly Brush BorderSelected = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E9A85"));
    private static readonly Brush TextPrimary = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F5FA"));
    private static readonly Brush TextSecondary = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AEB8C2"));
    private static readonly Brush Accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28D7B7"));

    public NavButton()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            IsSelected = _selected;
            Services.Customization.Changed += OnCustomizationChanged;
        };
        Unloaded += (_, _) => Services.Customization.Changed -= OnCustomizationChanged;
    }

    private void OnCustomizationChanged() => UpdateVisuals(false);

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
        => ((NavButton)d).IconText.Text = IconGlyph.Map((string)e.NewValue);

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
        NavClicked?.Invoke(this, e);
    }

    private void UpdateVisuals(bool animate)
    {
        if (!_selected)
        {
            Container.Background = Brushes.Transparent;
            Container.BorderBrush = Brushes.Transparent;
            Container.BorderThickness = new Thickness(0);
            LabelText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            IconText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return;
        }

        Container.Background = Res("AccentGlowBrush", "#123129");
        Container.BorderBrush = Res("AccentBrush", "#28D7B7");
        Container.BorderThickness = new Thickness(1);
        LabelText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        IconText.Foreground = Res("AccentBrush", "#28D7B7");
    }
}

/// <summary>Maps short icon keys to Segoe MDL2 Assets glyphs.</summary>
public static class IconGlyph
{
    public static string Map(string key) => key switch
    {
        "W1" => "\uE7F8", // USB - devices / connection
        "W2" => "\uE765", // Keyboard - keymap
        "W3" => "\uE7B3", // Light - lighting
        "W4" => "\uE7C9", // Touch pointer - key test
        "W5" => "\uE713", // Settings gear
        _ => "\uE787",    // Setting
    };
}