using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

public partial class LightingView : UserControl, INotifyPropertyChanged
{
    private readonly AppState _state = StateHub.State;

    private Color _color1 = Color.FromRgb(255, 255, 255);
    private bool _initDone;
    private bool _updatingCombo;

    public LightingView()
    {
        InitializeComponent();
        DataContext = this;

        EffectCombo.ItemsSource = BuildEffects();
        EffectCombo.DisplayMemberPath = "Name";
        EffectCombo.SelectedIndex = 0;
        ApplyConfigRanges();
        UpdateConfigGate();
        BrightnessSlider.Value = 80;
        SpeedSlider.Value = 80;

        _color1 = Color.FromRgb(124, 140, 255);
        ApplyColorSwatches();
        UpdateColorDisplay();

        Color1Click = new RelayCommand(() => _ = PickColorAndApply());

        _state.PropertyChanged += OnStateChanged;
        _initDone = true;
        UpdatePreview();
        Loc.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(BrightnessLabel));
            OnPropertyChanged(nameof(SpeedLabel));
        };

        // Keep the radial glow inside the rounded preview corners
        PreviewBox.SizeChanged += (_, e) =>
            PreviewGlow.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    }

    public ICommand? Color1Click { get; private set; }

    public string BrightnessLabel => Loc.T("t_light_brightness", BrightnessSlider.Value);
    public string SpeedLabel => Loc.T("t_light_speed", SpeedSlider.Value);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isClient = e.PropertyName == nameof(AppState.Client);
        if (e.PropertyName != nameof(AppState.Definition) && !isClient) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateConfigGate();
            ApplyConfigRanges();

            // Rebuild the effect list WITHOUT pushing anything to the keyboard:
            // auto-selecting an item must never overwrite the saved profile.
            _updatingCombo = true;
            var selectedValue = (EffectCombo.SelectedItem as EffectItem)?.Value;
            EffectCombo.ItemsSource = BuildEffects();
            EffectCombo.DisplayMemberPath = "Name";
            EffectCombo.SelectedItem = EffectCombo.Items.Cast<EffectItem>()
                .FirstOrDefault(i => i.Value == selectedValue);
            if (EffectCombo.SelectedItem == null && EffectCombo.Items.Count > 0)
                EffectCombo.SelectedIndex = 0;
            _updatingCombo = false;

            // Show the locally saved profile (from the last "Save") if there is one.
            ApplySavedProfile();

            // Connected with a config loaded: restore the keyboard's current
            // lighting profile into the UI (effect, brightness, speed, color).
            if (_state.Client != null && _state.Definition != null)
                _ = RestoreLightingFromDeviceAsync();
        });
    }

    private void ApplySavedProfile()
    {
        var s = AppSettings.Load();
        if (s.LightingEffect.HasValue)
        {
            var match = EffectCombo.Items.Cast<EffectItem>().FirstOrDefault(i => i.Value == s.LightingEffect.Value);
            if (match != null)
            {
                _updatingCombo = true;
                EffectCombo.SelectedItem = match;
                _updatingCombo = false;
            }
        }
        if (s.LightingBrightness.HasValue && s.LightingBrightness.Value >= BrightnessSlider.Minimum && s.LightingBrightness.Value <= BrightnessSlider.Maximum)
            BrightnessSlider.Value = s.LightingBrightness.Value;
        if (s.LightingSpeed.HasValue && s.LightingSpeed.Value >= SpeedSlider.Minimum && s.LightingSpeed.Value <= SpeedSlider.Maximum)
            SpeedSlider.Value = s.LightingSpeed.Value;
        if (Customization.TryParse(s.LightingColor, out var c))
        {
            _color1 = c;
            UpdateColorDisplay();
        }
    }

    private void SaveProfileLocally()
    {
        var s = AppSettings.Load();
        s.LightingEffect = (EffectCombo.SelectedItem as EffectItem)?.Value;
        s.LightingBrightness = BrightnessSlider.Value;
        s.LightingSpeed = SpeedSlider.Value;
        s.LightingColor = Customization.ToHex(_color1);
        s.Save();
    }

    private async Task RestoreLightingFromDeviceAsync()
    {
        var qk = _state.Definition?.QkLighting;
        var client = _state.Client;
        if (qk == null || client == null) return;

        try
        {
            var eff = await client.GetQkValueAsync(qk.Group, qk.EffectSub);
            if (eff != null && eff.Success)
            {
                var match = EffectCombo.Items.Cast<EffectItem>().FirstOrDefault(i => i.Value == eff.Value);
                if (match != null)
                {
                    _updatingCombo = true;
                    EffectCombo.SelectedItem = match;
                    _updatingCombo = false;
                }
            }

            var br = await client.GetQkValueAsync(qk.Group, qk.BrightnessSub);
            if (br != null && br.Success && br.Value >= BrightnessSlider.Minimum && br.Value <= BrightnessSlider.Maximum)
                BrightnessSlider.Value = br.Value;

            var sp = await client.GetQkValueAsync(qk.Group, qk.SpeedSub);
            if (sp != null && sp.Success && sp.Value >= SpeedSlider.Minimum && sp.Value <= SpeedSlider.Maximum)
                SpeedSlider.Value = sp.Value;

            // Color comes back as hue+sat; value (brightness) is kept from the slider.
            var col = await client.GetQkValueAsync(qk.Group, qk.ColorSub);
            if (col != null && col.Success)
            {
                byte sat = col.Extra != null && col.Extra.Length > 0 ? col.Extra[0] : (byte)255;
                _color1 = ColorPickerDialog.HsvToColor(col.Value / 255.0 * 360.0, sat / 255.0, 1.0);
                UpdateColorDisplay();
            }
        }
        catch { }
    }

    private List<EffectItem> BuildEffects()
    {
        var list = new List<EffectItem>();
        // No JSON config loaded -> no invented defaults; effects come only from the file.
        var qk = _state.Definition?.QkLighting;
        if (qk == null && _state.Definition?.Lighting?.UnderglowEffects == null)
            return list;

        if (qk != null && qk.HasEffects)
        {
            foreach (var e in qk.Effects)
                list.Add(new EffectItem { Name = e.Name, Value = e.Value });
            return list;
        }

        if (_state.Definition?.Lighting?.UnderglowEffects != null)
        {
            foreach (var item in _state.Definition.Lighting.UnderglowEffects)
            {
                if (item.Count >= 2)
                {
                    var name = ValueToString(item[0]);
                    if (!string.IsNullOrWhiteSpace(name) && TryValueToInt(item[1], out var value))
                        list.Add(new EffectItem { Name = name, Value = value });
                }
            }
        }
        return list;
    }

    /// <summary>Keyboard settings are defined by the loaded JSON only - without a
    /// config the lighting controls stay hidden instead of showing made-up values.</summary>
    private void UpdateConfigGate()
    {
        var hasConfig = _state.Definition != null;
        TypeCard.Visibility = hasConfig ? Visibility.Visible : Visibility.Collapsed;
        ControlsCard.Visibility = hasConfig ? Visibility.Visible : Visibility.Collapsed;
        NoConfigHint.Visibility = hasConfig ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string ValueToString(object? value)
    {
        if (value is string s) return s;
        if (value is JsonElement el)
            return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
        return value?.ToString() ?? "";
    }

    private static bool TryValueToInt(object? value, out int result)
    {
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = (int)l; return true; }
        if (value is double d) { result = (int)d; return true; }
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out result)) return true;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out result)) return true;
        }
        return int.TryParse(value?.ToString(), out result);
    }

    private void ApplyConfigRanges()
    {
        var qk = _state.Definition?.QkLighting;
        if (qk == null) return;
        BrightnessSlider.Minimum = qk.BrightnessMin;
        BrightnessSlider.Maximum = qk.BrightnessMax;
        SpeedSlider.Minimum = qk.SpeedMin;
        SpeedSlider.Maximum = qk.SpeedMax;
    }

    private async void Color1Box_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await PickColorAndApply();
    }

    private async void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: SwatchItem item })
        {
            await SelectedFromSwatch(item.Color);
        }
    }

    private async Task PickColorAndApply()
    {
        var owner = Window.GetWindow(this);
        var picker = new ColorPickerDialog(_color1);
        if (owner != null && owner.IsLoaded)
            picker.Owner = owner;

        if (picker.ShowDialog() == true)
        {
            _color1 = picker.SelectedColor;
            UpdateColorDisplay();
            await PushColorAsync(_color1);
        }
    }

    private void UpdateColorDisplay()
    {
        Color1Box.Background = new SolidColorBrush(_color1);
        Color1Hex.Text = "#" + _color1.R.ToString("X2") + _color1.G.ToString("X2") + _color1.B.ToString("X2");
        UpdatePreview();
    }

    private void ApplyColorSwatches()
    {
        var preset = new[]
        {
            Color.FromRgb(124,140,255), Color.FromRgb(143,108,255),
            Color.FromRgb(255,90,120), Color.FromRgb(52,211,153),
            Color.FromRgb(251,191,36), Color.FromRgb(6,182,212),
            Color.FromRgb(255,255,255), Color.FromRgb(56,64,90),
        };

        var items = preset.Select(c => new SwatchItem(c, () => _ = SelectedFromSwatch(c))).ToList();
        Swatches.ItemsSource = items;
    }

    private async Task SelectedFromSwatch(Color c)
    {
        _color1 = c;
        UpdateColorDisplay();
        await PushColorAsync(c);
    }

    private void UpdatePreview()
    {
        if (!_initDone || PreviewGlow == null) return;
        // Solid fill of the selected color across the whole preview field
        PreviewGlow.Background = new SolidColorBrush(_color1);
    }

    private void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initDone || _updatingCombo) return;
        if (EffectCombo.SelectedItem is EffectItem it)
        {
            _ = PushValueAsync(ViaCommands.LightingEffect, (byte)it.Value);
            SaveProfileLocally();
        }
        Keyboard.ClearFocus();
    }

    private void EffectCombo_DropDownClosed(object? sender, EventArgs e)
    {
        Keyboard.ClearFocus();
    }

    private void EffectCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!EffectCombo.IsDropDownOpen)
        {
            e.Handled = true;
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OnPropertyChanged(nameof(BrightnessLabel));
        UpdatePreview();
        if (!_initDone) return;
        _ = PushValueAsync(ViaCommands.LightingBrightness, (byte)BrightnessSlider.Value);
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OnPropertyChanged(nameof(SpeedLabel));
        if (!_initDone) return;
        _ = PushValueAsync(ViaCommands.LightingEffectSpeed, (byte)SpeedSlider.Value);
    }

    /// <summary>
    /// Color is sent exactly like the official VIA app: ONE custom-set command
    /// carrying Hue + Saturation (VIA stores colors in HSV, brightness separately).
    /// </summary>
    private async Task PushColorAsync(Color c)
    {
        var qk = _state.Definition?.QkLighting;
        if (qk == null || _state.Client == null) return;

        ColorToHsv(c, out var hue, out var sat, out _);
        await Task.Run(() =>
            _state.Client?.SetQkValueDataAsync(qk.Group, qk.ColorSub, hue, sat));
        SaveProfileLocally();
    }

    private static void ColorToHsv(Color c, out byte hue, out byte sat, out byte val)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0 ? 0 : delta / max;
        double v = max;

        hue = (byte)Math.Round(h / 360.0 * 255.0);
        sat = (byte)Math.Round(s * 255.0);
        val = (byte)Math.Round(v * 255.0);
    }

    private async Task PushValueAsync(byte valueId, byte value)
    {
        var qk = _state.Definition?.QkLighting;
        if (qk == null || _state.Client == null) return;
        await Task.Run(() => _state.Client.SetQkValueAsync(qk.Group, valueId, value));
        SaveProfileLocally();
    }

    private async void BtnSaveLighting_Click(object sender, RoutedEventArgs e)
    {
        var qk = _state.Definition?.QkLighting;
        if (_state.Client == null || qk == null) { MessageBox.Show(Loc.T("t_msg_connect_first"), "KeyForge"); return; }

        // Persist to keyboard EEPROM and mirror the profile inside the app
        await _state.Client.SaveQkChannelAsync(qk.Group);
        SaveProfileLocally();
        _state.DeviceStatus = Loc.T("t_stat_lighting_saved");

        // Visual confirmation on the button
        BtnSaveLighting.IsEnabled = false;
        BtnSaveLighting.Content = Loc.T("t_light_saved_check");
        await Task.Delay(1600);
        BtnSaveLighting.SetResourceReference(System.Windows.Controls.Button.ContentProperty, "t_light_btn_save");
        BtnSaveLighting.IsEnabled = true;
    }

    public class EffectItem { public string Name { get; set; } = ""; public int Value { get; set; } }
    public class SwatchItem
    {
        public ICommand Select { get; }
        public Brush Swatch { get; }
        public Color Color { get; }
        public SwatchItem(Color c, Action onSelect)
        {
            Color = c;
            Swatch = new SolidColorBrush(c);
            Select = new RelayCommand(onSelect);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
