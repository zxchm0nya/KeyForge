using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KyeForge.App.Views;

/// <summary>Small modal: pick a running process + name the profile.</summary>
public partial class ProfilePickerDialog : Window
{
    public string? SelectedProcess { get; private set; }
    public string ProfileName => string.IsNullOrWhiteSpace(NameBox.Text)
        ? (SelectedProcess ?? "")
        : NameBox.Text.Trim();

    public ProfilePickerDialog(IReadOnlyList<string> processes)
    {
        InitializeComponent();
        foreach (var p in processes)
            ProcessCombo.Items.Add(new ComboBoxItem { Content = p, Tag = p });
        if (ProcessCombo.Items.Count > 0)
            ProcessCombo.SelectedIndex = 0;
        UiAnimations.FadeSlideIn(this);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Ok_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessCombo.SelectedItem is ComboBoxItem { Tag: string proc })
        {
            SelectedProcess = proc;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
