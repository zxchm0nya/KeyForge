using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        public double StartX, StartY;
        public TranslateTransform Move = new();
        public ScaleTransform Zoom = new(1, 1);
        public ScrollViewer? Scroller;
        public DispatcherTimer? AutoTimer;
        public Window? Window;
    }

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
                Width = 26, Height = 26,
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
        Panel.SetZIndex(grip, 10);
        wrapper.Children.Add(grip);
        Grips[block] = grip;
        grip.DragStarted += (_, _) => BeginDrag(block);
        grip.DragDelta += (_, e) => DragMove(e.HorizontalChange, e.VerticalChange);
        grip.DragCompleted += (_, _) => CommitDrag();
    }

    private static void ShowGrip(FrameworkElement block)
    {
        if (_drag != null || !Grips.TryGetValue(block, out var grip)) return;
        grip.IsHitTestVisible = true;
        grip.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(grip.Opacity, 1, TimeSpan.FromMilliseconds(120)));
    }

    private static void HideGrip(FrameworkElement block)
    {
        if (!Grips.TryGetValue(block, out var grip)) return;
        if (_drag?.Block == block) return;
        var fade = new DoubleAnimation(grip.Opacity, 0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            if (_drag?.Block != block && Math.Abs(grip.Opacity) < 0.01)
                grip.IsHitTestVisible = false;
        };
        grip.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void HideAllGrips()
    {
        foreach (var g in Grips.Values)
        {
            g.BeginAnimation(UIElement.OpacityProperty, null);
            g.Opacity = 0;
            g.IsHitTestVisible = false;
        }
    }

    // ---------------- Drag ----------------

    private static void BeginDrag(FrameworkElement block)
    {
        if (_drag != null) return;
        if (VisualTreeHelper.GetParent(block) is not StackPanel panel) return;
        string pageId = GetPageId(panel);
        if (string.IsNullOrEmpty(pageId)) return;

        var mouse = Mouse.GetPosition(panel);
        _drag = new DragSession
        {
            Panel = panel, PageId = pageId, Block = block,
            OrigVisualIndex = panel.Children.IndexOf(block),
            StartX = mouse.X, StartY = mouse.Y,
            Scroller = FindParent<ScrollViewer>(panel),
            Window = Window.GetWindow(panel)
        };

        HideAllGrips();
        panel.CacheMode = null; // content cache would re-render on every mousemove
        Panel.SetZIndex(block, 50);

        var group = new TransformGroup();
        _drag.Zoom = new ScaleTransform(1, 1);
        _drag.Move = new TranslateTransform(0, 0);
        group.Children.Add(_drag.Zoom);
        group.Children.Add(_drag.Move);
        block.RenderTransformOrigin = new Point(0.5, 0);
        block.RenderTransform = group;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _drag.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        _drag.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 1.03, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        block.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(block.Opacity, 0.96, TimeSpan.FromMilliseconds(140)));

        _drag.AutoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _drag.AutoTimer.Tick += (_, _) => AutoScrollTick();
        _drag.AutoTimer.Start();
        if (_drag.Window != null)
            _drag.Window.PreviewKeyDown += EscHandler;
    }

    private static void DragMove(double dx, double dy)
    {
        var d = _drag;
        if (d == null) return;
        d.Move.X += dx;
        d.Move.Y += dy;
        UpdateSlot();
    }

    private static void CommitDrag() => EndDrag(commit: true);

    private static void CancelDrag()
    {
        var d = _drag;
        if (d == null) return;
        // glide back to the original position, then settle
        MoveBlockToVisualIndex(d, d.OrigVisualIndex);
        EndDrag(commit: true);
    }

    private static void MoveBlockToVisualIndex(DragSession d, int visualIndex)
    {
        int slot = 0;
        int limit = Math.Min(visualIndex, d.Panel.Children.Count);
        for (int i = 0; i < limit; i++)
        {
            if (d.Panel.Children[i] is FrameworkElement c && c != d.Block &&
                c.Visibility == Visibility.Visible && !string.IsNullOrEmpty(GetBlockId(c)))
                slot++;
        }
        MoveBlockToVisibleSlot(d, slot);
    }

    private static void EndDrag(bool commit)
    {
        var d = _drag;
        if (d == null) return;
        _drag = null;
        try { if (d.AutoTimer != null) d.AutoTimer.Stop(); } catch { }
        if (d.Window != null) d.Window.PreviewKeyDown -= EscHandler;
        Panel.SetZIndex(d.Block, 0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(SettleMs);
        d.Move.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(d.Move.X, 0, dur) { EasingFunction = ease });
        d.Move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(d.Move.Y, 0, dur) { EasingFunction = ease });
        d.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(d.Zoom.ScaleX, 1, dur) { EasingFunction = ease });
        d.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(d.Zoom.ScaleY, 1, dur) { EasingFunction = ease });
        d.Block.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(d.Block.Opacity, 1, dur));

        SaveOrder(d.PageId, d.Panel);
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
        double y;
        try { y = Mouse.GetPosition(d.Panel).Y; }
        catch { return; }
        var vis = VisibleBlocks(d.Panel, d.Block);
        int slot = 0;
        foreach (var b in vis)
        {
            double mid;
            try { mid = b.TranslatePoint(new Point(0, 0), d.Panel).Y + b.ActualHeight / 2; }
            catch { continue; }
            if (y > mid) slot++;
            else break;
        }
        int cur = VisibleIndexOf(d.Panel, d.Block);
        if (slot != cur) MoveBlockToVisibleSlot(d, slot);
    }

    private static void MoveBlockToVisibleSlot(DragSession d, int slot)
    {
        var panel = d.Panel;
        var block = d.Block;
        var before = new Dictionary<FrameworkElement, double>();
        foreach (var b in VisibleBlocks(panel, block))
        {
            try { before[b] = b.TranslatePoint(new Point(0, 0), panel).Y; }
            catch { }
        }

        panel.Children.Remove(block);
        int childIdx = panel.Children.Count, vi = 0;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is FrameworkElement c &&
                c.Visibility == Visibility.Visible && !string.IsNullOrEmpty(GetBlockId(c)))
            {
                if (vi == slot) { childIdx = i; break; }
                vi++;
            }
        }
        panel.Children.Insert(Math.Min(childIdx, panel.Children.Count), block);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(GlideMs);
        foreach (var b in VisibleBlocks(panel, block))
        {
            if (!before.TryGetValue(b, out var y0)) continue;
            double after;
            try { after = b.TranslatePoint(new Point(0, 0), panel).Y; }
            catch { continue; }
            double delta = y0 - after;
            if (Math.Abs(delta) > 0.5)
                GlideTo(GetTranslate(b), delta, dur, ease);
        }
    }

    private static void GlideTo(TranslateTransform t, double fromY, TimeSpan? dur = null, EasingFunctionBase? ease = null)
    {
        t.BeginAnimation(TranslateTransform.YProperty, null);
        t.Y = fromY;
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
