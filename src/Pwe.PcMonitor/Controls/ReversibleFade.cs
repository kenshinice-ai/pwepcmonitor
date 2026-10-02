using System.Windows;
using System.Windows.Media.Animation;

namespace Pwe.PcMonitor.Controls;

/// <summary>Reverses from the displayed opacity; old completions cannot close a reopened panel.</summary>
public sealed class ReversibleFade(FrameworkElement surface)
{
    private int _generation;

    public void To(bool visible, bool animate, Action? completed = null)
    {
        var generation = ++_generation;
        var current = surface.Opacity;
        var target = visible ? 1d : 0d;
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.Opacity = target;
        if (!animate || Math.Abs(current - target) < 0.001)
        {
            completed?.Invoke();
            return;
        }

        var transition = new DoubleAnimation(current, target,
            TimeSpan.FromMilliseconds(140 * Math.Abs(current - target)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        transition.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            surface.BeginAnimation(UIElement.OpacityProperty, null);
            surface.Opacity = target;
            completed?.Invoke();
        };
        surface.BeginAnimation(UIElement.OpacityProperty, transition, HandoffBehavior.SnapshotAndReplace);
    }
}
