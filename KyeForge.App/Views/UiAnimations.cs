using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace KyeForge.App.Views;

/// <summary>Shared popup entrance/exit animations (fade + slide), matching page transitions.</summary>
public static class UiAnimations
{
    public static void FadeSlideIn(Window window)
    {
        void Run()
        {
            try
            {
                if (window.Content is FrameworkElement content)
                {
                    content.Opacity = 0;
                    var shift = new TranslateTransform(0, 12);
                    content.RenderTransform = shift;

                    var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
                    var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };

                    fade.Completed += (_, _) =>
                    {
                        content.BeginAnimation(UIElement.OpacityProperty, null);
                        content.Opacity = 1;
                    };
                    slide.Completed += (_, _) =>
                    {
                        shift.BeginAnimation(TranslateTransform.YProperty, null);
                        shift.Y = 0;
                    };

                    content.BeginAnimation(UIElement.OpacityProperty, fade);
                    shift.BeginAnimation(TranslateTransform.YProperty, slide);
                }
            }
            catch
            {
                if (window.Content is FrameworkElement fe) fe.Opacity = 1;
            }
        }

        if (window.IsLoaded) Run();
        else window.Loaded += (_, _) => Run();
    }

    /// <summary>
    /// Plays a fade-out + slide-down animation on the window content,
    /// then actually closes the window. Call this instead of setting DialogResult directly.
    /// </summary>
    public static void AnimatedClose(Window window, bool? dialogResult)
    {
        // Guard against double-close
        if (window.Tag is "closing") return;
        window.Tag = "closing";

        try
        {
            if (window.Content is FrameworkElement content)
            {
                var shift = content.RenderTransform as TranslateTransform ?? new TranslateTransform(0, 0);
                content.RenderTransform = shift;

                var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
                var fade = new DoubleAnimation(content.Opacity, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
                var slide = new DoubleAnimation(shift.Y, 10, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };

                fade.Completed += (_, _) =>
                {
                    try { window.DialogResult = dialogResult; }
                    catch { try { window.Close(); } catch { } }
                };

                content.BeginAnimation(UIElement.OpacityProperty, fade);
                shift.BeginAnimation(TranslateTransform.YProperty, slide);
                return;
            }
        }
        catch { }

        // Fallback: close immediately
        try { window.DialogResult = dialogResult; }
        catch { try { window.Close(); } catch { } }
    }
}
