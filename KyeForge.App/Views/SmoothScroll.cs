using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KyeForge.App.Views;

/// <summary>
/// Fast smooth scrolling without inertia float or restart spikes.
///
/// One window-level wheel handler finds the ScrollViewer under the cursor.
/// Classic mouse-wheel notches accumulate a target offset; a single render
/// pump (CompositionTarget.Rendering) eases the actual offset toward it with
/// exponential smoothing. No animation objects are created per tick, so fast
/// flicks cannot spike the velocity by restarting an EaseOut curve - the
/// motion stays continuous. The pump runs at the display's vsync rate and
/// unsubscribes itself when everything settles.
/// High-resolution input (touchpad) follows the finger 1:1 with no lag.
/// When the inner scroller is pinned at the edge, the outer one takes over.
/// </summary>
public static class SmoothScroll
{
    private const double StepPx = 110;   // pixels per wheel notch
    private const double Smoothing = 0.22; // per-frame ease factor (frame-rate independent feel)
    private const double SettlePx = 0.5;

    private sealed class ScrollState
    {
        public double TargetY;
        public double TargetX;
        public bool HasY;
        public bool HasX;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();
    private static readonly ConditionalWeakTable<Window, object> AttachedWindows = new();
    private static readonly ConditionalWeakTable<ScrollViewer, object> AttachedViewers = new();
    private static readonly HashSet<ScrollViewer> Active = new();
    private static bool _pumpRunning;

    public static void Attach(Window window)
    {
        if (AttachedWindows.TryGetValue(window, out _)) return;
        AttachedWindows.Add(window, new object());
        window.PreviewMouseWheel += OnWindowPreviewMouseWheel;
        window.Closed += (_, _) => window.PreviewMouseWheel -= OnWindowPreviewMouseWheel;
    }

    /// <summary>
    /// Per-viewer variant for ScrollViewers outside the main visual tree
    /// (e.g. ComboBox dropdown popups). Idempotent.
    /// </summary>
    public static void AttachViewer(ScrollViewer sv)
    {
        if (AttachedViewers.TryGetValue(sv, out _)) return;
        AttachedViewers.Add(sv, new object());
        sv.PreviewMouseWheel += OnWindowPreviewMouseWheel;
        sv.Unloaded += OnViewerUnloaded;
    }

    private static void OnViewerUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.PreviewMouseWheel -= OnWindowPreviewMouseWheel;
            sv.Unloaded -= OnViewerUnloaded;
            AttachedViewers.Remove(sv);
        }
    }

    private static void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        if (e.OriginalSource is not DependencyObject origin) return;

        bool wantHorizontal = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        double notches = e.Delta / 120.0;

        var found = FindScroller(origin, notches, wantHorizontal);
        if (found == null) return;
        var (sv, horizontal) = found.Value;

        if (Math.Abs(e.Delta) < 120)
            ScrollDirect(sv, e.Delta, horizontal);
        else
            ScrollToward(sv, notches, horizontal);
        e.Handled = true;
    }

    /// <summary>
    /// Innermost scrollable-in-that-direction scroller first; if it is pinned
    /// at the edge in the scroll direction, fall through to the outer one.
    /// </summary>
    private static (ScrollViewer Sv, bool Horizontal)? FindScroller(
        DependencyObject origin, double notches, bool wantHorizontal)
    {
        DependencyObject? current = origin;
        while (current != null)
        {
            if (current is ScrollViewer sv)
            {
                bool horizontal = wantHorizontal
                    && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled
                    && sv.ScrollableWidth > 0;
                double range = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
                if (range > 0)
                {
                    double actual = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
                    bool pinnedTop = actual <= 0.5 && notches > 0;
                    bool pinnedBottom = actual >= range - 0.5 && notches < 0;
                    if (!pinnedTop && !pinnedBottom)
                        return (sv, horizontal);
                }
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void ScrollDirect(ScrollViewer sv, int delta, bool horizontal)
    {
        var state = States.GetOrCreateValue(sv);
        if (horizontal)
        {
            if (sv.ScrollableWidth <= 0) return;
            double target = Math.Clamp(sv.HorizontalOffset - delta, 0, sv.ScrollableWidth);
            sv.ScrollToHorizontalOffset(target);
            state.TargetX = target;
            state.HasX = true;
        }
        else
        {
            if (sv.ScrollableHeight <= 0) return;
            double target = Math.Clamp(sv.VerticalOffset - delta, 0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(target);
            state.TargetY = target;
            state.HasY = true;
        }
    }

    private static void ScrollToward(ScrollViewer sv, double notches, bool horizontal)
    {
        var state = States.GetOrCreateValue(sv);
        if (horizontal)
        {
            double range = sv.ScrollableWidth;
            if (range <= 0) return;
            if (!state.HasX || Math.Abs(sv.HorizontalOffset - state.TargetX) > StepPx)
                state.TargetX = sv.HorizontalOffset;
            state.HasX = true;
            state.TargetX = Math.Clamp(state.TargetX - notches * StepPx, 0, range);
        }
        else
        {
            double range = sv.ScrollableHeight;
            if (range <= 0) return;
            if (!state.HasY || Math.Abs(sv.VerticalOffset - state.TargetY) > StepPx)
                state.TargetY = sv.VerticalOffset;
            state.HasY = true;
            state.TargetY = Math.Clamp(state.TargetY - notches * StepPx, 0, range);
        }
        WakePump(sv);
    }

    private static void WakePump(ScrollViewer sv)
    {
        lock (Active) Active.Add(sv);
        if (!_pumpRunning)
        {
            _pumpRunning = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        ScrollViewer[] snapshot;
        lock (Active)
        {
            if (Active.Count == 0)
            {
                CompositionTarget.Rendering -= OnRendering;
                _pumpRunning = false;
                return;
            }
            snapshot = Active.ToArray();
        }

        foreach (var sv in snapshot)
        {
            try
            {
                if (!sv.IsLoaded)
                {
                    lock (Active) Active.Remove(sv);
                    continue;
                }
                var state = States.GetOrCreateValue(sv);
                bool doneY = EaseAxis(sv, state, horizontal: false);
                bool doneX = EaseAxis(sv, state, horizontal: true);
                if (doneY && doneX)
                {
                    lock (Active) Active.Remove(sv);
                }
            }
            catch
            {
                lock (Active) Active.Remove(sv);
            }
        }
    }

    private static bool EaseAxis(ScrollViewer sv, ScrollState state, bool horizontal)
    {
        bool has = horizontal ? state.HasX : state.HasY;
        if (!has) return true;
        double range = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
        if (range <= 0) return true;

        double target = horizontal ? state.TargetX : state.TargetY;
        target = Math.Clamp(target, 0, range);
        double actual = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
        double next = actual + (target - actual) * Smoothing;
        if (Math.Abs(target - next) < SettlePx)
        {
            next = target;
            if (horizontal) sv.ScrollToHorizontalOffset(next);
            else sv.ScrollToVerticalOffset(next);
            return true;
        }
        if (horizontal) sv.ScrollToHorizontalOffset(next);
        else sv.ScrollToVerticalOffset(next);
        return false;
    }
}
