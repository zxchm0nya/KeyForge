using System.Windows;

namespace KyeForge.App.Services;

/// <summary>Runtime localization: resolves strings from the merged language dictionary and swaps languages live.</summary>
public static class Loc
{
    public static string Current { get; internal set; } = "ru";

    /// <summary>Raised after the UI language dictionary was swapped.</summary>
    public static event Action? LanguageChanged;

    public static string T(string key)
        => (Application.Current?.TryFindResource(key) as string) ?? key;

    public static string T(string key, params object[] args)
        => string.Format(T(key), args);

    public static void SetLanguage(string code)
    {
        if (Current == code) return;
        Current = code;

        var app = Application.Current;
        if (app == null) return;

        var merged = app.Resources.MergedDictionaries;
        var langs = merged.Where(d => d.Source != null &&
            d.Source.OriginalString.IndexOf("lang.", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        foreach (var d in langs) merged.Remove(d);

        merged.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Resources/lang.{code}.xaml", UriKind.Relative)
        });

        var s = AppSettings.Load();
        s.Language = code;
        s.Save();

        LanguageChanged?.Invoke();
    }
}
