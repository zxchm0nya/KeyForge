using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KyeForge.App.Services;

/// <summary>Result of a GitHub release check against the installed build.</summary>
public sealed record UpdateInfo(
    string Version,
    string Title,
    string Body,
    string Url,
    bool IsNewer);

/// <summary>
/// Checks zxchm0nya/KeyForge releases on GitHub (same idea as zapret's check_updates):
/// fetch latest release, compare semver with the local assembly version, expose notes + URL.
/// </summary>
public static class UpdateChecker
{
    public const string RepoOwner = "zxchm0nya";
    public const string RepoName = "KeyForge";
    public const string RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    /// <summary>Last successful (or failed-as-null) check result.</summary>
    public static UpdateInfo? LastResult { get; private set; }

    /// <summary>True once a check finished this session (result may be null on error).</summary>
    public static bool HasChecked { get; private set; }

    /// <summary>Raised on the UI thread after CheckAsync completes.</summary>
    public static event Action<UpdateInfo?>? Checked;

    static UpdateChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("KeyForge-Updater");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        Http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
        };
    }

    /// <summary>Fire-and-forget startup check; never throws to the caller.</summary>
    public static async Task CheckInBackgroundAsync()
    {
        try
        {
            var info = await CheckAsync().ConfigureAwait(false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Publish(info));
        }
        catch
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Publish(null));
            }
            catch { }
        }
    }

    /// <summary>Marks a check finished and raises <see cref="Checked"/> (UI thread).</summary>
    public static void Publish(UpdateInfo? info)
    {
        LastResult = info;
        HasChecked = true;
        Checked?.Invoke(info);
    }

    public static string LocalVersion
    {
        get
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
            catch { return "0.0.0"; }
        }
    }

    /// <summary>Fetches the latest release. Returns null on network/API failure.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            string tag = Str(root, "tag_name");
            string name = Str(root, "name");
            string body = Str(root, "body");
            string html = Str(root, "html_url");
            if (string.IsNullOrWhiteSpace(html))
                html = string.IsNullOrEmpty(tag) ? RepoUrl : $"{RepoUrl}/releases/latest";

            string version = ExtractVersion(name) ?? ExtractVersion(tag) ?? "0.0.0";
            bool newer = IsNewer(version, LocalVersion);

            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrWhiteSpace(tag) ? version : tag;

            var info = new UpdateInfo(version, name, body?.Trim() ?? "", html, newer);
            return info;
        }
        catch
        {
            return null;
        }
    }

    public static void OpenRelease(UpdateInfo? info)
    {
        var url = info?.Url;
        if (string.IsNullOrWhiteSpace(url)) url = $"{RepoUrl}/releases/latest";
        OpenUrl(url);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Best-effort markdown → plain text for the toast body.</summary>
    public static string PlainNotes(string? body, int maxChars = 600)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var s = body.Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"```[\s\S]*?```", " ");
        s = Regex.Replace(s, @"`([^`]+)`", "$1");
        s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", " ");
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
        s = Regex.Replace(s, @"^#{1,6}\s*", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "$1");
        s = Regex.Replace(s, @"\*([^*]+)\*", "$1");
        s = Regex.Replace(s, @"^>\s?", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = s.Trim();
        if (s.Length > maxChars)
            s = s[..maxChars].TrimEnd() + "…";
        return s;
    }

    private static string Str(JsonElement root, string prop)
        => root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static string? ExtractVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\b(\d+)\.(\d+)(?:\.(\d+))?\b");
        if (!m.Success) return null;
        int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return $"{int.Parse(m.Groups[1].Value)}.{int.Parse(m.Groups[2].Value)}.{patch}";
    }

    private static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(Normalize(remote), out var r)) return false;
        if (!Version.TryParse(Normalize(local), out var l)) return true;
        return r > l;
    }

    private static string Normalize(string v)
    {
        var m = Regex.Match(v.Trim().TrimStart('v', 'V'), @"^\d+(\.\d+){0,3}");
        if (!m.Success) return "0.0.0";
        var parts = m.Value.Split('.');
        while (parts.Length < 4) parts = parts.Append("0").ToArray();
        return string.Join('.', parts);
    }
}
