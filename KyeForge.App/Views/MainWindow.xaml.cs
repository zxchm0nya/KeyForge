using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KyeForge.App.Services;
using KyeForge.App.ViewModels;

namespace KyeForge.App.Views
{
    public partial class MainWindow : Window
    {
        private readonly AppState _state = StateHub.State;
        private readonly AppSettings _settings = AppSettings.Load();
        private IntPtr _hookId;
        private NativeMethods.LowLevelKeyboardProc? _proc;
        private IntPtr _hwnd;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                    new Uri("pack://application:,,,/Assets/app.ico"));
            }
            catch { }
            Loaded += (_, _) => InitializeNav();
        }

        // ---------------- Setup / nav ----------------

        private void InitializeNav()
        {
            var navs = new[] { NavDevices, NavKeymap, NavLighting, NavTest, NavSettings };
            foreach (var n in navs)
                n.NavClicked += OnNavClicked;

            ShowPage("devices");
            _state.PropertyChanged += OnStatePropertyChanged;
            Loc.LanguageChanged += OnLanguageChanged;
            Customization.Changed += ApplyCustomBackground;

            ApplyCustomBackground();
            PageDevices.SetFromSettings(_settings);
            if (_settings.RememberLastDevice && !string.IsNullOrEmpty(_settings.LastDeviceName))
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                StatusText.SetResourceReference(TextBlock.TextProperty, "t_status_last_device");
                DeviceNameFooter.Text = _settings.LastDeviceName;
                StopStatusPulse();
            }
            else
            {
                StartStatusPulse();
            }
            UpdateConfigBadge();
            InstallKeyboardHook();
            PlayWindowEntrance();
        }

        private void OnNavClicked(object sender, MouseButtonEventArgs e)
        {
            var tag = ((NavButton)sender).Tag as string;
            ShowPage(tag ?? "devices");
        }

        private void ShowPage(string page)
        {
            NavDevices.IsSelected = page == "devices";
            NavKeymap.IsSelected = page == "keymap";
            NavLighting.IsSelected = page == "lighting";
            NavTest.IsSelected = page == "keytest";
            NavSettings.IsSelected = page == "settings";

            var pages = new (string Id, FrameworkElement El)[]
            {
                ("devices", PageDevices),
                ("keymap", PageKeymap),
                ("lighting", PageLighting),
                ("keytest", PageKeyTest),
                ("settings", PageSettings),
            };

            foreach (var (id, el) in pages)
            {
                bool show = id == page;
                if (show && el.Visibility != Visibility.Visible)
                {
                    el.Visibility = Visibility.Visible;
                    el.Opacity = 0;
                    el.RenderTransform = new TranslateTransform(0, 12);
                    AnimatePageIn(el);
                }
                else if (!show && el.Visibility != Visibility.Collapsed)
                {
                    el.Visibility = Visibility.Collapsed;
                }
            }

            PageTitle.SetResourceReference(TextBlock.TextProperty, page switch
            {
                "devices" => "t_devices_title",
                "keymap" => "t_keymap_title",
                "lighting" => "t_light_title",
                "keytest" => "t_keytest_title",
                "settings" => "t_settings_title",
                _ => "t_devices_title"
            });
        }

        private static void AnimatePageIn(FrameworkElement page)
        {
            var story = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            story.Children.Add(fade);

            var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            story.Children.Add(slide);

            story.Completed += (_, _) => page.Opacity = 1;
            story.Begin(page);
        }

        private void PlayWindowEntrance()
        {
            RootGrid.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => RootGrid.Opacity = 1;
            RootGrid.BeginAnimation(OpacityProperty, fade);
        }

        // ---------------- Status ----------------

        private void OnStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.DeviceStatus))
            {
                if (_state.IsConnected)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    StatusText.SetResourceReference(TextBlock.TextProperty, "t_status_connected");
                    DeviceNameFooter.Text = _state.SelectedDevice?.Name ?? "";
                    StopStatusPulse();
                    if (_settings.RememberLastDevice)
                    {
                        _settings.LastDevicePath = _state.SelectedDevice?.Path ?? "";
                        _settings.LastDeviceName = _state.SelectedDevice?.Name ?? "";
                        _settings.Save();
                    }
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(107, 118, 144));
                    StatusText.SetResourceReference(TextBlock.TextProperty, "t_status_disconnected");
                    StartStatusPulse();
                }
            }
            else if (e.PropertyName == nameof(AppState.ConfigName))
            {
                UpdateConfigBadge();
                if (_settings.RememberLastConfig)
                {
                    _settings.LastConfigPath = _state.ConfigPath;
                    _settings.LastConfigName = _state.ConfigName;
                    _settings.Save();
                }
            }
        }

        private void OnLanguageChanged()
        {
            UpdateConfigBadge();
            if (DeviceNameFooter.Text.Length > 0 && _state.SelectedDevice == null)
                DeviceNameFooter.Text = _settings.LastDeviceName;
        }

        // ---------------- Custom background ----------------

        private void ApplyCustomBackground()
        {
            try
            {
                var path = Customization.BackgroundPath;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage();
                    img.BeginInit();
                    img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(path);
                    img.EndInit();
                    img.Freeze();

                    BgImageBrush.ImageSource = img;
                    BgImageHost.Visibility = Visibility.Visible;
                    BgDimHost.Opacity = Customization.BackgroundDim / 100.0;
                    BgImageHost.Effect = Customization.BackgroundBlur > 0
                        ? new System.Windows.Media.Effects.BlurEffect { Radius = Customization.BackgroundBlur }
                        : null;

                    SidebarHost.SetResourceReference(Border.BackgroundProperty, "BgPanelBrush");
                    ContentHost.SetResourceReference(Grid.BackgroundProperty, "BgDeepBrush");
                }
                else
                {
                    BgImageHost.Visibility = Visibility.Collapsed;
                    BgDimHost.Opacity = 0;
                    BgImageHost.Effect = null;
                    ContentHost.SetResourceReference(Grid.BackgroundProperty, "BgDeepBrush");
                    SidebarHost.SetResourceReference(Border.BackgroundProperty, "BgPanelBrush");
                }
            }
            catch
            {
                BgImageHost.Visibility = Visibility.Collapsed;
                BgDimHost.Opacity = 0;
            }
        }

        private void UpdateConfigBadge()
        {
            if (_state.ConfigLoaded)
            {
                ConfigBadge.Visibility = Visibility.Visible;
                ConfigBadgeText.Text = Loc.T("t_config_badge", _state.ConfigName);
            }
            else
            {
                ConfigBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void StartStatusPulse()
        {
            StatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }

        private void StopStatusPulse()
        {
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;
        }

        // ---------------- Window chrome ----------------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            // Windows 11: force square corners
            try
            {
                var pref = 1; // DWMWCP_DONOTROUND
                DwmSetWindowAttribute(_hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
            }
            catch { }

            // Dark title bar
            try
            {
                var dark = 1;
                DwmSetWindowAttribute(_hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
                DwmSetWindowAttribute(_hwnd, 19 /* older builds */, ref dark, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(this,
                $"KeyForge v{(ver != null ? ver.ToString(3) : "1.0")}\n\n{Loc.T("t_settings_about_text")}",
                Loc.T("t_about_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // F10 opens About - except on the key test page where F10 is a testable key
            PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.F10 && PageKeyTest.Visibility != Visibility.Visible)
                    BtnAbout_Click(this, new RoutedEventArgs());
            };
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private void InstallKeyboardHook()
        {
            _proc = HookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookId = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL, _proc,
                    GetModuleHandle(curModule?.ModuleName ?? ""), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && PageKeyTest.Visibility == Visibility.Visible)
            {
                if (wParam.ToInt64() == NativeMethods.WM_KEYDOWN || wParam.ToInt64() == NativeMethods.WM_SYSKEYDOWN ||
                    wParam.ToInt64() == NativeMethods.WM_KEYUP || wParam.ToInt64() == NativeMethods.WM_SYSKEYUP)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var usage = VkToUsb(info.vkCode);
                    var isDown = wParam.ToInt64() == NativeMethods.WM_KEYDOWN || wParam.ToInt64() == NativeMethods.WM_SYSKEYDOWN;
                    Application.Current.Dispatcher.BeginInvoke(() => PageKeyTest.ReportKey(usage, isDown));
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static byte VkToUsb(int vk)
        {
            if (vk >= 'A' && vk <= 'Z') return (byte)(0x04 + (vk - 'A'));
            if (vk >= '1' && vk <= '9') return (byte)(0x1D + (vk - '0'));
            if (vk == '0') return 0x27;
            return vk switch
            {
                // Modifiers - low-level hook reports distinct L/R virtual keys
                0xA0 => 0xE1, 0xA1 => 0xE5,                 // L/R Shift
                0xA2 => 0xE0, 0xB3 => 0xE4,                 // L/R Ctrl
                0xA4 => 0xE2, 0xB5 => 0xE6,                 // L/R Alt
                0x5B => 0xE3, 0x5C => 0xE7,                 // L/R Win
                0x10 => 0xE1, 0x11 => 0xE0, 0x12 => 0xE2,   // generic Shift/Ctrl/Alt fallback

                // Control keys
                0x20 => 0x2C, 0x0D => 0x28, 0x08 => 0x2A, 0x09 => 0x2B,
                0x1B => 0x29, 0x14 => 0x39, 0x90 => 0x53, 0x91 => 0x47,
                0x13 => 0x48, 0x2C => 0x46, 0x5D => 0x65,

                // Navigation cluster
                0x24 => 0x4A, 0x25 => 0x50, 0x26 => 0x52, 0x27 => 0x4F, 0x28 => 0x51,
                0x21 => 0x4B, 0x22 => 0x4E, 0x23 => 0x4D, 0x2D => 0x49, 0x2E => 0x4C,

                // OEM punctuation
                0xBA => 0x33, 0xBB => 0x2E, 0xBC => 0x36, 0xBD => 0x2D, 0xBE => 0x37,
                0xBF => 0x38, 0xC0 => 0x35, 0xDB => 0x2F, 0xDC => 0x31, 0xDD => 0x30,
                0xDE => 0x34, 0xE2 => 0x31,

                // Numpad
                0x60 => 0x62, 0x61 => 0x59, 0x62 => 0x5A, 0x63 => 0x5B, 0x64 => 0x5C,
                0x65 => 0x5D, 0x66 => 0x5E, 0x67 => 0x5F, 0x68 => 0x60, 0x69 => 0x61,
                0x6A => 0x55, 0x6B => 0x57, 0x6C => 0x63, 0x6D => 0x56, 0x6F => 0x54,

                // Media
                0xAD => 0x80, 0xAE => 0x82, 0xAF => 0x81,

                // F1-F12, F13-F24
                >= 0x70 and <= 0x7B => (byte)(0x3A + vk - 0x70),
                >= 0x7C and <= 0x87 => (byte)(0xD4 + vk - 0x7C),
                _ => 0x00
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hookId != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hookId);
            _state.SelectedDevice?.Dispose();
            base.OnClosed(e);
        }
    }

    internal static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }
}
