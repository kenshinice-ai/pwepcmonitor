namespace Pwe.PcMonitor.Models;

public enum HealthState
{
    Calm = 0,
    Warm = 1,
    Hot = 2
}

public static class HealthRules
{
    public static HealthState Grade(double? value, double warm, double hot)
    {
        if (value is null || double.IsNaN(value.Value)) return HealthState.Calm;
        return value >= hot ? HealthState.Hot : value >= warm ? HealthState.Warm : HealthState.Calm;
    }

    public static HealthState Max(params HealthState[] states) => states.Max();

    public static HealthState Temperature(double? celsius, bool storage = false) =>
        Grade(celsius, storage ? 55 : 75, storage ? 68 : 92);

    public static HealthState Utilization(double percent) => Grade(percent, 55, 85);

    public static HealthState Capacity(double percent) => Grade(percent, 85, 95);
}
