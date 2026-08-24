using LibreHardwareMonitor.Hardware;
using Pwe.PcMonitor.Models;

namespace Pwe.PcMonitor.Services;

public sealed class SystemSampler : IDisposable
{
    private readonly WindowsMetricsReader _windows = new();
    private readonly bool _enableEnhancedSensors;
    private Computer? _computer;
    private bool _hardwareAttempted;
    private bool _hardwareInventoryLogged;
    private bool _pawnIoInstalled;
    private bool _isElevated;
    private string _hardwareStatus = "Basic Windows metrics";

    public SystemSampler(bool enableEnhancedSensors = true)
    {
        _enableEnhancedSensors = enableEnhancedSensors;
        if (!enableEnhancedSensors) _hardwareStatus = "Basic metrics only · safe mode";
    }

    public SystemSnapshot Sample()
    {
        try
        {
            var basic = _windows.Read();
            var hardware = _enableEnhancedSensors ? ReadHardware() : new HardwareMetrics();

            return new SystemSnapshot
            {
                Timestamp = DateTimeOffset.Now,
                MachineName = Environment.MachineName,
                ProcessorName = hardware.ProcessorName ?? basic.ProcessorName,
                GpuName = hardware.GpuName ?? "GPU",
                SensorStatus = _hardwareStatus,
                Uptime = basic.Uptime,
                CpuUsage = hardware.CpuUsage ?? basic.CpuUsage,
                CpuClockMhz = hardware.CpuClockMhz,
                CpuTemperature = hardware.CpuTemperature,
                CpuTemperatureMax = hardware.CpuTemperatureMax,
                CpuPowerWatts = hardware.CpuPowerWatts,
                GpuUsage = hardware.GpuUsage,
                GpuClockMhz = hardware.GpuClockMhz,
                GpuTemperature = hardware.GpuTemperature,
                GpuPowerWatts = hardware.GpuPowerWatts,
                MemoryTotal = basic.MemoryTotal,
                MemoryAvailable = basic.MemoryAvailable,
                DiskName = hardware.StorageName ?? basic.DiskName,
                DiskTotal = basic.DiskTotal,
                DiskFree = basic.DiskFree,
                DiskReadBytesPerSecond = hardware.DiskReadBytesPerSecond,
                DiskWriteBytesPerSecond = hardware.DiskWriteBytesPerSecond,
                DiskTemperature = hardware.DiskTemperature,
                MotherboardTemperature = hardware.MotherboardTemperature,
                MotherboardTemperatureMax = hardware.MotherboardTemperatureMax,
                NetworkName = basic.NetworkName,
                IpAddress = basic.IpAddress,
                NetworkDownBytesPerSecond = basic.NetworkDown,
                NetworkUpBytesPerSecond = basic.NetworkUp,
                HasBattery = basic.HasBattery,
                BatteryPercent = basic.BatteryPercent,
                BatteryCharging = basic.BatteryCharging,
                BatteryOnAc = basic.BatteryOnAc,
                Processes = basic.Processes,
                Cores = hardware.Cores,
                Fans = hardware.Fans,
                Sensors = hardware.Sensors
            };
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("System sample failed", exception);
            return new SystemSnapshot { SensorStatus = "Basic metrics temporarily unavailable" };
        }
    }

