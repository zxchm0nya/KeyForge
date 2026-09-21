using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using KyeForge.App.Models;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

public partial class KeymapView : UserControl
{
    private readonly AppState _state = StateHub.State;
    private const double KeyUnit = 46;
    private const double KeyGap = 6;

    private static readonly Brush KeyFill = Solid("#161F27");
    private static readonly Brush KeyFillHover = Solid("#20303A");
    private static readonly Brush KeyBorder = Solid("#33414D");
    private static readonly Brush KeyBorderHover = Solid("#28D7B7");
    private static readonly Brush KeyText = Solid("#F3F5FA");

    private static SolidColorBrush Solid(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    public KeymapView()
    {
        InitializeComponent();
        _state.PropertyChanged += OnStateChanged;
        OnStateChanged(_state, null);
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
    {
        if (e != null && e.PropertyName != nameof(AppState.Layout) && e.PropertyName != nameof(AppState.Definition))
            return;
        Application.Current.Dispatcher.Invoke(RenderLayout);
    }

    private void RenderLayout()
    {
        CanvasHost.Content = null;
        EmptyMsg.Visibility = _state.Layout == null ? Visibility.Visible : Visibility.Collapsed;
        HintMsg.Visibility = _state.Layout == null ? Visibility.Collapsed : Visibility.Visible;
        if (_state.Layout == null) return;

        var canvas = new Canvas { Background = Brushes.Transparent };
        var seenGeometry = new HashSet<(double X, double Y, double W, double H)>();

        foreach (var key in _state.Layout.Keys)
        {
            if (key.IsEncoder || key.MatrixRow < 0) continue;

            // Skip exact duplicate placements (same spot drawn twice = visual overlap)
            if (!seenGeometry.Add((key.X, key.Y, key.W, key.H))) continue;

            double ux = key.X, uy = key.Y, uw = key.W, uh = key.H;
            double px = ux * KeyUnit;
            double py = uy * KeyUnit;
            double pw = uw * KeyUnit - KeyGap;
            double ph = uh * KeyUnit - KeyGap;

            var border = new Border
            {
                Width = Math.Max(14, pw),
                Height = Math.Max(14, ph),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            border.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            Canvas.SetLeft(border, px + KeyGap / 2);
            Canvas.SetTop(border, py + KeyGap / 2);

            var tb = new TextBlock
            {
                FontSize = pw >= 42 ? 11 : pw >= 28 ? 10 : pw >= 18 ? 8.5 : 7,
                FontWeight = FontWeights.SemiBold,
                Text = LegendFor(key),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 0)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            border.Child = tb;

            border.MouseEnter += (_, _) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "BgHoverBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            };
            border.MouseLeave += (_, _) =>
            {
                border.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            };

            var keyForClick = key;
            border.MouseLeftButtonDown += async (_, _) => await EditKeyAsync(border, keyForClick);

            canvas.Children.Add(border);
        }

        canvas.Width = _state.Layout.MaxX * KeyUnit + 12;
        canvas.Height = _state.Layout.MaxY * KeyUnit + 12;
        CanvasHost.Content = new ScrollViewer
        {
            Content = canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        ScrollHost.ScrollToTop();
        ScrollHost.ScrollToLeftEnd();
    }

    private string LegendFor(LayoutKey key)
    {
        if (key.Keycode == 0) return key.Legend ?? "";
        return KeycodeMap.Name(key.Keycode);
    }

    private async Task EditKeyAsync(Border keyVisual, LayoutKey key)
    {
        var picker = new KeycodePickerDialog(key.Keycode)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() != true) return;

        key.Keycode = (ushort)picker.SelectedKeycode;
        (keyVisual.Child as TextBlock)!.Text = LegendFor(key);
        _state.Keycodes[(key.MatrixRow, key.MatrixCol)] = (ushort)picker.SelectedKeycode;
        _state.DeviceStatus = Loc.T("t_stat_keycode_set", KeycodeMap.Name(picker.SelectedKeycode));

        if (_state.Client != null)
        {
            await _state.Client.SetKeycodeAsync(0, (byte)key.MatrixRow, (byte)key.MatrixCol, (ushort)picker.SelectedKeycode);
        }
    }

    private async void BtnReadKeymap_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Client == null) { MessageBox.Show(Loc.T("t_msg_connect_first"), "KeyForge"); return; }
        if (_state.Layout == null) { MessageBox.Show(Loc.T("t_msg_load_config_first"), "KeyForge"); return; }

        BtnReadKeymap.IsEnabled = false;
        try
        {
            foreach (var k in _state.Layout.Keys)
            {
                if (k.IsEncoder || k.MatrixRow < 0) continue;
                var res = await _state.Client.GetKeycodeAsync(0, (byte)k.MatrixRow, (byte)k.MatrixCol);
                if (res != null && res.Success)
                    k.Keycode = res.Keycode;
            }
            RenderLayout();
            _state.DeviceStatus = Loc.T("t_stat_keymap_read");
        }
        catch (Exception ex)
        {
            _state.DeviceStatus = Loc.T("t_stat_keymap_failed", ex.Message);
        }
        finally { BtnReadKeymap.IsEnabled = true; }
    }

    private void BtnBlankBoard_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Layout == null) return;
        foreach (var k in _state.Layout.Keys) k.Keycode = 0;
        RenderLayout();
        _state.DeviceStatus = "";
    }

    private void BtnSaveLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Layout == null) return;
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("t_dlg_export_title"),
            Filter = "JSON (*.json)|*.json",
            FileName = "keymap.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var rows = new List<List<ushort>>();
            int maxRow = _state.Layout.Keys.Count > 0 ? _state.Layout.Keys.Max(k => k.MatrixRow) + 1 : 0;
            int maxCol = _state.Layout.Keys.Count > 0 ? _state.Layout.Keys.Max(k => k.MatrixCol) + 1 : 0;
            for (int r = 0; r < maxRow; r++)
            {
                var row = new List<ushort>();
                for (int c = 0; c < maxCol; c++)
                    row.Add(_state.Keycodes.TryGetValue((r, c), out var kc) ? kc : (ushort)0);
                rows.Add(row);
            }
            var payload = new { layers = new[] { rows } };
            File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            _state.DeviceStatus = Loc.T("t_stat_exported");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "KeyForge");
        }
    }
}
