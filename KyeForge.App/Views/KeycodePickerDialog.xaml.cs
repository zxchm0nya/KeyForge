using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

public partial class KeycodePickerDialog : Window
{
    public uint SelectedKeycode { get; private set; }

    private class PickItem
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
        public uint Value { get; set; }
        public string SearchText => Name + " " + Hex;
    }

    private readonly List<PickItem> _all = new();

    public KeycodePickerDialog(uint current)
    {
        InitializeComponent();
        UiAnimations.FadeSlideIn(this);

        _all.Add(new PickItem { Name = "NO (unbind)", Hex = "0x00", Value = 0 });
        foreach (var kv in KeycodeMap.Basic)
            if (kv.Key != 0)
                _all.Add(new PickItem { Name = kv.Value, Hex = Format(kv.Key), Value = kv.Key });
        foreach (var kv in KeycodeMap.Media)
            _all.Add(new PickItem { Name = kv.Value, Hex = Format(kv.Key), Value = kv.Key });
        foreach (var kv in KeycodeMap.CustomEntries)
            _all.Add(new PickItem { Name = kv.Value + " (custom)", Hex = Format(kv.Key), Value = kv.Key });
        foreach (uint modCombo in new[] { 0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7 })
        {
            foreach (var kv in KeycodeMap.Basic)
            {
                if (kv.Key == 0 || kv.Key >= 0xE0) continue;
                uint combo = (kv.Key | (modCombo << 8));
                _all.Add(new PickItem { Name = KeycodeMap.Name(combo), Hex = Format(kv.Key) + " + mod", Value = combo });
            }
        }

        ApplyFilter("");
        SelectValue(current);
    }

    private static string Format(uint v) => $"0x{v:X2}";

    private void ApplyFilter(string q)
    {
        q = q.Trim().ToLowerInvariant();
        var source = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(i => i.SearchText.ToLowerInvariant().Contains(q)).ToList();

        var list = new ObservableCollection<PickItem>(source);
        KeyList.ItemsSource = list;
    }

    private void SelectValue(uint value)
    {
        for (int i = 0; i < KeyList.Items.Count; i++)
        {
            if (KeyList.Items[i] is PickItem it && it.Value == value)
            {
                KeyList.SelectedIndex = i;
                KeyList.ScrollIntoView(KeyList.Items[i]);
                break;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text);

    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyList.SelectedItem is PickItem it)
            SelectedKeycode = it.Value;
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        SelectedKeycode = 0;
        DialogResult = true;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is PickItem it)
            SelectedKeycode = it.Value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OK_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}