    private HardwareMetrics ReadHardware()
    {
        EnsureHardware();
        if (_computer is null) return new HardwareMetrics();

        try
        {
            _computer.Accept(new UpdateVisitor());
            var allHardware = Flatten(_computer.Hardware).ToArray();
            if (!_hardwareInventoryLogged)
            {
                var inventory = string.Join(" | ", allHardware.Select(item => $"{item.HardwareType}:{item.Name}[{item.Sensors.Count()}]").Take(64));
                AppDiagnostics.Write($"Hardware inventory: {inventory}");
                _hardwareInventoryLogged = true;
            }
            var sensors = new List<SensorReading>();
            var fans = new Dictionary<string, FanReading>(StringComparer.OrdinalIgnoreCase);
            var cores = new List<CoreReading>();
            var cpuTemps = new List<double>();
            var cpuClocks = new List<double>();
            var cpuPowers = new List<double>();
            var gpuTemps = new List<double>();
            var gpuPowers = new List<double>();
            var gpuLoads = new List<double>();
            var gpuClocks = new List<double>();
            var diskTemps = new List<double>();
            var motherboardTemps = new List<double>();
            double? cpuTotal = null;
            double? diskRead = null;
            double? diskWrite = null;
            string? processorName = null;
            string? gpuName = null;
            string? storageName = null;

            foreach (var hardware in allHardware)
            {
                var hardwareType = hardware.HardwareType.ToString();
                var isCpu = hardwareType.Equals("Cpu", StringComparison.OrdinalIgnoreCase);
                var isGpu = hardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);
                var isStorage = hardwareType.Equals("Storage", StringComparison.OrdinalIgnoreCase);
                var isMotherboard = IsMotherboardHardware(hardwareType);
                if (isCpu) processorName ??= hardware.Name;
                if (isGpu) gpuName ??= hardware.Name;
                if (isStorage) storageName ??= hardware.Name;

                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.Value is not float raw || float.IsNaN(raw)) continue;
                    var value = (double)raw;
                    var type = sensor.SensorType.ToString();
                    var name = sensor.Name;
                    var unit = UnitFor(type);
                    sensors.Add(new SensorReading(hardware.Name, name, type, value, unit));

                    if (type == "Fan")
                    {
                        var fanName = $"{hardware.Name} · {name}";
                        fans[fanName] = new FanReading(fanName, value);
                    }
                    else if ((type == "Control" || type == "Level") && IsFanControlName(name))
                    {
                        var fanName = $"{hardware.Name} · {name}";
                        if (!fans.ContainsKey(fanName)) fans[fanName] = new FanReading(fanName, null, value);
                    }
                    else if (isCpu && type == "Temperature")
                    {
                        cpuTemps.Add(value);
                    }
                    else if (isCpu && type == "Clock" && value > 0)
                    {
                        cpuClocks.Add(value);
                    }
                    else if (isCpu && type == "Power" && IsPackageLike(name))
                    {
                        cpuPowers.Add(value);
                    }
                    else if (isCpu && type == "Load")
                    {
                        if (name.Contains("Total", StringComparison.OrdinalIgnoreCase)) cpuTotal = value;
                        else if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        {
                            var clock = FindMatchingClock(hardware.Sensors, name);
                            cores.Add(new CoreReading(name, value, clock));
                        }
                    }
                    else if (isGpu && type == "Temperature")
                    {
                        gpuTemps.Add(value);
                    }
                    else if (isGpu && type == "Power" && IsPackageLike(name))
                    {
                        gpuPowers.Add(value);
                    }
                    else if (isGpu && type == "Load" &&
                             (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("D3D", StringComparison.OrdinalIgnoreCase)))
                    {
                        gpuLoads.Add(value);
                    }
                    else if (isGpu && type == "Clock" && name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    {
                        gpuClocks.Add(value);
                    }
                    else if (isStorage && type == "Temperature")
                    {
                        diskTemps.Add(value);
                    }
                    else if (isMotherboard && type == "Temperature")
                    {
                        motherboardTemps.Add(value);
                    }
                    else if (isStorage && type == "Throughput")
                    {
                        if (name.Contains("Read", StringComparison.OrdinalIgnoreCase)) diskRead = value;
                        if (name.Contains("Write", StringComparison.OrdinalIgnoreCase)) diskWrite = value;
                    }
                }
            }

            if (cores.Count == 0 && cpuTotal is not null)
            {
                cores.Add(new CoreReading("CPU", cpuTotal.Value, Average(cpuClocks)));
            }

