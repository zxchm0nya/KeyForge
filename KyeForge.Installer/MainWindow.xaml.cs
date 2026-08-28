using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace KyeForge.Installer;

public partial class MainWindow : Window
{
    private const string AppExeResource = "KyeForge.App.exe";
    private const string AppExeName = "KyeForge.exe";
    private bool _silent;
    private string _logFile = "";

    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KyeForge";
    private const string AppKey = @"SOFTWARE\KyeForge";
    private const string InstallerTitle = "KyeForge Setup";

    private int _step = 1;
    private bool _isUninstalling;

    private static readonly string[] Steps = { "Welcome", "Location", "Install", "Done" };

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        _isUninstalling = args.Contains("--uninstall");
        if (_isUninstalling)
        {
            Title = "KyeForge Uninstaller";
            SetupUninstallUi();
        }
        else if (args.Contains("--silent"))
        {
            _silent = true;
            _logFile = Path.Combine(Path.GetTempPath(), "kyeforge-install.log");
            Log("KyeForge silent install started");
            // Silent install: install to path from env or default, and exit.
            Loaded += async (_, _) =>
            {
                var envPath = Environment.GetEnvironmentVariable("KF_INSTALL_DIR");
                InstallPath.Text = string.IsNullOrEmpty(envPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KyeForge")
                    : envPath;
                Hide();
                await RunInstall();
                Close();
            };
        }

        RenderSteps();
    }

    // ==================== UI helpers ====================

    private void RenderSteps()
    {
        StepsHost.Items.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            int idx = i;
            bool active = i == _step - 1;
            bool done = i < _step - 1;

            var border = new Border
            {
                Margin = new Thickness(0, 0, 10, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 12, 7),
                Background = new SolidColorBrush(active
                    ? (Color)FindResource("BgCard")
                    : System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(active
                    ? (Color)FindResource("Accent")
                    : (Color)FindResource("Border")),
                BorderThickness = new Thickness(1),
            };
            var txt = new TextBlock
            {
                Text = $"{idx + 1}. {Steps[idx]}",
                FontSize = 12.5,
                FontWeight = done || active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(active || done
                    ? (Color)FindResource("TextPrimary")
                    : (Color)FindResource("TextMuted")),
            };
            border.Child = txt;
            StepsHost.Items.Add(border);
        }
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepWelcome.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepPath.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepProgress.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepDone.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        BtnBack.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        BtnCancel.Visibility = step <= 2 ? Visibility.Visible : Visibility.Collapsed;
        BtnNext.Visibility = step != 3 ? Visibility.Visible : Visibility.Collapsed;

        BtnNext.Content = _isUninstalling
            ? (step == 1 ? "Uninstall" : step == 4 ? "Finish" : "Next")
            : (step == 1 ? "Next" : step == 2 ? "Install" : step == 4 ? "Finish" : "Next");

        RenderSteps();
    }

