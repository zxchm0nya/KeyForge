using System.IO;
using Serilog;
using Serilog.Events;

namespace KyeForge.App.Services;

/// <summary>App-wide Serilog logger: file with rotation + in-memory list for the export UI.</summary>
public static class AppLog
{
    public const string AppName = "KeyForge";

    /// <summary>Full path of the current log file (set by <see cref="Init"/>).</summary>
    public static string LogFilePath { get; private set; } = "";

    /// <summary>Directory that holds rolling log files.</summary>
    public static string LogDirectory { get; private set; } = "";

    private static bool _inited;

    public static void Init()
    {
        if (_inited) return;
        _inited = true;
        try
        {
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName, "logs");
            Directory.CreateDirectory(LogDirectory);
            LogFilePath = Path.Combine(LogDirectory, "keyforge-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== {App} {Version} started ===", AppName, VersionText);
        }
        catch
        {
            // Logging must never break the app.
            _inited = false;
        }
    }

    public static string VersionText
    {
        get
        {
            try
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v?.ToString(3) ?? "0.0.0";
            }
            catch { return "0.0.0"; }
        }
    }

    public static void Debug(string message) => Safe(m => Log.Debug(m), message);
    public static void Info(string message) => Safe(m => Log.Information(m), message);
    public static void Warn(string message) => Safe(m => Log.Warning(m), message);
    public static void Error(string message) => Safe(m => Log.Error(m), message);
    public static void Error(string message, Exception ex) => Safe((m, e) => Log.Error(e, m), message, ex);

    public static void Fatal(string message, Exception ex) => Safe((m, e) => Log.Fatal(e, m), message, ex);

    private static void Safe(Action<string> a, string m)
    {
        try { a(m); } catch { }
    }

    private static void Safe(Action<string, Exception> a, string m, Exception e)
    {
        try { a(m, e); } catch { }
    }

    /// <summary>Flushes sinks so the export button can zip a complete file.</summary>
    public static void Flush()
    {
        try { Log.CloseAndFlush(); } catch { }
        // Re-open so logging continues after export.
        if (_inited)
        {
            try
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        path: LogFilePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();
            }
            catch { }
        }
    }

    /// <summary>Zips all log files into a temp archive and returns its path (or "" on failure).</summary>
    public static string ExportArchive()
    {
        try
        {
            if (string.IsNullOrEmpty(LogDirectory) || !Directory.Exists(LogDirectory)) return "";
            var files = Directory.GetFiles(LogDirectory, "keyforge-*.log");
            if (files.Length == 0) return "";

            Flush();
            var outDir = Path.Combine(Path.GetTempPath(), "KeyForgeLogs");
            Directory.CreateDirectory(outDir);
            var zipPath = Path.Combine(outDir,
                $"keyforge-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            System.IO.Compression.ZipFile.CreateFromDirectory(
                LogDirectory, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        catch
        {
            return "";
        }
    }
}
