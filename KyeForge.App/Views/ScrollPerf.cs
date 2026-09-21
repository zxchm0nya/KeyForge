using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KyeForge.App.Views;

/// <summary>
/// Makes scrolling cheap: caches every ScrollViewer's content as a GPU bitmap
/// (at the current DPI scale, so text stays crisp) so a scroll frame blits the
/// cache instead of re-rasterizing all text and vectors. The cache invalidates
/// itself whenever the content actually changes (typing, toggles, sliders).
/// </summary>
public static class ScrollPerf
{
    private static readonly List<WeakReference<FrameworkElement>> Cached = new();

    public static void CacheScrollContents(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is ScrollViewer sv
                && sv.Content is FrameworkElement fe
                && fe.CacheMode == null)
                CacheContent(fe);
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
    }

    public static void RefreshAll()
    {
        lock (Cached)
        {
            foreach (var wr in Cached.ToArray())
            {
                if (wr.TryGetTarget(out var fe) && fe.IsLoaded)
                    Apply(fe);
                else
                    Cached.Remove(wr);
            }
        }
    }

    private static void CacheContent(FrameworkElement fe)
    {
        lock (Cached) Cached.Add(new WeakReference<FrameworkElement>(fe));
        if (fe.IsLoaded) Apply(fe);
        else fe.Loaded += (_, _) => Apply(fe);
    }

    private static void Apply(FrameworkElement fe)
    {
        try
        {
            double scale = 1.0;
            try { scale = VisualTreeHelper.GetDpi(fe).DpiScaleX; } catch { }
            fe.CacheMode = new BitmapCache(Math.Clamp(scale, 1.0, 4.0));
        }
        catch { }
    }
}