    // ==================== Buttons ====================

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstalling)
        {
            if (_step == 1) { ShowStep(3); _ = RunUninstall(); }
            else if (_step == 4) { Close(); }
            return;
        }

        switch (_step)
        {
            case 1:
                ShowStep(2);
                break;
            case 2:
                ShowStep(3);
                _ = RunInstall();
                break;
            case 4:
                if (LaunchAfter.IsChecked == true) LaunchApp();
                Close();
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 2) ShowStep(1);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select install folder",
            SelectedPath = InstallPath.Text,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            InstallPath.Text = dlg.SelectedPath;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // ==================== Install / Uninstall ====================

    private async Task RunInstall()
    {
        BtnNext.IsEnabled = false;

        string target = InstallPath.Text.Trim();
        string exeTarget = Path.Combine(target, AppExeName);

        ProgressText.Text = "Checking installation folder...";
        InstallProgress.Value = 10;

        try
        {
            Directory.CreateDirectory(target);

            ProgressText.Text = "Extracting KyeForge...";
            InstallProgress.Value = 35;
            await Task.Delay(100);

            byte[]? data = ExtractResource(AppExeResource);
            if (data == null)
            {
                throw new InvalidOperationException("Bundled application data is missing. Re-run the setup from the installer package.");
            }

            await File.WriteAllBytesAsync(exeTarget, data);
            InstallProgress.Value = 50;

            ProgressText.Text = "Extracting source code...";
            await Task.Delay(50);

            byte[]? srcData = ExtractResource("KyeForge.Source.zip");
            if (srcData != null)
            {
                var srcZip = Path.Combine(target, "_source.zip");
                await File.WriteAllBytesAsync(srcZip, srcData);
                var srcDir = Path.Combine(target, "src");
                ZipFile.ExtractToDirectory(srcZip, srcDir);
                File.Delete(srcZip);
            }

            InstallProgress.Value = 75;

            // Bundle the setup itself as the uninstaller
            try
            {
                var self = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(self) && File.Exists(self))
                    File.Copy(self, Path.Combine(target, "KyeForgeSetup.exe"), true);
            }
            catch { }

            ProgressText.Text = "Creating shortcuts...";
            await Task.Delay(80);

            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var startMenuDir = Path.Combine(startMenu, "KyeForge");
            Directory.CreateDirectory(startMenuDir);

            CreateShortcut(Path.Combine(startMenuDir, "KyeForge.lnk"), exeTarget);
            if (ChkDesktopShortcut.IsChecked == true)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "KyeForge.lnk"), exeTarget);

            ProgressText.Text = "Registering uninstaller...";
            await Task.Delay(80);
            WriteRegistry(exeTarget);

            InstallProgress.Value = 100;
            ProgressText.Text = "Done.";

            DoneTitle.Text = "Installation complete";
            DoneSub.Text = $"KyeForge was installed to:\n{target}\n\nPlug in your VIA keyboard and load a config to get started.";
            ShowStep(4);
        }
        catch (Exception ex)
        {
            InstallProgress.Value = 0;
            ProgressText.Text = "Installation failed: " + ex.Message;
            Log("Install failed: " + ex);
            if (_silent)
            {
                try { File.WriteAllText(Path.Combine(InstallPath.Text, "install-error.txt"), ex.ToString()); } catch { }
                Close();
                return;
            }
            System.Windows.MessageBox.Show("Installation failed.\n\n" + ex.Message, InstallerTitle,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            ShowStep(2);
        }
        finally
        {
            BtnNext.IsEnabled = true;
        }
    }

    private async Task RunUninstall()
    {
        BtnNext.IsEnabled = false;
        ProgressText.Text = "Removing program files...";
        InstallProgress.Value = 40;

        try
        {
            string? target = GetRegistryValue(AppKey, "InstallPath") as string;
            if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
            {
                // Wait for the app to exit if it's running
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExeName)))
                    p.Kill();

                await Task.Delay(150);
                Directory.Delete(target, true);
            }
            InstallProgress.Value = 70;

            var startMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KyeForge", "KyeForge.lnk");
            var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "KyeForge.lnk");
            if (File.Exists(startMenuLink)) File.Delete(startMenuLink);
            if (File.Exists(desktopLink)) File.Delete(desktopLink);
            var smDir = Path.GetDirectoryName(startMenuLink);
            if (!string.IsNullOrEmpty(smDir) && Directory.Exists(smDir) && !Directory.EnumerateFileSystemEntries(smDir).Any())
                Directory.Delete(smDir);

            InstallProgress.Value = 90;

            using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
                key?.DeleteSubKeyTree("", false);
            using (var key = Registry.LocalMachine.CreateSubKey(AppKey))
                key?.DeleteSubKeyTree("", false);

            InstallProgress.Value = 100;
            ProgressText.Text = "KyeForge was removed.";
            DoneTitle.Text = "Uninstall complete";
            DoneSub.Text = "KyeForge was removed from your computer.";
            ShowStep(4);
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Uninstall failed: " + ex.Message;
            Log("Uninstall failed: " + ex);
            if (_silent) { Close(); return; }
            System.Windows.MessageBox.Show("Uninstall failed.\n\n" + ex.Message, InstallerTitle,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            ShowStep(1);
        }
        finally
        {
            BtnNext.IsEnabled = true;
        }
    }

    private void SetupUninstallUi()
    {
        DoneTitle.Text = "Uninstall complete";
        BtnNext.Content = "Uninstall";
        ProgressText.Text = "Removing KyeForge...";
        StepWelcome.Children.Clear();
        StepWelcome.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SectionTitle"),
            Text = "Uninstall KyeForge?"
        });
        StepWelcome.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SubText"),
            Margin = new Thickness(0, 8, 0, 0),
            Text = "This will remove the application, shortcuts and registry entries from your computer."
        });
        StepPath.Visibility = Visibility.Collapsed;
        ShowStep(1);
    }

    // ==================== Helpers ====================

    private void Log(string msg)
    {
        try { File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}"); } catch { }
    }

    private static byte[]? ExtractResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res == null) return null;
        using var stream = asm.GetManifestResourceStream(res);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void CreateShortcut(string lnkPath, string exePath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic? wsh = Activator.CreateInstance(shellType);
            if (wsh == null) return;
            dynamic sc = wsh.CreateShortcut(lnkPath);
            sc.TargetPath = exePath;
            sc.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            sc.IconLocation = exePath + ",0";
            sc.Description = "KyeForge - Keyboard Configurator";
            sc.Save();
        }
        catch
        {
            // Shortcuts are best-effort; ignore failures.
        }
    }

    private static void LaunchApp()
    {
        try
        {
            var target = Path.Combine(GetRegistryValue(AppKey, "InstallPath") as string ?? "", AppExeName);
            if (File.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { }
    }

    private void WriteRegistry(string exePath)
    {
        try
        {
            using (var k = Registry.LocalMachine.CreateSubKey(AppKey))
            {
                k.SetValue("InstallPath", Path.GetDirectoryName(exePath) ?? "");
                k.SetValue("Version", "1.0.0");
            }

            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "KyeForge");
                k.SetValue("DisplayVersion", "1.0.0");
                k.SetValue("Publisher", "KyeForge");
                k.SetValue("DisplayIcon", exePath);
                k.SetValue("InstallLocation", Path.GetDirectoryName(exePath) ?? "");
                k.SetValue("UninstallString", $"\"{Path.Combine(Path.GetDirectoryName(exePath) ?? "", "KyeForgeSetup.exe")}\" --uninstall");
            }
        }
        catch (Exception ex)
        {
            Log("Registry write failed: " + ex.Message);
            if (!_silent)
            {
                System.Windows.MessageBox.Show("Files were installed, but writing the registry uninstaller entry failed.\n\n" + ex.Message,
                    InstallerTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    private static object? GetRegistryValue(string subKey, string name)
    {
        using var k = Registry.LocalMachine.OpenSubKey(subKey);
        return k?.GetValue(name);
    }
}
