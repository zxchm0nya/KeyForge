namespace KyeForge.App.Plugins;

/// <summary>
/// Contract for KeyForge plugins. Drop a .dll implementing this interface
/// into the <c>plugins/</c> folder next to KeyForge.exe — it is loaded on startup.
/// </summary>
public interface IKeyForgePlugin
{
    /// <summary>Unique id, e.g. "com.example.mypugin".</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the log / settings.</summary>
    string Name { get; }

    /// <summary>Semantic version of the plugin.</summary>
    string Version { get; }

    /// <summary>Called once after the plugin is loaded. Runs on the UI thread.</summary>
    void OnLoad();

    /// <summary>Called before the app exits.</summary>
    void OnUnload();

    /// <summary>Raised when the active lighting/keymap profile changes (optional hook).</summary>
    /// <param name="profileName">New profile name, or "" when reset to default.</param>
    void OnProfileSwitch(string profileName);
}
