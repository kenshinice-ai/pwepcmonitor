namespace Pwe.PcMonitor.Models;

public sealed record FanReading(string Name, double? Rpm, double? Percent = null)
{
    public string DisplayValue => Rpm is >= 0
        ? $"{Rpm:0} rpm"
        : Percent is double percent && percent >= 0
            ? $"{percent:0}%"
            : "—";
}

public sealed record ProcessReading(string Name, double CpuPercent, long MemoryBytes);

public sealed record SensorReading(string Group, string Name, string Type, double Value, string Unit);

public sealed record CoreReading(string Name, double LoadPercent, double? ClockMhz)
{
    public double BarHeight => Math.Clamp(LoadPercent, 2, 100) * 0.42;
}

public sealed record SystemSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string ProcessorName { get; init; } = "Windows PC";
    public string GpuName { get; init; } = "GPU";
    public string SensorStatus { get; init; } = "Basic Windows metrics";
    public string TemperatureStatus { get; init; } = "Temperature channels unavailable";
    public string GpuTemperatureSource { get; init; } = "GPU provider not detected";
    public TimeSpan Uptime { get; init; }

    public double CpuUsage { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuTemperature { get; init; }
    public double? CpuTemperatureMax { get; init; }
    public double? CpuPowerWatts { get; init; }

    public double? GpuUsage { get; init; }
    public double? GpuClockMhz { get; init; }
    public double? GpuTemperature { get; init; }
    public double? GpuPowerWatts { get; init; }

    public ulong MemoryTotal { get; init; }
    public ulong MemoryAvailable { get; init; }
    public ulong MemoryUsed => MemoryTotal > MemoryAvailable ? MemoryTotal - MemoryAvailable : 0;
    public double MemoryUsedPercent => MemoryTotal == 0 ? 0 : MemoryUsed * 100d / MemoryTotal;

    public string DiskName { get; init; } = "System drive";
    public long DiskTotal { get; init; }
    public long DiskFree { get; init; }
    public double DiskUsedPercent => DiskTotal <= 0 ? 0 : (DiskTotal - DiskFree) * 100d / DiskTotal;
    public double? DiskReadBytesPerSecond { get; init; }
    public double? DiskWriteBytesPerSecond { get; init; }
    public double? DiskTemperature { get; init; }
    public double? MotherboardTemperature { get; init; }
    public double? MotherboardTemperatureMax { get; init; }

    public string NetworkName { get; init; } = "Network";
    public string IpAddress { get; init; } = "—";
    public double NetworkDownBytesPerSecond { get; init; }
    public double NetworkUpBytesPerSecond { get; init; }

    public bool HasBattery { get; init; }
    public int BatteryPercent { get; init; }
    public bool BatteryCharging { get; init; }
    public bool BatteryOnAc { get; init; }

    public IReadOnlyList<CoreReading> Cores { get; init; } = [];
    public IReadOnlyList<FanReading> Fans { get; init; } = [];
    public IReadOnlyList<ProcessReading> Processes { get; init; } = [];
    public IReadOnlyList<SensorReading> Sensors { get; init; } = [];

    public HealthState CpuHealth => HealthRules.Max(
        HealthRules.Utilization(CpuUsage),
        HealthRules.Temperature(CpuTemperatureMax ?? CpuTemperature));
    public HealthState GpuHealth => HealthRules.Max(
        HealthRules.Utilization(GpuUsage ?? 0),
        HealthRules.Temperature(GpuTemperature));
    public HealthState MemoryHealth => HealthRules.Capacity(MemoryUsedPercent);
    public HealthState DiskHealth => HealthRules.Max(
        HealthRules.Capacity(DiskUsedPercent),
        HealthRules.Temperature(DiskTemperature, storage: true));
    public HealthState MotherboardHealth => HealthRules.Temperature(MotherboardTemperatureMax ?? MotherboardTemperature);
    public HealthState OverallHealth => HealthRules.Max(
        HealthRules.Temperature(CpuTemperatureMax ?? CpuTemperature),
        HealthRules.Temperature(GpuTemperature),
        MotherboardHealth,
        MemoryHealth,
        DiskHealth);
}
