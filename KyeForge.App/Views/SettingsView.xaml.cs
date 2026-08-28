using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KyeForge.App.Services;
using Microsoft.Win32;

namespace KyeForge.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _initDone;

    private static readonly (string Hex, string Key)[] Presets =
    {
        ("#28D7B7", "t_custom_preset_default"),
        ("#A78BFA", "t_custom_preset_violet"),
        ("#61A8FF", "t_custom_preset_blue"),
        ("#F472B6", "t_custom_preset_pink"),
        ("#FB923C", "t_custom_preset_orange"),
        ("#A3E635", "t_custom_preset_lime"),
    };

    public SettingsView()
    {
        InitializeComponent();

        LanguageCombo.Items.Clear();
        var en = new ComboBoxItem { Content = Loc.T("t_settings_lang_en"), Tag = "en" };
        var ru = new ComboBoxItem { Content = Loc.T("t_settings_lang_ru"), Tag = "ru" };
        LanguageCombo.Items.Add(en);
        LanguageCombo.Items.Add(ru);
        LanguageCombo.SelectedItem = (LanguageCombo.Items.Cast<ComboBoxItem>())
            .FirstOrDefault(i => (string)i.Tag == Loc.Current) ?? en;

        ChkRememberDevice.IsChecked = _settings.RememberLastDevice;
        ChkRememberConfig.IsChecked = _settings.RememberLastConfig;

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = Loc.T("t_settings_version", ver != null ? ver.ToString(3) : "1.0");

        BuildPresetRow();
        RefreshChips();
        DimSlider.Value = _settings.BackgroundDim;
        BlurSlider.Value = _settings.BackgroundBlur;
        UpdateDimLabel();
        UpdateBlurLabel();

        Loc.LanguageChanged += RefreshLanguageItems;
        _initDone = true;
    }

    // ---------------- Presets ----------------

    private void BuildPresetRow()
    {
        PresetRow.Children.Clear();
        foreach (var (hex, key) in Presets)
        {
            var color = Customization.TryParse(hex, out var c) ? c : default;
            var chip = new Border
            {
                Width = 38,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                Tag = hex,
                ToolTip = Loc.T(key)
            };
            chip.MouseLeftButtonDown += Preset_Click;
            PresetRow.Children.Add(chip);
        }
    }

    private void Preset_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Border { Tag: string hex } && Customization.TryParse(hex, out var c))
        {
            _settings.AccentColor = hex;
            _settings.Save();
            Customization.ApplyAccent(c);
            RefreshChips();
        }
    }

    // ---------------- Color slots ----------------

    private void RefreshChips()
    {
        SetChip(AccentChip, _settings.AccentColor, "#28D7B7");
        SetChip(BgChip, _settings.BgColor, "#090B0F");
        SetChip(PanelChip, _settings.PanelColor, "#101419");
        SetChip(CardChip, _settings.CardColor, "#151B22");
        SetChip(TextChip, _settings.TextColor, "#F3F5FA");
    }

    private static void SetChip(Border chip, string custom, string fallback)
    {
        var hex = string.IsNullOrWhiteSpace(custom) ? fallback : custom;
        chip.Background = Customization.TryParse(hex, out var c)
            ? new SolidColorBrush(c)
            : Brushes.Transparent;
        chip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x46, 0x53, 0x5D));
        chip.BorderThickness = new Thickness(1);
    }

    private void Chip_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border chip || chip.Tag is not string slot) return;

        var current = slot switch
        {
            "accent" => _settings.AccentColor,
            "bg" => _settings.BgColor,
            "panel" => _settings.PanelColor,
            "card" => _settings.CardColor,
            "text" => _settings.TextColor,
            _ => ""
        };
        var fallback = slot switch
        {
            "accent" => "#28D7B7",
            "bg" => "#090B0F",
            "panel" => "#101419",
            "card" => "#151B22",
            "text" => "#F3F5FA",
            _ => "#FFFFFF"
        };

        var initial = Customization.TryParse(
            string.IsNullOrWhiteSpace(current) ? fallback : current, out var ic) ? ic : Colors.White;

        var owner = Window.GetWindow(this);
        var dlg = new ColorPickerDialog(initial);
        if (owner != null && owner.IsLoaded)
            dlg.Owner = owner;

        if (dlg.ShowDialog() != true) return;

        var hex = Customization.ToHex(dlg.SelectedColor);
        switch (slot)
        {
            case "accent": _settings.AccentColor = hex; break;
            case "bg": _settings.BgColor = hex; break;
            case "panel": _settings.PanelColor = hex; break;
            case "card": _settings.CardColor = hex; break;
            case "text": _settings.TextColor = hex; break;
        }
        _settings.Save();
        Customization.Apply(_settings);
        RefreshChips();
    }

    private void BtnResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _settings.AccentColor = "";
        _settings.BgColor = "";
        _settings.PanelColor = "";
        _settings.CardColor = "";
        _settings.TextColor = "";
        _settings.BackgroundImagePath = "";
        _settings.BackgroundDim = 55;
        _settings.BackgroundBlur = 0;
        _settings.Save();
        Customization.ResetColors();
        DimSlider.Value = _settings.BackgroundDim;
        BlurSlider.Value = _settings.BackgroundBlur;
        RefreshChips();
        UpdateDimLabel();
        UpdateBlurLabel();
    }

    // ---------------- Background image ----------------

    private void BtnPickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("t_custom_image_pick"),
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        _settings.BackgroundImagePath = dlg.FileName;
        _settings.Save();
        Customization.Apply(_settings);
    }

    private void BtnClearImage_Click(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundImagePath = "";
        _settings.Save();
        Customization.Apply(_settings);
    }

    private void DimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initDone) return;
        _settings.BackgroundDim = DimSlider.Value;
        _settings.Save();
        Customization.Apply(_settings);
        UpdateDimLabel();
    }

    private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initDone) return;
        _settings.BackgroundBlur = BlurSlider.Value;
        _settings.Save();
        Customization.Apply(_settings);
        UpdateBlurLabel();
    }

    private void UpdateDimLabel()
        => DimLabel.Text = Loc.T("t_custom_dim", _settings.BackgroundDim);

    private void UpdateBlurLabel()
        => BlurLabel.Text = Loc.T("t_custom_blur", _settings.BackgroundBlur);

    private void RefreshLanguageItems()
    {
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            item.Content = item.Tag as string == "ru"
                ? Loc.T("t_settings_lang_ru")
                : Loc.T("t_settings_lang_en");
        }
        VersionText.Text = Loc.T("t_settings_version",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0");
        UpdateDimLabel();
        UpdateBlurLabel();
        foreach (var child in PresetRow.Children.OfType<Border>())
        {
            if (child.Tag is string hex)
            {
                var preset = Presets.FirstOrDefault(p => p.Hex == hex);
                if (preset.Key != null) child.ToolTip = Loc.T(preset.Key);
            }
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initDone) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            Loc.SetLanguage(code);
        }
    }

    private void ChkRememberDevice_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initDone) return;
        _settings.RememberLastDevice = ChkRememberDevice.IsChecked == true;
        _settings.Save();
    }

    private void ChkRememberConfig_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initDone) return;
        _settings.RememberLastConfig = ChkRememberConfig.IsChecked == true;
        _settings.Save();
    }
}