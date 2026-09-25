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
        Loc.LanguageChanged += RefreshUpdateUi;
        Customization.Changed += () => UpdateNavPositionSelection(_settings.SidebarPosition);
        UpdateChecker.Checked += OnUpdateChecked;
        UpdateNavPositionSelection(_settings.SidebarPosition);
        RefreshUpdateUi();
        if (UpdateChecker.HasChecked)
            OnUpdateChecked(UpdateChecker.LastResult);

        // Theme radios
        if (_settings.Theme == "light") RadioThemeLight.IsChecked = true;
        else RadioThemeDark.IsChecked = true;

        // Profiles + plugins
        AppProfileWatcher.ProfileChanged += OnProfileChanged;
        RebuildProfileList();
        PluginStatusText.Text = KyeForge.App.Plugins.PluginHost.Plugins.Count == 0
            ? Loc.T("t_settings_plugins_none")
            : Loc.T("t_settings_plugins_count", KyeForge.App.Plugins.PluginHost.Plugins.Count);

        _initDone = true;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initDone) return;
        var theme = RadioThemeLight.IsChecked == true ? "light" : "dark";
        if (_settings.Theme == theme) return;
        _settings.Theme = theme;
        _settings.Save();
        Customization.SetTheme(theme);
    }

    private void BtnExportLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var zip = AppLog.ExportArchive();
            if (string.IsNullOrEmpty(zip))
            {
                ExportLogStatus.Text = Loc.T("t_settings_export_log_fail");
                ExportLogStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }
            else
            {
                ExportLogStatus.Text = Loc.T("t_settings_export_log_ok", zip);
                ExportLogStatus.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                // Open Explorer with the zip selected so the user can drag it into GitHub.
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\"");
            }
            ExportLogStatus.Visibility = Visibility.Visible;
        }
        catch
        {
            ExportLogStatus.Text = Loc.T("t_settings_export_log_fail");
            ExportLogStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            ExportLogStatus.Visibility = Visibility.Visible;
        }
    }

    private void OnProfileChanged(AppProfile? profile)
    {
        if (ProfileActiveText == null) return;
        if (profile == null)
        {
            ProfileActiveText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ProfileActiveText.Text = Loc.T("t_settings_profiles_current", profile.Name);
            ProfileActiveText.Visibility = Visibility.Visible;
        }
    }

    private void RebuildProfileList()
    {
        ProfileList.Children.Clear();
        var profiles = AppProfileWatcher.Profiles.OrderBy(p => p.ProcessName).ToList();
        ProfileEmptyText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var p in profiles)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = $"{p.ProcessName} — {p.Name}",
                Style = (Style)FindResource("SubText"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var removeBtn = new Button
            {
                Content = Loc.T("t_settings_profiles_remove"),
                Style = (Style)FindResource("SecondaryButton"),
                Height = 30,
                Padding = new Thickness(10, 4, 10, 4),
                Tag = p.ProcessName
            };
            removeBtn.Click += (_, _) =>
            {
                if (removeBtn.Tag is string proc)
                {
                    AppProfileWatcher.Remove(proc);
                    RebuildProfileList();
                }
            };
            Grid.SetColumn(removeBtn, 1);
            row.Children.Add(removeBtn);

            ProfileList.Children.Add(row);
        }
    }

    private void BtnAddProfile_Click(object sender, RoutedEventArgs e)
    {
        // List running user processes (exclude system/pids we can't open).
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (proc.Id == Environment.ProcessId) continue;
                var name = proc.ProcessName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Idle", StringComparison.OrdinalIgnoreCase)) continue;
                running.Add(name);
            }
            catch { }
            finally { proc.Dispose(); }
        }

        // Remove ones already mapped.
        foreach (var existing in AppProfileWatcher.Profiles)
            running.Remove(existing.ProcessName);

        if (running.Count == 0) return;

        var win = Window.GetWindow(this);
        var dlg = new ProfilePickerDialog(running.OrderBy(n => n).ToList())
        {
            Owner = win is { IsLoaded: true } ? win : null
        };
        if (dlg.ShowDialog() == true && dlg.SelectedProcess != null)
        {
            // Capture current lighting as the profile's starting point.
            var s = AppSettings.Load();
            AppProfileWatcher.AddOrUpdate(new AppProfile
            {
                ProcessName = dlg.SelectedProcess,
                Name = dlg.ProfileName,
                LightingEffect = s.LightingEffect,
                LightingBrightness = s.LightingBrightness,
                LightingSpeed = s.LightingSpeed,
                LightingColor = s.LightingColor
            });
            RebuildProfileList();
        }
    }

    private void BtnOpenPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = KyeForge.App.Plugins.PluginHost.PluginsDirectory;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch { }
    }

    private void RefreshUpdateUi()
    {
        if (UpdateCurrentText == null) return;
        UpdateCurrentText.Text = Loc.T("t_updates_current", UpdateChecker.LocalVersion);
        if (!UpdateChecker.HasChecked)
        {
            UpdateStatusText.Text = "";
            UpdateNotesHeader.Visibility = Visibility.Collapsed;
            UpdateNotesCard.Visibility = Visibility.Collapsed;
            BtnOpenRelease.Visibility = Visibility.Collapsed;
            return;
        }
        ApplyUpdateResult(UpdateChecker.LastResult, error: UpdateChecker.LastResult is null);
    }

    private void OnUpdateChecked(UpdateInfo? info)
    {
        if (!IsLoaded && UpdateStatusText == null) return;
        ApplyUpdateResult(info, error: info is null && UpdateChecker.HasChecked);
    }

    private void ApplyUpdateResult(UpdateInfo? info, bool error)
    {
        if (UpdateStatusText == null) return;

        if (error)
        {
            UpdateStatusText.SetResourceReference(TextBlock.TextProperty, "t_updates_error");
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            UpdateNotesHeader.Visibility = Visibility.Collapsed;
            UpdateNotesCard.Visibility = Visibility.Collapsed;
            BtnOpenRelease.Visibility = Visibility.Collapsed;
            return;
        }

        if (info == null)
        {
            UpdateStatusText.Text = "";
            return;
        }

        if (info.IsNewer)
        {
            UpdateStatusText.Text = Loc.T("t_updates_available", info.Version);
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var notes = UpdateChecker.PlainNotes(info.Body);
            if (string.IsNullOrWhiteSpace(notes))
            {
                UpdateNotesHeader.Visibility = Visibility.Collapsed;
                UpdateNotesCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateNotesHeader.Visibility = Visibility.Visible;
                UpdateNotesCard.Visibility = Visibility.Visible;
                UpdateNotesText.Text = notes;
            }
            BtnOpenRelease.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusText.Text = Loc.T("t_updates_none_named", UpdateChecker.LocalVersion);
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            UpdateNotesHeader.Visibility = Visibility.Collapsed;
            UpdateNotesCard.Visibility = Visibility.Collapsed;
            // Still allow opening the latest release page.
            BtnOpenRelease.Visibility = Visibility.Visible;
        }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        UpdateStatusText.SetResourceReference(TextBlock.TextProperty, "t_updates_checking");
        UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        UpdateNotesHeader.Visibility = Visibility.Collapsed;
        UpdateNotesCard.Visibility = Visibility.Collapsed;
        BtnOpenRelease.Visibility = Visibility.Collapsed;

        try
        {
            var info = await UpdateChecker.CheckAsync();
            UpdateChecker.Publish(info);
            ApplyUpdateResult(info, error: info is null);
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
        => UpdateChecker.OpenRelease(UpdateChecker.LastResult);

    private void UpdateNavPositionSelection(string currentPos)
    {
        var cards = new[] { CardNavLeft, CardNavTop, CardNavRight, CardNavBottom };
        foreach (var card in cards)
        {
            if (card == null) continue;
            bool isSelected = string.Equals((string)card.Tag, currentPos, StringComparison.OrdinalIgnoreCase);
            if (isSelected)
            {
                card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                card.SetResourceReference(Border.BackgroundProperty, "BgElevatedBrush");
            }
            else
            {
                card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                card.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
            }
        }
    }

    private void NavPosCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string pos)
        {
            _settings.SidebarPosition = pos;
            _settings.Save();
            UpdateNavPositionSelection(pos);

            var win = Window.GetWindow(this) as MainWindow;
            win?.ApplySidebarPosition(pos);
        }
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

    private void BtnResetBlocks_Click(object sender, RoutedEventArgs e) => BlockLayout.ResetAll();

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
            Filter = "Images & GIF (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Videos (*.mp4;*.avi;*.mov;*.wmv;*.mkv;*.webm;*.m4v)|*.mp4;*.avi;*.mov;*.wmv;*.mkv;*.webm;*.m4v|All files (*.*)|*.*",
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