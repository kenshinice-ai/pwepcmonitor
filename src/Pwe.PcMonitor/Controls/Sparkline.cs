using System.Windows;
using System.Windows.Media;

namespace Pwe.PcMonitor.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(System.Windows.Media.Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CeilingProperty = DependencyProperty.Register(
        nameof(Ceiling), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public System.Windows.Media.Brush Stroke
    {
        get => (System.Windows.Media.Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Ceiling
    {
        get => (double)GetValue(CeilingProperty);
        set => SetValue(CeilingProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var rail = System.Windows.Application.Current.Resources["RailBrush"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        drawingContext.DrawLine(new Pen(rail, 1), new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));
        if (Values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var points = new StreamGeometry();
        using (var context = points.Open())
        {
            for (var index = 0; index < Values.Count; index++)
            {
                var x = ActualWidth * index / Math.Max(1, Values.Count - 1);
                var normalized = Math.Clamp(Values[index] / Math.Max(0.001, Ceiling), 0, 1);
                var y = ActualHeight - normalized * (ActualHeight - 2) - 1;
                if (index == 0) context.BeginFigure(new Point(x, y), false, false);
                else context.LineTo(new Point(x, y), true, false);
            }
        }
        points.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 1.5), points);
    }
}
