using System.IO;
using System.Reflection;
using KyeForge.App.Services;

namespace KyeForge.App.Plugins;

/// <summary>
/// Loads every *.dll from the plugins/ folder that implements <see cref="IKeyForgePlugin"/>.
/// Failures are logged and skipped — a broken plugin never blocks startup.
/// </summary>
public static class PluginHost
{
    private static readonly List<IKeyForgePlugin> Loaded = new();

    public static IReadOnlyList<IKeyForgePlugin> Plugins => Loaded;

    public static string PluginsDirectory
    {
        get
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "plugins");
        }
    }

    public static void LoadAll()
    {
        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            var dlls = Directory.GetFiles(PluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly);
            AppLog.Info($"Plugins: scanning {dlls.Length} dll(s) in {PluginsDirectory}");

            foreach (var dll in dlls)
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (!typeof(IKeyForgePlugin).IsAssignableFrom(type)) continue;

                        if (Activator.CreateInstance(type) is not IKeyForgePlugin plugin)
                            continue;

                        plugin.OnLoad();
                        Loaded.Add(plugin);
                        AppLog.Info($"Plugin loaded: {plugin.Name} ({plugin.Id}) v{plugin.Version}");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load plugin {Path.GetFileName(dll)}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("PluginHost.LoadAll failed", ex);
        }
    }

    public static void UnloadAll()
    {
        foreach (var p in Loaded)
        {
            try { p.OnUnload(); }
            catch (Exception ex) { AppLog.Error($"Plugin unload failed: {p.Id}", ex); }
        }
        Loaded.Clear();
    }

    public static void NotifyProfileSwitch(string profileName)
    {
        foreach (var p in Loaded)
        {
            try { p.OnProfileSwitch(profileName); }
            catch (Exception ex) { AppLog.Error($"Plugin OnProfileSwitch failed: {p.Id}", ex); }
        }
    }
}
