using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

/// <summary>
/// One-time nudge shown after launch: asks the user to star the repository.
/// The flag is persisted in settings so it never opens twice.
/// </summary>
public partial class StarPromptDialog : Window
{
    public StarPromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => BtnStar.Focus();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateChecker.RepoUrl) { UseShellExecute = true });
        }
        catch { }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
