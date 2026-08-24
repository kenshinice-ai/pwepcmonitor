using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Pwe.PcMonitor.Models;

namespace Pwe.PcMonitor.Converters;

public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var state = value is HealthState health ? health : HealthState.Calm;
        var key = state switch { HealthState.Hot => "HotBrush", HealthState.Warm => "AccentBrush", _ => "TextBrush" };
        return System.Windows.Application.Current.Resources[key] as Brush ?? Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class HealthToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is HealthState.Hot ? "BadgeHotBrush" : value is HealthState.Warm ? "BadgeWarmBrush" : "RailBrush";
        return System.Windows.Application.Current.Resources[key] as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        long number when number >= 0 => ViewModels.MonitorViewModel.FormatBytes((ulong)number),
        ulong number => ViewModels.MonitorViewModel.FormatBytes(number),
        _ => "—"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SensorValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double value) return "—";
        var unit = values[1]?.ToString() ?? string.Empty;
        return unit switch
        {
            "%" or "rpm" or "MHz" => $"{value:0} {unit}",
            "°C" or "W" or "V" or "A" => $"{value:0.0} {unit}",
            "B/s" => ViewModels.MonitorViewModel.FormatRate(value),
            _ => string.IsNullOrWhiteSpace(unit) ? $"{value:0.##}" : $"{value:0.##} {unit}"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}
