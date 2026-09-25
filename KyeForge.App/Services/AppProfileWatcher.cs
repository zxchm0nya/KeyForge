using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using KyeForge.App.Plugins;
using KyeForge.App.Services;

namespace KyeForge.App.Services;

/// <summary>A keyboard lighting/keymap profile bound to a Windows process name.</summary>
public class AppProfile
{
    /// <summary>Process name without .exe, e.g. "chrome" or "cs2". Case-insensitive.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Profile label shown in the UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>Lighting settings applied when this profile activates.</summary>
    public int? LightingEffect { get; set; }
    public double? LightingBrightness { get; set; }
    public double? LightingSpeed { get; set; }
    public string LightingColor { get; set; } = "";
}

/// <summary>
/// Watches the foreground window and switches keyboard profiles when the active
/// application changes. Uses GetForegroundWindow + GetWindowThreadProcessId (P/Invoke).
/// </summary>
public static class AppProfileWatcher
{
    public static event Action<AppProfile?>? ProfileChanged;

    private static readonly HashSet<AppProfile> _profiles = new();
    private static AppProfile? _current;
    private static System.Timers.Timer? _timer;
    private static int _lastPid;

    /// <summary>Currently active profile (null = default / no match).</summary>
    public static AppProfile? Current => _current;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KyeForge", "profiles.json");

    // ---- P/Invoke ----
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static IReadOnlyCollection<AppProfile> Profiles => _profiles;

    public static void Load()
    {
        try
        {
            _profiles.Clear();
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<AppProfile>>(
                    File.ReadAllText(FilePath), JsonOpts());
                if (list != null)
                    foreach (var p in list)
                        if (!string.IsNullOrWhiteSpace(p.ProcessName))
                            _profiles.Add(p);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("AppProfileWatcher.Load failed", ex);
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(_profiles.ToList(), JsonOpts()));
        }
        catch (Exception ex)
        {
            AppLog.Error("AppProfileWatcher.Save failed", ex);
        }
    }

    public static void AddOrUpdate(AppProfile profile)
    {
        _profiles.RemoveWhere(p =>
            string.Equals(p.ProcessName, profile.ProcessName, StringComparison.OrdinalIgnoreCase));
        _profiles.Add(profile);
        Save();
    }

    public static bool Remove(string processName)
    {
        var removed = _profiles.RemoveWhere(p =>
            string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>Starts polling the foreground window (every 800 ms).</summary>
    public static void Start()
    {
        if (_timer != null) return;
        _timer = new System.Timers.Timer(800) { AutoReset = true };
        _timer.Elapsed += (_, _) => Poll();
        _timer.Start();
        AppLog.Info("AppProfileWatcher started (800 ms poll)");
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private static void Poll()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == _lastPid) return;
            _lastPid = (int)pid;

            string procName;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName ?? "";
            }
            catch
            {
                return; // system process / access denied
            }

            if (string.IsNullOrEmpty(procName)) return;

            AppProfile? match = null;
            foreach (var p in _profiles)
            {
                if (string.Equals(p.ProcessName, procName, StringComparison.OrdinalIgnoreCase))
                {
                    match = p;
                    break;
                }
            }

            if (ReferenceEquals(match, _current)) return;
            _current = match;

            AppLog.Info(match != null
                ? $"Foreground → {procName}, profile \"{match.Name}\""
                : $"Foreground → {procName}, no profile");

            // Raise on UI thread so WPF subscribers can apply lighting safely.
            var captured = match;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ProfileChanged?.Invoke(captured);
                PluginHost.NotifyProfileSwitch(captured?.Name ?? "");
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("AppProfileWatcher.Poll failed", ex);
        }
    }

    private static JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
