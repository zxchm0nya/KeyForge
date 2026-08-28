using System.Globalization;
using System.Windows;
using KyeForge.App.Services;

namespace KyeForge.App;

public partial class App : Application
{
    /// <summary>Merged language dictionary is loaded here: Startup fires before the StartupUri window is created.</summary>
    private void App_Startup(object sender, StartupEventArgs e)
    {
        var s = AppSettings.Load();
        var code = s.Language;
        if (string.IsNullOrEmpty(code))
            code = "ru";
        code = code == "ru" ? "ru" : "en";
        Loc.Current = code;
        Resources.MergedDictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Resources/lang.{code}.xaml", UriKind.Relative)
        });

        // Apply saved theme customization before any window is created.
        Customization.Apply(s);
    }
}
