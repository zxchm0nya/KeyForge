using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KyeForge.App.Hid;
using KyeForge.App.Services;
using Microsoft.Win32;

namespace KyeForge.App.Views;

public partial class DevicesView : UserControl
{
    private readonly AppState _state = StateHub.State;

    public class DeviceItem
    {
        public string Name { get; set; } = "";
        public string Meta { get; set; } = "";
        public ConnectedKeyboard? Keyboard { get; set; }
        public ICommand? Select { get; set; }
        public bool IsSelected { get; set; }
    }

    public DevicesView()
    {
        InitializeComponent();
        _state.Devices.CollectionChanged += (_, _) => RefreshList();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.SelectedDevice))
                Application.Current.Dispatcher.Invoke(RefreshList);
        };
        RefreshList();
    }

    private void RefreshList()
    {
        var items = new ObservableCollection<DeviceItem>();
        foreach (var kb in _state.Devices)
        {
            items.Add(new DeviceItem
            {
                Name = kb.Name,
                Meta = $"VID {kb.VendorId:X4} / PID {kb.ProductId:X4} / HID: {kb.InterfaceCount}",
                Keyboard = kb,
                IsSelected = _state.SelectedDevice == kb,
                Select = new RelayCommand(() => _ = SelectKeyboard(kb))
            });
        }

        DeviceList.ItemsSource = items;
        NoDevices.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotice(string message)
    {
        DeviceNoticeText.Text = message;
        DeviceNotice.Visibility = Visibility.Visible;
    }

    private void HideNotice()
    {
        DeviceNotice.Visibility = Visibility.Collapsed;
        DeviceNoticeText.Text = "";
    }

    private async Task SelectKeyboard(ConnectedKeyboard kb)
    {
        _state.SelectedDevice?.Close();
        _state.Client?.Dispose();
        _state.Client = null;
        _state.SelectedDevice = null;

        // Release every other enumerated interface so orphaned reader loops
        // cannot swallow the handshake reply meant for this connection.
        foreach (var other in _state.Devices)
        {
            if (!ReferenceEquals(other, kb))
            {
                try { other.Close(); } catch { }
            }
        }

        _state.DeviceStatus = Loc.T("t_stat_connecting", kb.Name);
        HideNotice();
        RefreshList();

        ViaClient? connectedClient = null;
        double connectedVersion = 0;
        var openedAny = false;

        // Two passes: some boards ignore the first handshake burst right after
        // enumeration or while another handle is being released.
        for (var pass = 0; pass < 2 && connectedClient == null; pass++)
        {
            if (pass > 0) await Task.Delay(450);

            foreach (var candidate in kb.Candidates)
            {
                var opened = await Task.Run(async () =>
                {
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        if (kb.Open(candidate)) return true;
                        await Task.Delay(200 + attempt * 250);
                    }

                    return false;
                });

                if (!opened) continue;
                openedAny = true;

                // Give the firmware a moment after the stream opens before probing it.
                await Task.Delay(pass == 0 ? 150 : 300);

                var client = new ViaClient(kb);
                double ver = 0;
                for (var probe = 0; probe < 2 && ver <= 0; probe++)
                {
                    ver = await client.GetProtocolVersionAsync();
                    if (ver <= 0) await Task.Delay(180);
                }

                // Vial-only firmware does not answer the VIA version query - try its handshake.
                if (ver <= 0)
                    ver = await client.ProbeVialAsync() ? 1 : 0;

                if (ver > 0)
                {
                    connectedClient = client;
                    connectedVersion = ver;
                    break;
                }

                client.Dispose();
                kb.Close();
            }
        }

        if (connectedClient == null)
        {
            _state.DeviceStatus = openedAny
                ? Loc.T("t_stat_failed_via", kb.Name)
                : Loc.T("t_stat_failed_open", kb.Name);
            ShowNotice(_state.DeviceStatus);
            RefreshList();
            return;
        }

        _state.SelectedDevice = kb;
        _state.Client = connectedClient;
        _state.DeviceStatus = connectedClient.IsVial
            ? Loc.T("t_stat_connected_vial", kb.Name)
            : Loc.T("t_stat_connected_via", kb.Name, connectedVersion);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        try
        {
            var devices = await RefreshDevicesAsync(preferDefinition: true);
            if (devices.Count == 0)
            {
                _state.DeviceStatus = Loc.T("t_msg_no_via");
                ShowNotice(Loc.T("t_msg_no_via_long"));
            }
            else
            {
                HideNotice();
            }
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private async Task<IReadOnlyList<ConnectedKeyboard>> RefreshDevicesAsync(bool preferDefinition)
    {
        var vid = preferDefinition && _state.Definition?.VendorIdValue > 0
            ? _state.Definition.VendorIdValue
            : (ushort?)null;
        var pid = preferDefinition && _state.Definition?.ProductIdValue > 0
            ? _state.Definition.ProductIdValue
            : (ushort?)null;
        var devices = await Task.Run(() => HidDeviceManager.ListViaDevices(vid, pid));

        // Dispose old handles BEFORE re-enumerating: orphaned reader loops from
        // previous scans steal incoming HID reports from the live connection.
        if (_state.SelectedDevice != null || _state.Client != null)
        {
            _state.Client?.Dispose();
            _state.Client = null;
            _state.SelectedDevice = null;
        }
        foreach (var old in _state.Devices)
        {
            try { old.Dispose(); } catch { }
        }
        _state.Devices.Clear();
        foreach (var device in devices)
            _state.Devices.Add(device);

        return devices;
    }

    private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("t_dlg_load_title"),
            Filter = "VIA JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
            LoadAndApply(dlg.FileName);
    }

    public void LoadAndApply(string path)
    {
        var (def, layout, error) = ViaConfigParser.Load(path);
        if (def == null || layout == null)
        {
            var reason = Loc.T("t_msg_parse_failed");
            if (!string.IsNullOrEmpty(error))
                reason += "\n\n" + Loc.T("t_msg_parse_detail", error);
            MessageBox.Show(reason, "KeyForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _state.Definition = def;
        _state.Layout = layout;
        _state.ConfigPath = path;
        _state.ConfigName = System.IO.Path.GetFileNameWithoutExtension(path);

        var customs = new List<KeyValuePair<byte, string>>();
        for (var i = 0; i < def.CustomKeycodes.Count && i < 16; i++)
            customs.Add(new KeyValuePair<byte, string>((byte)(0xF0 + i),
                def.CustomKeycodes[i].ShortName ?? def.CustomKeycodes[i].Name));
        KeycodeMap.RegisterCustom(customs);

        ConfigInfo.Visibility = Visibility.Visible;
        ConfigName.Text = def.Name;
        ConfigPath.Text = path;
        ConfigVidPid.Text = $"VID {def.VendorId} / PID {def.ProductId} / Matrix {layout.MaxX}x{layout.MaxY}";

        _state.Keycodes.Clear();
        foreach (var key in layout.Keys)
        {
            if (key.MatrixRow >= 0 && key.MatrixCol >= 0)
                _state.Keycodes[(key.MatrixRow, key.MatrixCol)] = key.Keycode;
        }

        PersistSettings();
        _ = AutoConnectFromDefinitionAsync();
    }

    private async Task AutoConnectFromDefinitionAsync()
    {
        var def = _state.Definition;
        if (def == null || def.VendorIdValue == 0 || def.ProductIdValue == 0) return;

        BtnRefresh.IsEnabled = false;
        try
        {
            var devices = await RefreshDevicesAsync(preferDefinition: true);
            var exact = _state.Devices.FirstOrDefault(d =>
                d.VendorId == def.VendorIdValue && d.ProductId == def.ProductIdValue);

            if (exact != null)
            {
                _state.DeviceStatus = Loc.T("t_stat_found_config_device", exact.Name);
                await SelectKeyboard(exact);
            }
            else if (devices.Count == 0)
            {
                _state.DeviceStatus = Loc.T("t_stat_config_loaded_no_device");
            }
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private void PersistSettings()
    {
        var settings = AppSettings.Load();
        if (settings.RememberLastConfig)
        {
            settings.LastConfigPath = _state.ConfigPath;
            settings.LastConfigName = _state.ConfigName;
        }
        if (settings.RememberLastDevice && _state.SelectedDevice != null)
        {
            settings.LastDevicePath = _state.SelectedDevice.Path;
            settings.LastDeviceName = _state.SelectedDevice.Name;
        }
        settings.Save();
    }

    public void SetFromSettings(AppSettings settings)
    {
        if (settings.RememberLastConfig && !string.IsNullOrEmpty(settings.LastConfigPath) &&
            System.IO.File.Exists(settings.LastConfigPath))
            LoadAndApply(settings.LastConfigPath);
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
    public void Execute(object? parameter) => _execute();
}
