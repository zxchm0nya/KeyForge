using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KyeForge.App.Services;

namespace KyeForge.App.Views;

/// <summary>
/// Dashboard-style reorderable blocks. Attach to a vertical StackPanel:
/// every direct child carrying a BlockId becomes a draggable block.
/// Blocks are dragged by a hover grip (top-right corner), glide aside with
/// FLIP animations, snap in with a settle animation, auto-scroll at the
/// edges, cancel on Esc, persist per page and reset to default on demand.
/// Big blocks always move as one unit and stretch to the panel width.
/// </summary>
public static class BlockLayout
{
    public static readonly DependencyProperty BlockIdProperty =
        DependencyProperty.RegisterAttached("BlockId", typeof(string), typeof(BlockLayout),
            new PropertyMetadata(""));
    public static string GetBlockId(DependencyObject d) => (string)d.GetValue(BlockIdProperty);
    public static void SetBlockId(DependencyObject d, string value) => d.SetValue(BlockIdProperty, value);

    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(BlockLayout),
            new PropertyMetadata(false, OnEnableChanged));
    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);

    public static readonly DependencyProperty PageIdProperty =
        DependencyProperty.RegisterAttached("PageId", typeof(string), typeof(BlockLayout),
            new PropertyMetadata(""));
    public static string GetPageId(DependencyObject d) => (string)d.GetValue(PageIdProperty);
    public static void SetPageId(DependencyObject d, string value) => d.SetValue(PageIdProperty, value);

    private const int GlideMs = 220;
    private const int SettleMs = 200;

    private static readonly Dictionary<string, WeakReference<StackPanel>> Panels = new();
    private static readonly Dictionary<FrameworkElement, Thumb> Grips = new();
    private static ControlTemplate? _gripTemplate;
    private static DragSession? _drag;

    private sealed class DragSession
    {
        public StackPanel Panel = null!;
        public string PageId = "";
        public FrameworkElement Block = null!;
        public int OrigVisualIndex;
        public int OrigSlot;
        public int CurrentTargetSlot;
        public double StartX, StartY;
        public double LastX, LastY;
        public Point GrabOffset;
        public double GhostW;
        public double GhostH;
        public TranslateTransform Move = new();
        public ScaleTransform Zoom = new(1, 1);
        public ScrollViewer? Scroller;
        public DispatcherTimer? AutoTimer;
        public Window? Window;
        public List<FrameworkElement> VisibleBlocks = new();
        public Dictionary<FrameworkElement, double> BaseY = new();
        public Popup? PreviewPopup;
        public Border? PreviewBlock;
        public Popup? GhostPopup;
        public Border? GhostBorder;
        public Image? GhostImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    // ---------------- Setup ----------------

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StackPanel panel || !(bool)e.NewValue) return;
        if (panel.IsLoaded) InitPanel(panel);
        else panel.Loaded += (_, _) => InitPanel(panel);
    }

    private static void InitPanel(StackPanel panel)
    {
        string pageId = GetPageId(panel);
        if (string.IsNullOrEmpty(pageId)) return;
        lock (Panels) Panels[pageId] = new WeakReference<StackPanel>(panel);

        var s = AppSettings.Load();
        var current = CurrentOrder(panel);
        bool dirty = false;
        if (!s.BlockDefaults.ContainsKey(pageId))
        {
            s.BlockDefaults[pageId] = new List<string>(current);
            dirty = true;
        }
        else
        {
            // Append blocks that shipped after defaults were first captured.
            var def = s.BlockDefaults[pageId];
            foreach (var id in current)
            {
                if (!def.Contains(id)) { def.Add(id); dirty = true; }
            }
        }
        if (s.BlockOrders.TryGetValue(pageId, out var saved))
            ApplyOrder(panel, saved, animate: false);
        if (dirty) s.Save();

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (string.IsNullOrEmpty(GetBlockId(child))) continue;
            if (child is Panel wrapper && wrapper.Background == null)
                wrapper.Background = Brushes.Transparent; // hit-testable gaps for hover
            EnsureGrip(child);
            child.MouseEnter += (_, _) => ShowGrip(child);
            child.MouseLeave += (_, _) => HideGrip(child);
        }
    }

    private static List<string> CurrentOrder(StackPanel panel)
        => panel.Children.OfType<DependencyObject>()
            .Select(GetBlockId).Where(id => !string.IsNullOrEmpty(id)).ToList();

    /// <summary>Reorders panel children: saved ids first, unknown/new blocks appended.</summary>
    public static void ApplyOrder(StackPanel panel, IList<string> order, bool animate)
    {
        var byId = new Dictionary<string, FrameworkElement>();
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            var id = GetBlockId(child);
            if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id))
                byId[id] = child;
        }
        var seen = new HashSet<string>();
        var next = new List<FrameworkElement>();
        foreach (var id in order)
        {
            if (seen.Add(id) && byId.TryGetValue(id, out var el))
                next.Add(el);
        }
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            var id = GetBlockId(child);
            if (!string.IsNullOrEmpty(id) && !seen.Contains(id))
                next.Add(child);
        }
        // Children without BlockId are never dropped: pin them back at their
        // original positions so foreign content always survives a reorder.
        var orphans = new List<(int index, FrameworkElement el)>();
        int oi = 0;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (string.IsNullOrEmpty(GetBlockId(child)))
                orphans.Add((oi, child));
            oi++;
        }
        foreach (var (index, el) in orphans.OrderBy(x => x.index))
            next.Insert(Math.Min(index, next.Count), el);

        Dictionary<FrameworkElement, double>? before = null;
        if (animate && panel.IsVisible)
        {
            SnapTransforms(panel);
            before = next.Where(b => b.Visibility == Visibility.Visible)
                .ToDictionary(b => b, b => b.TranslatePoint(new Point(0, 0), panel).Y);
        }
        panel.Children.Clear();
        foreach (var el in next) panel.Children.Add(el);
        if (before != null)
        {
            foreach (var b in next)
            {
                if (b.Visibility != Visibility.Visible || !before.TryGetValue(b, out var y0)) continue;
                double d = y0 - b.TranslatePoint(new Point(0, 0), panel).Y;
                if (Math.Abs(d) > 0.5) GlideTo(GetTranslate(b), d);
            }
        }
    }

    /// <summary>Restores every registered page to its default order (animated).</summary>
    public static void ResetAll()
    {
        var s = AppSettings.Load();
        bool dirty = false;
        lock (Panels)
        {
            foreach (var (pageId, wref) in Panels.ToArray())
            {
                if (!wref.TryGetTarget(out var panel)) { Panels.Remove(pageId); continue; }
                if (!s.BlockDefaults.TryGetValue(pageId, out var defaults))
                    defaults = CurrentOrder(panel);
                ApplyOrder(panel, defaults, animate: true);
                s.BlockOrders[pageId] = new List<string>(defaults);
                dirty = true;
            }
        }
        if (dirty) s.Save();
    }

    private static void SaveOrder(string pageId, StackPanel panel)
    {
        try
        {
            var s = AppSettings.Load();
            s.BlockOrders[pageId] = CurrentOrder(panel);
            s.Save();
        }
        catch { }
    }

    // ---------------- Grip ----------------

    private static ControlTemplate GripTemplate()
    {
        if (_gripTemplate != null) return _gripTemplate;
        const string xaml =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'>" +
            "<Border CornerRadius='7' Padding='5,3'" +
            " Background='{DynamicResource BgElevatedBrush}'" +
            " BorderBrush='{DynamicResource BorderStrongBrush}' BorderThickness='1'>" +
            "<TextBlock Text='&#x22EE;&#x22EE;' FontSize='11' LineHeight='11'" +
            " Foreground='{DynamicResource TextMutedBrush}'" +
            " HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "</Border></ControlTemplate>";
        _gripTemplate = (ControlTemplate)XamlReader.Parse(xaml);
        return _gripTemplate;
    }

    private static void EnsureGrip(FrameworkElement block)
    {
        if (Grips.ContainsKey(block) || block is not Panel wrapper) return;
        Thumb grip;
        try
        {
            grip = new Thumb
            {
                Width = 28, Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Cursor = Cursors.SizeAll,
                Opacity = 0, IsHitTestVisible = false, Focusable = false,
                ToolTip = Loc.T("t_blocks_grip"),
                Template = GripTemplate()
            };
        }
        catch { return; }
        Panel.SetZIndex(grip, 20);
        wrapper.Children.Add(grip);
        Grips[block] = grip;
        grip.MouseEnter += (_, _) => ShowGrip(block);
        grip.PreviewMouseLeftButtonDown += (_, _) => BeginDragFromPointer(block);
        grip.PreviewMouseMove += (_, _) => TrackPointerDrag();
        grip.PreviewMouseLeftButtonUp += (_, _) => CommitDragIfActive(block);
        grip.DragStarted += (_, _) => BeginDrag(block);
        grip.DragCompleted += (_, _) => CommitDrag();
    }

    private static void ShowGrip(FrameworkElement block)
    {
        if (_drag != null && _drag.Block != block) return;
        if (!Grips.TryGetValue(block, out var grip)) return;
        grip.IsHitTestVisible = true;
        grip.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(grip.Opacity, 1, TimeSpan.FromMilliseconds(120)));
    }

    private static void HideGrip(FrameworkElement block)
    {
        if (!Grips.TryGetValue(block, out var grip)) return;
        if (_drag?.Block == block) return;
        if (grip.IsMouseCaptureWithin || Mouse.LeftButton == MouseButtonState.Pressed) return;
        var fade = new DoubleAnimation(grip.Opacity, 0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            if (_drag?.Block != block && Math.Abs(grip.Opacity) < 0.01)
                grip.IsHitTestVisible = false;
        };
        grip.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void HideOtherGrips(FrameworkElement activeBlock)
    {
        foreach (var (b, g) in Grips)
        {
            if (b != activeBlock)
            {
                g.BeginAnimation(UIElement.OpacityProperty, null);
                g.Opacity = 0;
                g.IsHitTestVisible = false;
            }
            else
            {
                g.BeginAnimation(UIElement.OpacityProperty, null);
                g.Opacity = 1;
                g.IsHitTestVisible = true;
            }
        }
    }

    // ---------------- Drag ----------------

    private static void BeginDragFromPointer(FrameworkElement block)
    {
        if (VisualTreeHelper.GetParent(block) is not StackPanel panel) return;
        BeginDrag(block, CurrentPointerPosition(panel));
    }

    private static void CommitDragIfActive(FrameworkElement block)
    {
        if (_drag?.Block == block)
            CommitDrag();
    }

    private static void BeginDrag(FrameworkElement block, Point? start = null)
    {
        if (_drag != null) return;
        if (VisualTreeHelper.GetParent(block) is not StackPanel panel) return;
        string pageId = GetPageId(panel);
        if (string.IsNullOrEmpty(pageId)) return;

        var visible = VisibleBlocks(panel);
        int origSlot = visible.IndexOf(block);
        if (origSlot < 0) return;

        var baseY = new Dictionary<FrameworkElement, double>();
        foreach (var b in visible)
        {
            try { baseY[b] = b.TranslatePoint(new Point(0, 0), panel).Y; }
            catch { baseY[b] = 0; }
        }

        var mouse = start ?? CurrentPointerPosition(panel);
        _drag = new DragSession
        {
            Panel = panel,
            PageId = pageId,
            Block = block,
            OrigVisualIndex = panel.Children.IndexOf(block),
            OrigSlot = origSlot,
            CurrentTargetSlot = origSlot,
            StartX = mouse.X,
            StartY = mouse.Y,
            LastX = mouse.X,
            LastY = mouse.Y,
            VisibleBlocks = visible,
            BaseY = baseY,
            Scroller = FindParent<ScrollViewer>(panel),
            Window = Window.GetWindow(panel)
        };

        HideOtherGrips(block);
        panel.CacheMode = null; // content cache would re-render on every mousemove

        // Ghost popup: the dragged card flies above everything (not clipped by ScrollViewer)
        // Original stays as a dim placeholder so the layout doesn't collapse.
        double w = Math.Max(1, block.ActualWidth);
        double h = Math.Max(1, block.ActualHeight);
        Point blockPos;
        try { blockPos = block.TranslatePoint(new Point(0, 0), panel); }
        catch { blockPos = new Point(0, _drag.BaseY.TryGetValue(block, out var by) ? by : 0); }
        _drag.GrabOffset = new Point(mouse.X - blockPos.X, mouse.Y - blockPos.Y);
        _drag.GhostW = w;
        _drag.GhostH = h;

        // Snapshot BEFORE dimming: live VisualBrush would follow Opacity=0 of the placeholder,
        // and Freeze() on an in-tree visual always throws (old empty-card fallback).
        FrameworkElement ghostContent;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(block);
            int pw = Math.Max(1, (int)Math.Ceiling(w * dpi.DpiScaleX));
            int ph = Math.Max(1, (int)Math.Ceiling(h * dpi.DpiScaleY));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pw, ph, dpi.DpiScaleX * 96.0, dpi.DpiScaleY * 96.0, System.Windows.Media.PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(block), null, new Rect(0, 0, w, h));
            }
            rtb.Render(dv);
            rtb.Freeze();
            var img = new Image
            {
                Source = rtb,
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };
            ghostContent = img;
        }
        catch
        {
            ghostContent = new Border { Width = w, Height = h, Background = ResourceBrush(panel, "BgCardBrush", Color.FromRgb(21,27,34), 1) };
        }

        // Invisible placeholder: keeps layout height so the panel doesn't jump,
        // but siblings may FLIP over this slot without a visual double-exposure.
        block.Opacity = 0;
        block.IsHitTestVisible = false;
        Panel.SetZIndex(block, 0);

        var ghostBorder = new Border
        {
            Width = w, Height = h,
            CornerRadius = new CornerRadius(10),
            BorderBrush = ResourceBrush(panel, "AccentBrush", Color.FromRgb(40,215,183), 0.9),
            BorderThickness = new Thickness(1.4),
            Background = Brushes.Transparent,
            Child = ghostContent,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Color.FromRgb(0,0,0)
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        _drag.GhostBorder = ghostBorder;
        _drag.Zoom = (ScaleTransform)ghostBorder.RenderTransform;

        var ghostPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = panel,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = ghostBorder,
            HorizontalOffset = mouse.X - _drag.GrabOffset.X,
            VerticalOffset = mouse.Y - _drag.GrabOffset.Y
        };
        _drag.GhostPopup = ghostPopup;
        ghostPopup.IsOpen = true;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _drag.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 1.02, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        _drag.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 1.02, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        ghostBorder.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        _drag.AutoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _drag.AutoTimer.Tick += (_, _) => DragTimerTick();
        _drag.AutoTimer.Start();
        if (_drag.Window != null)
            _drag.Window.PreviewKeyDown += EscHandler;

        CreateDropPreview(_drag);
        UpdateDropPreview(_drag);
    }

    private static void DragMoveTo(Point p)
    {
        var d = _drag;
        if (d == null) return;
        d.LastX = p.X;
        d.LastY = p.Y;
        if (d.GhostPopup != null)
        {
            d.GhostPopup.HorizontalOffset = p.X - d.GrabOffset.X;
            d.GhostPopup.VerticalOffset = p.Y - d.GrabOffset.Y;
        }
        else
        {
            d.Move.X = (p.X - d.StartX) * 0.25;
            d.Move.Y = p.Y - d.StartY;
        }
        UpdateSlot();
        UpdateDropPreview(d);
    }

    private static void CommitDrag() => EndDrag(commit: true);

    private static void CancelDrag() => EndDrag(commit: false);

    private static void EndDrag(bool commit)
    {
        var d = _drag;
        if (d == null) return;
        _drag = null;
        try { d.AutoTimer?.Stop(); } catch { }
        if (d.Window != null) d.Window.PreviewKeyDown -= EscHandler;
        RemoveDropPreview(d);

        // Remove ghost (top-most popup)
        Popup? ghost = d.GhostPopup;
        Border? ghostBorder = d.GhostBorder;
        d.GhostPopup = null;
        d.GhostBorder = null;
        d.GhostImage = null;
        if (ghost != null)
        {
            try { ghost.IsOpen = false; ghost.Child = null; } catch { }
        }

        // Restore original placeholder
        var block = d.Block;
        block.BeginAnimation(UIElement.OpacityProperty, null);
        block.Opacity = 1;
        block.IsHitTestVisible = true;
        block.RenderTransform = null;
        Panel.SetZIndex(block, 0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(SettleMs);

        if (commit && d.CurrentTargetSlot != d.OrigSlot)
        {
            var panel = d.Panel;
            panel.Children.Remove(block);

            int childIdx = panel.Children.Count;
            int vi = 0;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is FrameworkElement c &&
                    c.Visibility == Visibility.Visible && !string.IsNullOrEmpty(GetBlockId(c)))
                {
                    if (vi == d.CurrentTargetSlot) { childIdx = i; break; }
                    vi++;
                }
            }
            panel.Children.Insert(Math.Min(childIdx, panel.Children.Count), block);
            SaveOrder(d.PageId, panel);
        }
        else if (ghostBorder != null)
        {
            // Animate ghost back to original slot when cancelled (visual feedback)
            // ghost already removed, just settle original
        }

        // Settle siblings back
        foreach (var b in d.VisibleBlocks)
        {
            if (b == block) continue;
            var t = GetTranslate(b);
            AnimateTranslateY(t, 0, dur, ease);
        }
        // Fade ghost out if we had one (already closed, just no leftover)
        if (ghostBorder != null)
        {
            ghostBorder.BeginAnimation(UIElement.OpacityProperty, null);
        }

        if (Grips.TryGetValue(block, out var grip))
        {
            grip.Opacity = 1;
            grip.IsHitTestVisible = true;
        }

        ScrollPerf.RefreshAll(); // restore content cache
    }

    private static void EscHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag != null)
        {
            e.Handled = true;
            CancelDrag();
        }
    }

    // ---------------- Slots (FLIP) ----------------

    private static List<FrameworkElement> VisibleBlocks(StackPanel panel, FrameworkElement? exclude = null)
    {
        var list = new List<FrameworkElement>();
        foreach (var c in panel.Children.OfType<FrameworkElement>())
        {
            if (c == exclude) continue;
            if (c.Visibility != Visibility.Visible) continue;
            if (string.IsNullOrEmpty(GetBlockId(c))) continue;
            list.Add(c);
        }
        return list;
    }

    private static int VisibleIndexOf(StackPanel panel, FrameworkElement block)
    {
        int slot = 0;
        foreach (var c in panel.Children.OfType<FrameworkElement>())
        {
            if (c == block) break;
            if (c.Visibility == Visibility.Visible && !string.IsNullOrEmpty(GetBlockId(c))) slot++;
        }
        return slot;
    }

    private static void UpdateSlot()
    {
        var d = _drag;
        if (d == null) return;

        double currentCenterY;
        try
        {
            if (d.GhostPopup != null)
                currentCenterY = d.GhostPopup.VerticalOffset + d.GhostH / 2.0;
            else
                currentCenterY = d.BaseY[d.Block] + d.Move.Y + d.Block.ActualHeight / 2.0;
        }
        catch { return; }

        var others = d.VisibleBlocks.Where(b => b != d.Block).ToList();
        int newSlot = 0;
        foreach (var b in others)
        {
            double bCenterY = d.BaseY[b] + b.ActualHeight / 2.0;
            if (currentCenterY > bCenterY) newSlot++;
            else break;
        }

        if (newSlot != d.CurrentTargetSlot)
        {
            d.CurrentTargetSlot = newSlot;
            AnimateSlots(d);
            UpdateDropPreview(d);
        }
    }

    private static void AnimateSlots(DragSession d)
    {
        var others = d.VisibleBlocks.Where(b => b != d.Block).ToList();
        double draggedHeight = d.Block.ActualHeight;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(GlideMs);

        for (int i = 0; i < others.Count; i++)
        {
            var b = others[i];
            double targetOffset = 0;

            if (d.CurrentTargetSlot > d.OrigSlot)
            {
                if (i >= d.OrigSlot && i < d.CurrentTargetSlot)
                    targetOffset = -draggedHeight;
            }
            else if (d.CurrentTargetSlot < d.OrigSlot)
            {
                if (i >= d.CurrentTargetSlot && i < d.OrigSlot)
                    targetOffset = draggedHeight;
            }

            var t = GetTranslate(b);
            AnimateTranslateY(t, targetOffset, dur, ease);
        }
    }

    private static void CreateDropPreview(DragSession d)
    {
        try
        {
            d.PreviewBlock = new Border
            {
                IsHitTestVisible = false,
                Background = ResourceBrush(d.Panel, "BgCardBrush", Color.FromRgb(21, 27, 34), 0.24),
                BorderBrush = ResourceBrush(d.Panel, "AccentBrush", Color.FromRgb(40, 215, 183), 0.68),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Opacity = 0.62,
                Effect = null
            };

            d.PreviewPopup = new Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Placement = PlacementMode.Relative,
                PlacementTarget = d.Panel,
                Child = d.PreviewBlock,
                StaysOpen = true,
                IsOpen = true
            };
        }
        catch { }
    }

    private static void UpdateDropPreview(DragSession d)
    {
        if (d.PreviewPopup == null || d.PreviewBlock == null) return;

        try
        {
            double y = PreviewY(d);
            double x = 0;
            double width = d.Panel.ActualWidth;

            try
            {
                var blockPos = d.Block.TranslatePoint(new Point(0, 0), d.Panel);
                x = Math.Max(0, blockPos.X);
                if (d.Block.ActualWidth > 0)
                    width = Math.Min(d.Block.ActualWidth, Math.Max(0, d.Panel.ActualWidth - x));
            }
            catch { }

            Rect visible = VisiblePanelRect(d);
            const double minHeight = 18;
            const double minWidth = 32;

            x = Math.Clamp(x, visible.Left, Math.Max(visible.Left, visible.Right - minWidth));
            width = Math.Min(width, Math.Max(minWidth, visible.Right - x));

            double desiredHeight = Math.Max(24, d.Block.ActualHeight);
            double previewHeight = Math.Min(desiredHeight, visible.Height);
            y = Math.Clamp(y, visible.Top, Math.Max(visible.Top, visible.Bottom - previewHeight));
            previewHeight = Math.Min(previewHeight, Math.Max(minHeight, visible.Bottom - y));

            d.PreviewBlock.Width = Math.Max(1, width);
            d.PreviewBlock.Height = Math.Max(1, previewHeight);
            d.PreviewPopup.HorizontalOffset = x;
            d.PreviewPopup.VerticalOffset = y;
        }
        catch { }
    }

    private static Rect VisiblePanelRect(DragSession d)
    {
        double left = 0;
        double top = 0;
        double right = Math.Max(1, d.Panel.ActualWidth);
        double bottom = Math.Max(1, d.Panel.ActualHeight);

        if (d.Scroller != null && d.Scroller.ViewportWidth > 0 && d.Scroller.ViewportHeight > 0)
        {
            try
            {
                var tl = d.Scroller.TranslatePoint(new Point(0, 0), d.Panel);
                var br = d.Scroller.TranslatePoint(
                    new Point(d.Scroller.ViewportWidth, d.Scroller.ViewportHeight), d.Panel);

                left = Math.Max(left, tl.X);
                top = Math.Max(top, tl.Y);
                right = Math.Min(right, br.X);
                bottom = Math.Min(bottom, br.Y);
            }
            catch { }
        }

        if (right <= left) right = left + Math.Max(1, d.Panel.ActualWidth);
        if (bottom <= top) bottom = top + Math.Max(1, Math.Min(d.Panel.ActualHeight, d.Block.ActualHeight));

        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static double PreviewY(DragSession d)
    {
        var others = d.VisibleBlocks.Where(b => b != d.Block).ToList();
        if (others.Count == 0 || !d.BaseY.TryGetValue(d.Block, out var draggedY))
            return 0;

        if (d.CurrentTargetSlot == d.OrigSlot)
            return draggedY;

        FrameworkElement anchor;
        if (d.CurrentTargetSlot > d.OrigSlot)
        {
            int index = Math.Clamp(d.CurrentTargetSlot - 1, 0, others.Count - 1);
            anchor = others[index];
        }
        else
        {
            int index = Math.Clamp(d.CurrentTargetSlot, 0, others.Count - 1);
            anchor = others[index];
        }

        return d.BaseY.TryGetValue(anchor, out var y) ? y : draggedY;
    }

    private static void RemoveDropPreview(DragSession d)
    {
        try
        {
            if (d.PreviewPopup != null)
            {
                d.PreviewPopup.IsOpen = false;
                d.PreviewPopup.Child = null;
            }
        }
        catch { }
        d.PreviewPopup = null;
        d.PreviewBlock = null;
    }

    private static Brush ResourceBrush(FrameworkElement owner, string key, Color fallback, double opacity)
    {
        Brush brush = owner.TryFindResource(key) is Brush resource
            ? resource.CloneCurrentValue()
            : new SolidColorBrush(fallback);
        brush.Opacity = opacity;
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static void AnimateTranslateY(TranslateTransform t, double toY, TimeSpan dur, EasingFunctionBase ease)
    {
        double fromY = t.Y;
        t.BeginAnimation(TranslateTransform.YProperty, null);

        var anim = new DoubleAnimation(fromY, toY, dur) { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.Y = toY;
        };
        t.BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateTranslateX(TranslateTransform t, double toX, TimeSpan dur, EasingFunctionBase ease)
    {
        double fromX = t.X;
        t.BeginAnimation(TranslateTransform.XProperty, null);

        var anim = new DoubleAnimation(fromX, toX, dur) { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.X = toX;
        };
        t.BeginAnimation(TranslateTransform.XProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private static void GlideTo(TranslateTransform t, double fromY, TimeSpan? dur = null, EasingFunctionBase? ease = null)
    {
        t.BeginAnimation(TranslateTransform.YProperty, null);
        t.Y = 0;
        t.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, dur ?? TimeSpan.FromMilliseconds(GlideMs))
            { EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static TranslateTransform GetTranslate(UIElement el)
    {
        switch (el.RenderTransform)
        {
            case TranslateTransform t: return t;
            case TransformGroup g:
                foreach (var c in g.Children)
                    if (c is TranslateTransform tt) return tt;
                var nt = new TranslateTransform();
                g.Children.Add(nt);
                return nt;
            default:
                var t2 = new TranslateTransform();
                el.RenderTransform = t2;
                return t2;
        }
    }

    private static void SnapTransforms(StackPanel panel)
    {
        foreach (var c in panel.Children.OfType<FrameworkElement>())
        {
            switch (c.RenderTransform)
            {
                case TranslateTransform t:
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                    t.X = 0; t.Y = 0;
                    break;
                case TransformGroup g:
                    foreach (var ch in g.Children)
                    {
                        switch (ch)
                        {
                            case TranslateTransform tt:
                                tt.BeginAnimation(TranslateTransform.XProperty, null);
                                tt.BeginAnimation(TranslateTransform.YProperty, null);
                                tt.X = 0; tt.Y = 0;
                                break;
                            case ScaleTransform st:
                                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                                st.ScaleX = 1; st.ScaleY = 1;
                                break;
                        }
                    }
                    break;
            }
            c.BeginAnimation(UIElement.OpacityProperty, null);
            c.Opacity = 1;
        }
    }

    // ---------------- Auto-scroll ----------------

    private static void AutoScrollTick()
    {
        var d = _drag;
        if (d?.Scroller == null) return;
        Point p;
        try { p = Mouse.GetPosition(d.Scroller); }
        catch { return; }
        const double edge = 70, step = 16;
        if (p.Y < edge)
            d.Scroller.ScrollToVerticalOffset(Math.Max(0, d.Scroller.VerticalOffset - step));
        else if (p.Y > d.Scroller.ViewportHeight - edge)
            d.Scroller.ScrollToVerticalOffset(Math.Min(d.Scroller.ScrollableHeight, d.Scroller.VerticalOffset + step));
        else return;
        UpdateSlot();
    }

    private static void DragTimerTick()
    {
        if (_drag == null) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CommitDrag();
            return;
        }

        TrackPointerDrag();
        AutoScrollTick();
    }

    private static void TrackPointerDrag()
    {
        var d = _drag;
        if (d == null) return;

        try { DragMoveTo(CurrentPointerPosition(d.Panel)); }
        catch { }
    }

    private static Point CurrentPointerPosition(StackPanel panel)
    {
        if (GetCursorPos(out var p))
            return panel.PointFromScreen(new Point(p.X, p.Y));
        return Mouse.GetPosition(panel);
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