            _hardwareStatus = BuildHardwareStatus(sensors.Count, motherboardTemps.Count > 0, fans.Values.Any(item => item.Rpm is > 0));
            return new HardwareMetrics
            {
                ProcessorName = processorName,
                GpuName = gpuName,
                StorageName = storageName,
                CpuUsage = cpuTotal,
                CpuClockMhz = Average(cpuClocks),
                CpuTemperature = PreferNamedTemperature(allHardware, isCpu: true) ?? Average(cpuTemps),
                CpuTemperatureMax = Max(cpuTemps),
                CpuPowerWatts = SumOrNull(cpuPowers),
                GpuUsage = Max(gpuLoads),
                GpuClockMhz = Max(gpuClocks),
                GpuTemperature = Max(gpuTemps),
                GpuPowerWatts = SumOrNull(gpuPowers),
                DiskTemperature = Max(diskTemps),
                MotherboardTemperature = Average(motherboardTemps),
                MotherboardTemperatureMax = Max(motherboardTemps),
                DiskReadBytesPerSecond = diskRead,
                DiskWriteBytesPerSecond = diskWrite,
                Cores = cores.Take(32).ToArray(),
                Fans = fans.Values.OrderBy(item => item.Name).ToArray(),
                Sensors = sensors.OrderBy(item => item.Group).ThenBy(item => item.Type).ThenBy(item => item.Name).ToArray()
            };
        }
        catch (Exception exception)
        {
            var prerequisite = !_pawnIoInstalled
                ? " · PawnIO may be required"
                : !_isElevated
                    ? " · try administrator access"
                    : string.Empty;
            _hardwareStatus = $"Enhanced sensors unavailable · {exception.GetType().Name}{prerequisite}";
            return new HardwareMetrics();
        }
    }

    private void EnsureHardware()
    {
        if (_hardwareAttempted) return;
        _hardwareAttempted = true;
        if (!_enableEnhancedSensors)
        {
            _hardwareStatus = "Basic metrics only · safe mode";
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            _hardwareStatus = "Hardware sensors require Windows";
            return;
        }

        try
        {
            _pawnIoInstalled = IsPawnIoInstalled();
            _isElevated = SensorAccessService.IsElevated;
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                // Keep motherboard enumeration on even before PawnIO is installed.
                // Some boards expose read-only values without the driver; when
                // they do not, the UI can now explain the exact prerequisite.
                IsMotherboardEnabled = true,
                IsControllerEnabled = _pawnIoInstalled,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsPowerMonitorEnabled = true
            };
            _computer.Open();
            AppDiagnostics.Write($"Hardware access: PawnIO={_pawnIoInstalled}; elevated={_isElevated}; motherboard=true; controller={_pawnIoInstalled}");
            _hardwareStatus = _pawnIoInstalled ? "Enhanced sensors starting" : "Enhanced sensors starting · PawnIO not installed";
        }
        catch (Exception exception)
        {
            _computer?.Close();
            _computer = null;
            var prerequisite = !_pawnIoInstalled
                ? " · PawnIO may be required"
                : !_isElevated
                    ? " · try administrator access"
                    : string.Empty;
            _hardwareStatus = $"Basic metrics only · {exception.GetType().Name}{prerequisite}";
        }
    }

    private static bool IsPawnIoInstalled()
    {
        try
        {
            return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("PawnIO availability check failed", exception);
            return false;
        }
    }

    private string BuildHardwareStatus(int sensorCount, bool hasMotherboardTemperature, bool hasFanRpm)
    {
        var status = $"Enhanced sensors · {sensorCount} readings";
        if (!hasMotherboardTemperature || !hasFanRpm)
        {
            status += !_pawnIoInstalled
                ? " · motherboard/fans may need PawnIO"
                : !_isElevated
                    ? " · run as administrator for more board sensors"
                    : " · some board channels may be unsupported";
        }
        return status;
    }

    private static bool IsMotherboardHardware(string hardwareType) =>
        hardwareType.Equals("Motherboard", StringComparison.OrdinalIgnoreCase) ||
        hardwareType.Equals("SuperIO", StringComparison.OrdinalIgnoreCase) ||
        hardwareType.Equals("EmbeddedController", StringComparison.OrdinalIgnoreCase);

    private static bool IsFanControlName(string name) =>
        name.Contains("Fan", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Pump", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Cool", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> roots)
    {
        foreach (var hardware in roots)
        {
            yield return hardware;
            foreach (var child in Flatten(hardware.SubHardware)) yield return child;
        }
    }

    private static double? FindMatchingClock(IEnumerable<ISensor> sensors, string loadName)
    {
        var suffix = loadName.Replace("CPU ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Load", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return sensors.FirstOrDefault(sensor => sensor.SensorType.ToString() == "Clock" &&
            (sensor.Name.Contains(suffix, StringComparison.OrdinalIgnoreCase) ||
             suffix.Contains(sensor.Name, StringComparison.OrdinalIgnoreCase)))?.Value;
    }

    private static double? PreferNamedTemperature(IEnumerable<IHardware> hardware, bool isCpu)
    {
        var candidate = hardware
            .Where(item => !isCpu || item.HardwareType.ToString().Equals("Cpu", StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Sensors)
            .FirstOrDefault(sensor => sensor.SensorType.ToString() == "Temperature" &&
                (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                 sensor.Name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase)));
        return candidate?.Value;
    }

    private static bool IsPackageLike(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Board", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("GPU", StringComparison.OrdinalIgnoreCase);

    private static string UnitFor(string type) => type switch
    {
        "Temperature" => "°C",
        "Fan" => "rpm",
        "Clock" => "MHz",
        "Load" or "Control" or "Level" => "%",
        "Power" => "W",
        "Voltage" => "V",
        "Current" => "A",
        "Throughput" => "B/s",
        "Data" => "GB",
        "SmallData" => "MB",
        _ => string.Empty
    };

    private static double? Average(IReadOnlyCollection<double> values) => values.Count == 0 ? null : values.Average();
    private static double? Max(IReadOnlyCollection<double> values) => values.Count == 0 ? null : values.Max();
    private static double? SumOrNull(IReadOnlyCollection<double> values) => values.Count == 0 ? null : values.Sum();

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware) child.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private sealed record HardwareMetrics
    {
        public string? ProcessorName { get; init; }
        public string? GpuName { get; init; }
        public string? StorageName { get; init; }
        public double? CpuUsage { get; init; }
        public double? CpuClockMhz { get; init; }
        public double? CpuTemperature { get; init; }
        public double? CpuTemperatureMax { get; init; }
        public double? CpuPowerWatts { get; init; }
        public double? GpuUsage { get; init; }
        public double? GpuClockMhz { get; init; }
        public double? GpuTemperature { get; init; }
        public double? GpuPowerWatts { get; init; }
        public double? DiskTemperature { get; init; }
        public double? MotherboardTemperature { get; init; }
        public double? MotherboardTemperatureMax { get; init; }
        public double? DiskReadBytesPerSecond { get; init; }
        public double? DiskWriteBytesPerSecond { get; init; }
        public IReadOnlyList<CoreReading> Cores { get; init; } = [];
        public IReadOnlyList<FanReading> Fans { get; init; } = [];
        public IReadOnlyList<SensorReading> Sensors { get; init; } = [];
    }
}
