using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KyeForge.App.Services;
using KyeForge.App.ViewModels;

namespace KyeForge.App.Views;

/// <summary>Compact tray flyout: status + quick actions without opening the main window.</summary>
public partial class TrayFlyout : Window
{
    private readonly AppState _state = StateHub.State;
    private bool _refreshing;
    private bool _applying;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    /// <summary>UTC time of the last click-away auto-hide (toggle race guard).</summary>
    public DateTime LastAutoHide { get; private set; } = DateTime.MinValue;

    public TrayFlyout()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            LastAutoHide = DateTime.UtcNow;
            Hide();
        };
    }

    /// <summary>Shows the flyout above the anchor (tray icon position, DIPs).</summary>
    public void RefreshAndShow(Point anchorDip, Rect workAreaDip)
    {
        Refresh();
        Opacity = 0;
        Show();
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                double x = anchorDip.X - ActualWidth / 2;
                double y = anchorDip.Y - ActualHeight - 10;
                x = Math.Max(workAreaDip.Left + 4, Math.Min(x, workAreaDip.Right - ActualWidth - 4));
                if (y < workAreaDip.Top + 4)
                    y = anchorDip.Y + 10; // taskbar docked on top: drop below the icon
                y = Math.Max(workAreaDip.Top + 4, Math.Min(y, workAreaDip.Bottom - ActualHeight - 4));
                Left = x;
                Top = y;
                Opacity = 1;
                Activate();
            }
            catch { Opacity = 1; }
        }, DispatcherPriority.Loaded);
    }

    private void Refresh()
    {
        // Status.
        var device = _state.SelectedDevice;
        if (_state.IsConnected && device != null)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            StatusText.Text = device.Name;
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x76, 0x90));
            StatusText.Text = Loc.T("t_tray_nodevice");
        }

        // Profiles (user-defined only).
        _refreshing = true;
        try
        {
            var profiles = AppProfileWatcher.Profiles.OrderBy(p => p.Name).ToList();
            ProfileBox.ItemsSource = profiles;
            ProfileBox.Visibility = profiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var cur = AppProfileWatcher.Current;
            ProfileBox.SelectedItem = cur == null ? null :
                profiles.FirstOrDefault(p =>
                    string.Equals(p.ProcessName, cur.ProcessName, StringComparison.OrdinalIgnoreCase));
            ProfileBox.IsEnabled = _state.IsConnected;
        }
        finally { _refreshing = false; }

        // Backlight (tentative label, then a live read).
        LightButton.Content = Loc.T("t_tray_light_off");
        LightButton.IsEnabled = _state.IsConnected;
        _ = RefreshLightAsync();

        // Errors.
        try
        {
            int n = AppLog.CountErrors();
            ErrorsButton.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (n > 0) ErrorsButton.Content = Loc.T("t_tray_errors", n);
        }
        catch { ErrorsButton.Visibility = Visibility.Collapsed; }
    }

    private async Task RefreshLightAsync()
    {
        try
        {
            var on = await LightingControl.GetBacklightOnAsync(_state);
            if (on == null) { LightButton.IsEnabled = false; return; }
            LightButton.IsEnabled = _state.IsConnected;
            LightButton.Content = Loc.T(on.Value ? "t_tray_light_on" : "t_tray_light_off");
        }
        catch { }
    }

    private async void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _applying) return;
        if (ProfileBox.SelectedItem is not AppProfile p) return;
        _applying = true;
        try
        {
            AppProfileWatcher.Activate(p);
            await LightingControl.ApplyProfileAsync(_state, p);
        }
        catch { }
        finally { _applying = false; }
    }

    private async void LightButton_Click(object sender, RoutedEventArgs e)
    {
        LightButton.IsEnabled = false;
        try
        {
            var on = await LightingControl.ToggleBacklightAsync(_state);
            if (on != null)
                LightButton.Content = Loc.T(on.Value ? "t_tray_light_on" : "t_tray_light_off");
        }
        catch { }
        finally { LightButton.IsEnabled = _state.IsConnected; }
    }

    private void ErrorsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var zip = AppLog.ExportArchive();
            if (!string.IsNullOrEmpty(zip))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\"");
        }
        catch { }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
