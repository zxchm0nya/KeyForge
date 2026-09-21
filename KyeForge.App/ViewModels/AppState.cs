using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KyeForge.App.Hid;
using KyeForge.App.Models;

namespace KyeForge.App.ViewModels;

/// <summary>Central observable state shared by all views.</summary>
public class AppState : INotifyPropertyChanged
{
    public ObservableCollection<ConnectedKeyboard> Devices { get; } = new();

    private ConnectedKeyboard? _selectedDevice;
    public ConnectedKeyboard? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConnected)); }
    }

    private ViaClient? _client;
    public ViaClient? Client
    {
        get => _client;
        set { _client = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConnected)); }
    }

    public bool IsConnected => Client != null;

    private string _deviceStatus = "No keyboard connected";
    public string DeviceStatus
    {
        get => _deviceStatus;
        set { _deviceStatus = value; OnPropertyChanged(); }
    }

    private VialKeyboardDefinition? _definition;
    public VialKeyboardDefinition? Definition
    {
        get => _definition;
        set { _definition = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConfigLoaded)); }
    }

    private KeyboardLayout? _layout;
    public KeyboardLayout? Layout
    {
        get => _layout;
        set { _layout = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConfigLoaded)); }
    }

    public bool ConfigLoaded => Definition != null && Layout != null;

    private string _configPath = "";
    public string ConfigPath
    {
        get => _configPath;
        set { _configPath = value; OnPropertyChanged(); }
    }

    private string _configName = "";
    public string ConfigName
    {
        get => _configName;
        set { _configName = value; OnPropertyChanged(); }
    }

    // Keymap data (matrix[row,col] -> keycode) for the loaded layout's keys
    public Dictionary<(int, int), ushort> Keycodes { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}