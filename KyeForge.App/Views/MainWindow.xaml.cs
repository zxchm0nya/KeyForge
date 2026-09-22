using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

        // Animated background (GIF frames / video), paused when window is inactive.
        // GIF frames are pre-scaled once at load so per-frame cost stays tiny.
        private List<BitmapSource> _gifFrames = new();
        private List<TimeSpan> _gifDelays = new();
        private int _gifIndex;
        private DispatcherTimer? _gifTimer;
        private bool _videoActive;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                    new Uri("pack://application:,,,/KyeForge;component/Assets/app.ico"));
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

            ApplySidebarPosition(_settings.SidebarPosition, save: false);
            ShowPage("devices");
            _state.PropertyChanged += OnStatePropertyChanged;
            Loc.LanguageChanged += OnLanguageChanged;
            Customization.Changed += ApplyCustomBackground;

            Activated += (_, _) => ResumeBackground();
            Deactivated += (_, _) => PauseBackground();
            BgVideoHost.MediaEnded += (_, _) =>
            {
                try
                {
                    BgVideoHost.Position = TimeSpan.Zero;
                    if (IsActive) BgVideoHost.Play();
                }
                catch { }
            };
            NavGrid.SizeChanged += (_, _) => MoveNavIndicator(false);

            ApplyCustomBackground();
            SmoothScroll.Attach(this);
            ScrollPerf.CacheScrollContents(this);
            DpiChanged += (_, _) => ScrollPerf.RefreshAll();
            Dispatcher.BeginInvoke(() => MoveNavIndicator(false), DispatcherPriority.Loaded);
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

            MoveNavIndicator(true);
        }

        public string CurrentSidebarPosition { get; private set; } = "Left";

        public void ApplySidebarPosition(string position, bool save = true)
        {
            position = position switch
            {
                "Top" => "Top",
                "Right" => "Right",
                "Bottom" => "Bottom",
                _ => "Left"
            };

            CurrentSidebarPosition = position;
            if (save)
            {
                _settings.SidebarPosition = position;
                _settings.Save();
            }

            bool isHorizontal = position is "Top" or "Bottom";

            // Reconfigure RootGrid
            RootGrid.ColumnDefinitions.Clear();
            RootGrid.RowDefinitions.Clear();

            if (position == "Left")
            {
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(SidebarHost, 0);
                Grid.SetRow(SidebarHost, 0);
                Grid.SetColumn(ContentHost, 1);
                Grid.SetRow(ContentHost, 0);

                SidebarHost.BorderThickness = new Thickness(0, 0, 1, 0);
                SidebarHost.ClearValue(WidthProperty);
                SidebarHost.ClearValue(HeightProperty);
            }
            else if (position == "Right")
            {
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

                Grid.SetColumn(ContentHost, 0);
                Grid.SetRow(ContentHost, 0);
                Grid.SetColumn(SidebarHost, 1);
                Grid.SetRow(SidebarHost, 0);

                SidebarHost.BorderThickness = new Thickness(1, 0, 0, 0);
                SidebarHost.ClearValue(WidthProperty);
                SidebarHost.ClearValue(HeightProperty);
            }
            else if (position == "Top")
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(SidebarHost, 0);
                Grid.SetRow(SidebarHost, 0);
                Grid.SetColumn(ContentHost, 0);
                Grid.SetRow(ContentHost, 1);

                SidebarHost.BorderThickness = new Thickness(0, 0, 0, 1);
                SidebarHost.ClearValue(WidthProperty);
                SidebarHost.ClearValue(HeightProperty);
            }
            else if (position == "Bottom")
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetColumn(ContentHost, 0);
                Grid.SetRow(ContentHost, 0);
                Grid.SetColumn(SidebarHost, 0);
                Grid.SetRow(SidebarHost, 1);

                SidebarHost.BorderThickness = new Thickness(0, 1, 0, 0);
                SidebarHost.ClearValue(WidthProperty);
                SidebarHost.ClearValue(HeightProperty);
            }

            // Ensure background elements span everything
            int colSpan = RootGrid.ColumnDefinitions.Count > 0 ? RootGrid.ColumnDefinitions.Count : 1;
            int rowSpan = RootGrid.RowDefinitions.Count > 0 ? RootGrid.RowDefinitions.Count : 1;
            var bgHosts = new FrameworkElement[] { BgImageHost, BgGifHost, BgVideoHost, BgDimHost };
            foreach (var bg in bgHosts)
            {
                Grid.SetColumn(bg, 0);
                Grid.SetRow(bg, 0);
                Grid.SetColumnSpan(bg, colSpan);
                Grid.SetRowSpan(bg, rowSpan);
            }

            // Internal Sidebar Layout
            if (isHorizontal)
            {
                SidebarInnerGrid.RowDefinitions.Clear();
                SidebarInnerGrid.ColumnDefinitions.Clear();
                SidebarInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                SidebarInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                SidebarInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Brand
                Grid.SetRow(SidebarBrand, 0);
                Grid.SetColumn(SidebarBrand, 0);
                SidebarBrand.Margin = new Thickness(20, 8, 16, 8);
                SidebarBrand.VerticalAlignment = VerticalAlignment.Center;
                BrandDivider.Visibility = Visibility.Collapsed;

                // Nav
                Grid.SetRow(NavGrid, 0);
                Grid.SetColumn(NavGrid, 1);
                NavGrid.Margin = new Thickness(8, 4, 8, 4);
                NavGrid.HorizontalAlignment = HorizontalAlignment.Center;
                NavGrid.VerticalAlignment = VerticalAlignment.Center;
                NavStack.Orientation = Orientation.Horizontal;

                NavIndicator.HorizontalAlignment = HorizontalAlignment.Left;
                NavIndicator.VerticalAlignment = VerticalAlignment.Center;
                NavIndicator.Height = 40;

                foreach (var child in NavStack.Children.OfType<NavButton>())
                    child.Margin = new Thickness(3, 0, 3, 0);

                // Footer
                Grid.SetRow(SidebarFooter, 0);
                Grid.SetColumn(SidebarFooter, 2);
                SidebarFooter.Margin = new Thickness(12, 6, 20, 6);
                SidebarFooter.Padding = new Thickness(12, 6, 12, 6);
                SidebarFooter.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                SidebarInnerGrid.RowDefinitions.Clear();
                SidebarInnerGrid.ColumnDefinitions.Clear();
                SidebarInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SidebarInnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                SidebarInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Brand
                Grid.SetRow(SidebarBrand, 0);
                Grid.SetColumn(SidebarBrand, 0);
                SidebarBrand.Margin = new Thickness(24, 24, 24, 20);
                SidebarBrand.VerticalAlignment = VerticalAlignment.Top;
                BrandDivider.Visibility = Visibility.Visible;

                // Nav
                Grid.SetRow(NavGrid, 1);
                Grid.SetColumn(NavGrid, 0);
                NavGrid.Margin = new Thickness(14, 0, 14, 0);
                NavGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                NavGrid.VerticalAlignment = VerticalAlignment.Top;
                NavStack.Orientation = Orientation.Vertical;

                NavIndicator.HorizontalAlignment = HorizontalAlignment.Stretch;
                NavIndicator.VerticalAlignment = VerticalAlignment.Top;
                NavIndicator.Height = 40;

                foreach (var child in NavStack.Children.OfType<NavButton>())
                    child.Margin = new Thickness(0, 2, 0, 2);

                // Footer
                Grid.SetRow(SidebarFooter, 2);
                Grid.SetColumn(SidebarFooter, 0);
                SidebarFooter.Margin = new Thickness(20, 0, 20, 20);
                SidebarFooter.Padding = new Thickness(14, 12, 14, 12);
                SidebarFooter.VerticalAlignment = VerticalAlignment.Bottom;
            }

            Dispatcher.BeginInvoke(() => MoveNavIndicator(false), DispatcherPriority.Render);
        }

        /// <summary>
        /// Glides the sidebar selection pill to the currently selected nav button
        /// instead of teleporting the highlight.
        /// </summary>
        private void MoveNavIndicator(bool animate)
        {
            try
            {
                NavButton? btn = null;
                if (NavDevices.IsSelected) btn = NavDevices;
                else if (NavKeymap.IsSelected) btn = NavKeymap;
                else if (NavLighting.IsSelected) btn = NavLighting;
                else if (NavTest.IsSelected) btn = NavTest;
                else if (NavSettings.IsSelected) btn = NavSettings;
                if (btn == null || !btn.IsLoaded || NavGrid.ActualWidth <= 0) return;

                bool isHorizontal = CurrentSidebarPosition is "Top" or "Bottom";
                var pos = btn.TransformToAncestor(NavGrid).Transform(new Point(0, 0));

                if (NavIndicator.Visibility != Visibility.Visible)
                {
                    NavIndicator.Visibility = Visibility.Visible;
                    animate = false;
                }

                if (isHorizontal)
                {
                    double targetX = pos.X;
                    double targetW = btn.ActualWidth;
                    double targetH = btn.ActualHeight > 0 ? btn.ActualHeight : 40;
                    if (targetW <= 0) return;

                    NavIndicator.Height = targetH;
                    NavIndicatorShift.Y = pos.Y;

                    if (!animate)
                    {
                        NavIndicator.BeginAnimation(WidthProperty, null);
                        NavIndicatorShift.BeginAnimation(TranslateTransform.XProperty, null);
                        NavIndicator.Width = targetW;
                        NavIndicatorShift.X = targetX;
                        return;
                    }

                    if (Math.Abs(NavIndicatorShift.X - targetX) < 0.5 &&
                        Math.Abs(NavIndicator.Width - targetW) < 0.5)
                        return;

                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    var animX = new DoubleAnimation(NavIndicatorShift.X, targetX,
                        TimeSpan.FromMilliseconds(210)) { EasingFunction = ease };
                    NavIndicatorShift.BeginAnimation(TranslateTransform.XProperty, animX,
                        HandoffBehavior.SnapshotAndReplace);
                    var animW = new DoubleAnimation(NavIndicator.Width, targetW,
                        TimeSpan.FromMilliseconds(210)) { EasingFunction = ease };
                    NavIndicator.BeginAnimation(WidthProperty, animW, HandoffBehavior.SnapshotAndReplace);
                }
                else
                {
                    double targetY = pos.Y;
                    double targetH = btn.ActualHeight;
                    if (targetH <= 0) return;

                    NavIndicator.Width = double.NaN; // stretch
                    NavIndicatorShift.X = 0;

                    if (!animate)
                    {
                        NavIndicator.BeginAnimation(HeightProperty, null);
                        NavIndicatorShift.BeginAnimation(TranslateTransform.YProperty, null);
                        NavIndicator.Height = targetH;
                        NavIndicatorShift.Y = targetY;
                        return;
                    }

                    if (Math.Abs(NavIndicatorShift.Y - targetY) < 0.5 &&
                        Math.Abs(NavIndicator.Height - targetH) < 0.5)
                        return;

                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    var animY = new DoubleAnimation(NavIndicatorShift.Y, targetY,
                        TimeSpan.FromMilliseconds(210)) { EasingFunction = ease };
                    NavIndicatorShift.BeginAnimation(TranslateTransform.YProperty, animY,
                        HandoffBehavior.SnapshotAndReplace);
                    var animH = new DoubleAnimation(NavIndicator.Height, targetH,
                        TimeSpan.FromMilliseconds(210)) { EasingFunction = ease };
                    NavIndicator.BeginAnimation(HeightProperty, animH, HandoffBehavior.SnapshotAndReplace);
                }
            }
            catch { }
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
                StopGif();
                StopVideo();
                BgImageHost.Visibility = Visibility.Collapsed;
                BgGifHost.Visibility = Visibility.Collapsed;
                BgVideoHost.Visibility = Visibility.Collapsed;
                BgImageHost.Effect = null;
                BgGifHost.Effect = null;
                BgVideoHost.Effect = null;
                BgGifHost.CacheMode = null;
                BgVideoHost.CacheMode = null;

                var path = Customization.BackgroundPath;
                var kind = Customization.Kind;
                if (kind == Customization.BackgroundKind.None ||
                    string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    BgDimHost.Opacity = 0;
                    ContentHost.SetResourceReference(Grid.BackgroundProperty, "BgDeepBrush");
                    SidebarHost.SetResourceReference(Border.BackgroundProperty, "BgPanelBrush");
                    return;
                }

                System.Windows.Media.Effects.Effect? blur = Customization.BackgroundBlur > 0
                    ? new System.Windows.Media.Effects.BlurEffect { Radius = Customization.BackgroundBlur }
                    : null;

                switch (kind)
                {
                    case Customization.BackgroundKind.Gif:
                        BgGifHost.Effect = blur;
                        // When blurred, render the animation at half resolution and let
                        // the GPU upscale it: blur on a fullscreen surface every frame
                        // is what drops the whole window to slideshow fps.
                        BgGifHost.CacheMode = blur != null ? new BitmapCache(0.5) : null;
                        BgGifHost.Visibility = Visibility.Visible;
                        StartGif(path);
                        break;
                    case Customization.BackgroundKind.Video:
                        BgVideoHost.Effect = blur;
                        BgVideoHost.CacheMode = blur != null ? new BitmapCache(0.5) : null;
                        BgVideoHost.Visibility = Visibility.Visible;
                        StartVideo(path);
                        break;
                    default:
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.DecodePixelWidth = 1920;
                        img.UriSource = new Uri(path);
                        img.EndInit();
                        img.Freeze();
                        // Bake the blur into the bitmap once: a live fullscreen
                        // BlurEffect is recomputed on the CPU for EVERY frame,
                        // turning any scroll/animation into a slideshow.
                        BgImageBrush.ImageSource = Customization.BackgroundBlur > 0
                            ? BakeBlurred(img, Customization.BackgroundBlur)
                            : img;
                        BgImageHost.Effect = null;
                        BgImageHost.Visibility = Visibility.Visible;
                        break;
                }

                BgDimHost.Opacity = Customization.BackgroundDim / 100.0;
                SidebarHost.SetResourceReference(Border.BackgroundProperty, "BgPanelBrush");
                ContentHost.SetResourceReference(Grid.BackgroundProperty, "BgDeepBrush");
            }
            catch
            {
                StopGif();
                StopVideo();
                BgImageHost.Visibility = Visibility.Collapsed;
                BgGifHost.Visibility = Visibility.Collapsed;
                BgVideoHost.Visibility = Visibility.Collapsed;
                BgDimHost.Opacity = 0;
            }
        }

        /// <summary>
        /// Bakes blur into a bitmap once at load. Rendered small (blur hides
        /// detail anyway) and upscaled on display by the GPU.
        /// </summary>
        private static BitmapSource BakeBlurred(BitmapSource src, double radius)
        {
            try
            {
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w <= 0 || h <= 0) return src;
                double scale = Math.Min(1.0, 960.0 / Math.Max(w, h));
                int rw = Math.Max(2, (int)(w * scale));
                int rh = Math.Max(2, (int)(h * scale));
                int pad = (int)(radius * scale * 2) + 8;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawImage(src, new Rect(pad, pad, rw, rh));
                dv.Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = Math.Max(2.0, radius * scale),
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                };

                var rtb = new RenderTargetBitmap(rw + pad * 2, rh + pad * 2, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                var crop = new CroppedBitmap(rtb, new Int32Rect(pad, pad, rw, rh));
                crop.Freeze();
                return crop;
            }
            catch { return src; }
        }

        // ---------------- Animated background: GIF ----------------

        private void StartGif(string path)
        {
            try
            {
                var decoder = new GifBitmapDecoder(new Uri(path),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return;

                // Pre-scale frames once: a 4K GIF re-uploaded + re-rastered every
                // frame is enough to drag all window animations down with it.
                // Blurred backgrounds hide detail anyway -> shrink harder.
                int cap = Customization.BackgroundBlur > 0 ? 800 : 1280;
                double scale = 1.0;
                int w = decoder.Frames[0].PixelWidth;
                if (w > cap) scale = cap / (double)w;

                _gifFrames = new List<BitmapSource>(decoder.Frames.Count);
                _gifDelays = new List<TimeSpan>(decoder.Frames.Count);
                foreach (var f in decoder.Frames)
                {
                    BitmapSource src = f;
                    if (scale < 1.0)
                        src = new TransformedBitmap(f, new ScaleTransform(scale, scale));
                    src.Freeze();
                    _gifFrames.Add(src);
                    _gifDelays.Add(GetGifFrameDelay(f));
                }

                _gifIndex = 0;
                BgGifHost.Source = _gifFrames[0];

                if (_gifFrames.Count == 1) return;

                _gifTimer = new DispatcherTimer { Interval = _gifDelays[0] };
                _gifTimer.Tick += (_, _) => AdvanceGifFrame();
                if (IsActive) _gifTimer.Start();
            }
            catch { }
        }

        private void AdvanceGifFrame()
        {
            try
            {
                if (_gifFrames.Count == 0) return;
                _gifIndex = (_gifIndex + 1) % _gifFrames.Count;
                BgGifHost.Source = _gifFrames[_gifIndex];
                if (_gifTimer != null)
                    _gifTimer.Interval = _gifDelays[_gifIndex];
            }
            catch { }
        }

        private static TimeSpan GetGifFrameDelay(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata meta &&
                    meta.ContainsQuery("/grctlext/Delay"))
                {
                    var delay = Convert.ToUInt16(meta.GetQuery("/grctlext/Delay"));
                    int ms = Math.Clamp(delay * 10, 20, 1000);
                    return TimeSpan.FromMilliseconds(ms);
                }
            }
            catch { }
            return TimeSpan.FromMilliseconds(100);
        }

        private void StopGif()
        {
            try { _gifTimer?.Stop(); } catch { }
            _gifTimer = null;
            _gifFrames = new List<BitmapSource>();
            _gifDelays = new List<TimeSpan>();
            _gifIndex = 0;
        }

        // ---------------- Animated background: video ----------------

        private void StartVideo(string path)
        {
            try
            {
                _videoActive = true;
                BgVideoHost.Source = new Uri(path);
                if (IsActive) BgVideoHost.Play();
            }
            catch { _videoActive = false; }
        }

        private void StopVideo()
        {
            _videoActive = false;
            try
            {
                BgVideoHost.Stop();
                BgVideoHost.Source = null;
            }
            catch { }
        }

        /// <summary>Pauses GIF/video so they don't burn CPU while another app is in front.</summary>
        private void PauseBackground()
        {
            try { _gifTimer?.Stop(); } catch { }
            try { if (_videoActive && BgVideoHost.Source != null) BgVideoHost.Pause(); } catch { }
            NavButton.PauseIconAnimations();
        }

        private void ResumeBackground()
        {
            try
            {
                if (_gifTimer != null && BgGifHost.Visibility == Visibility.Visible && !IsActive) return;
                if (_gifTimer != null && BgGifHost.Visibility == Visibility.Visible) _gifTimer.Start();
            }
            catch { }
            try
            {
                if (_videoActive && BgVideoHost.Source != null &&
                    BgVideoHost.Visibility == Visibility.Visible && IsActive)
                    BgVideoHost.Play();
            }
            catch { }
            NavButton.ResumeIconAnimations();
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
            StopGif();
            StopVideo();
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
