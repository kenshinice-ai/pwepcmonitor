using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;

namespace Pwe.PcMonitor.ViewModels;

public sealed class MonitorViewModel : INotifyPropertyChanged, IDisposable
{
    private const int HistoryLength = 89;
    private readonly SystemSampler _sampler;
    private readonly AppSettingsService _settingsService;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private readonly Queue<double> _powerHistory = new();
    private CancellationTokenSource? _cancellation;
    private SystemSnapshot _snapshot = new();
    private AppSettings _settings;
    private bool _isSampling;
    private bool _memoryActionInProgress;

    public string MemoryActionStatus { get; private set; } = string.Empty;

    public MonitorViewModel(AppSettingsService settingsService, bool enableEnhancedSensors = true)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();
        ThemeManager.Apply(_settings.Theme);
        _sampler = new SystemSampler(enableEnhancedSensors);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SystemSnapshot>? SnapshotUpdated;

    public SystemSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            _snapshot = value;
            OnPropertyChanged();
            RaiseAllDisplayProperties();
        }
    }

    public AppSettings Settings => _settings;
    public ObservableCollection<CoreReading> Cores { get; } = [];
    public ObservableCollection<FanReading> Fans { get; } = [];
    public ObservableCollection<ProcessReading> Processes { get; } = [];
    public ObservableCollection<SensorReading> Sensors { get; } = [];

    public IReadOnlyList<double> CpuHistory => _cpuHistory.ToArray();
    public IReadOnlyList<double> GpuHistory => _gpuHistory.ToArray();
    public IReadOnlyList<double> PowerHistory => _powerHistory.ToArray();

    public string MachineSummary => string.Join(" · ",
        new[]
        {
            Snapshot.MachineName,
            Snapshot.MemoryTotal > 0 ? FormatBytes(Snapshot.MemoryTotal, oneDecimal: false) : null,
            $"up {FormatUptime(Snapshot.Uptime)}"
        }.Where(item => !string.IsNullOrWhiteSpace(item)));
    public string SensorStatus => Snapshot.SensorStatus;
    public string CpuValue => $"{Snapshot.CpuUsage:0}%";
    public string CpuSub => JoinAvailable(FormatGhz(Snapshot.CpuClockMhz), FormatTemperature(Snapshot.CpuTemperatureMax ?? Snapshot.CpuTemperature));
    public string GpuValue => Snapshot.GpuUsage is double value ? $"{value:0}%" : "—";
    public string GpuSub => JoinAvailable(FormatMhz(Snapshot.GpuClockMhz), FormatTemperature(Snapshot.GpuTemperature));
    public double? CombinedPower => SumNullable(Snapshot.CpuPowerWatts, Snapshot.GpuPowerWatts);
    public string PowerValue => CombinedPower is double value ? FormatWatts(value) : "—";
    public string PowerSub => JoinAvailable(
        Snapshot.CpuPowerWatts is double cpu ? $"CPU {FormatWatts(cpu)}" : null,
        Snapshot.GpuPowerWatts is double gpu ? $"GPU {FormatWatts(gpu)}" : null);
    public string CompactSecondaryLabel => HasGpuData ? "GPU" : HasBattery ? "BAT" : HasPowerData ? "POWER" : string.Empty;
    public string CompactSecondaryValue => HasGpuData
        ? HasGpuUsage ? GpuValue : GpuSub
        : HasBattery ? BatteryValue
        : HasPowerData ? PowerValue
        : string.Empty;
    public string CompactSecondarySub => HasGpuData
        ? HasGpuUsage && HasGpuSub ? GpuSub : string.Empty
        : HasBattery ? BatteryState
        : HasPowerData && HasPowerSub ? PowerSub
        : string.Empty;
    public HealthState CompactSecondaryHealth => HasGpuData ? GpuHealth : HasPowerData ? PowerHealth : HealthState.Calm;
    public bool HasCompactSecondary => HasGpuData || HasBattery || HasPowerData;
    public bool HasCompactSecondarySub => !string.IsNullOrWhiteSpace(CompactSecondarySub) && CompactSecondarySub != "—";
    public string CpuAverageTemperature => FormatTemperatureDetailed(Snapshot.CpuTemperature);
    public string CpuMaxTemperature => FormatTemperatureDetailed(Snapshot.CpuTemperatureMax);
    public string GpuTemperature => FormatTemperatureDetailed(Snapshot.GpuTemperature);
    public string GpuTemperatureSource => Snapshot.GpuTemperatureSource;
    public bool HasGpuTemperatureSource => !Snapshot.GpuTemperatureSource.Equals("GPU provider not detected", StringComparison.OrdinalIgnoreCase);
    public string DiskTemperature => FormatTemperatureDetailed(Snapshot.DiskTemperature);
    public string MotherboardTemperature => FormatTemperatureDetailed(Snapshot.MotherboardTemperature);
    public string TemperatureStatus => Snapshot.TemperatureStatus;
    public string MemoryValue => $"{FormatBytes(Snapshot.MemoryUsed)}";
    public string MemoryTotal => $"of {FormatBytes(Snapshot.MemoryTotal)}";
    public string MemoryAvailable => FormatBytes(Snapshot.MemoryAvailable);
    public string MemoryPressure => Snapshot.MemoryHealth switch { HealthState.Hot => "Critical", HealthState.Warm => "Elevated", _ => "Normal" };
    public string DiskValue => FormatBytes((ulong)Math.Max(0, Snapshot.DiskTotal - Snapshot.DiskFree));
    public string DiskTotal => $"of {FormatBytes((ulong)Math.Max(0, Snapshot.DiskTotal))}";
    public string DiskRead => FormatRate(Snapshot.DiskReadBytesPerSecond);
    public string DiskWrite => FormatRate(Snapshot.DiskWriteBytesPerSecond);
    public string NetworkDown => FormatRate(Snapshot.NetworkDownBytesPerSecond);
    public string NetworkUp => FormatRate(Snapshot.NetworkUpBytesPerSecond);
    public string BatteryValue => $"{Snapshot.BatteryPercent}%";
    public string BatteryState => Snapshot.BatteryCharging ? "charging" : Snapshot.BatteryOnAc ? "on power" : "on battery";
    public string FanSummary => Fans.Count == 0 ? "No readable fan sensors" : $"{Fans.Count} readable channel{(Fans.Count == 1 ? "" : "s")}";
    public string FanValue => Fans.FirstOrDefault()?.DisplayValue ?? "—";
    public string FanSub => Fans.Count switch
    {
        0 => "no readable channel",
        1 => "fan channel",
        _ => $"{Fans.Count} channels"
    };
    public string SensorAccessHint => NeedsSensorAccess
        ? Snapshot.SensorStatus.Contains("PawnIO", StringComparison.OrdinalIgnoreCase)
            ? "PawnIO is optional. Current native sensors stay available; choose Get PawnIO only for board, fan or deeper temperature channels, then Recheck sensors."
            : "Some channels need administrator access before they can be read; choose Recheck sensors after changing access."
        : string.Empty;
    public string OverallLabel => Snapshot.OverallHealth switch { HealthState.Hot => "HOT", HealthState.Warm => "WARM", _ => "CALM" };
    public HealthState OverallHealth => Snapshot.OverallHealth;
    public HealthState CpuHealth => Snapshot.CpuHealth;
    public HealthState GpuHealth => Snapshot.GpuHealth;
    public HealthState PowerHealth => HealthRules.Grade(CombinedPower, 150, 300);
    public HealthState MemoryHealth => Snapshot.MemoryHealth;
    public HealthState DiskHealth => Snapshot.DiskHealth;
    public bool HasBattery => Snapshot.HasBattery;
    public bool HasCpuSub => HasPositive(Snapshot.CpuClockMhz) || HasPositive(Snapshot.CpuTemperatureMax ?? Snapshot.CpuTemperature);
    public bool HasCpuAverageTemperature => HasPositive(Snapshot.CpuTemperature);
    public bool HasCpuMaxTemperature => HasPositive(Snapshot.CpuTemperatureMax);
    public bool HasGpuUsage => Snapshot.GpuUsage is not null;
    public bool HasGpuSub => HasPositive(Snapshot.GpuClockMhz) || HasPositive(Snapshot.GpuTemperature);
    public bool HasGpuTemperature => HasPositive(Snapshot.GpuTemperature);
    public bool HasGpuData => HasGpuUsage || HasGpuSub;
    public bool HasPowerData => CombinedPower is not null;
    public bool HasPowerSub => Snapshot.CpuPowerWatts is not null || Snapshot.GpuPowerWatts is not null;
    public bool HasGpuOrPowerData => HasGpuData || HasPowerData;
    public bool HasGpuAndPowerData => HasGpuData && HasPowerData;
    public bool HasThermalReadings => HasCpuAverageTemperature || HasCpuMaxTemperature || HasGpuTemperature || HasPositive(Snapshot.DiskTemperature) || HasPositive(Snapshot.MotherboardTemperature);
    public bool HasMotherboardTemperature => HasPositive(Snapshot.MotherboardTemperature);
    public bool HasDiskTemperature => HasPositive(Snapshot.DiskTemperature);
    public bool HasMemoryData => Snapshot.MemoryTotal > 0;
    public bool HasDiskData => Snapshot.DiskTotal > 0;
    public bool HasDiskRead => Snapshot.DiskReadBytesPerSecond is not null;
    public bool HasDiskWrite => Snapshot.DiskWriteBytesPerSecond is not null;
    public bool HasNetworkData => !Snapshot.NetworkName.Equals("Network", StringComparison.OrdinalIgnoreCase) || HasNetworkAddress || Snapshot.NetworkDownBytesPerSecond > 0 || Snapshot.NetworkUpBytesPerSecond > 0;
    public bool HasNetworkAddress => !string.IsNullOrWhiteSpace(Snapshot.IpAddress) && !Snapshot.IpAddress.Equals("—", StringComparison.Ordinal);
    public bool HasProcesses => Processes.Count > 0;
    public bool HasFans => Fans.Any(item => item.Rpm is >= 0 || item.Percent is >= 0);
    public bool HasThermalOrMemoryData => HasThermalReadings || HasMemoryData;
    public bool HasFansOrDiskData => HasFans || HasDiskData;
    public bool HasBatteryOrNetworkData => HasBattery || HasNetworkData;
    public IReadOnlyList<string> SensorDiagnostics => BuildSensorDiagnostics();
    public bool HasSensorDiagnostics => SensorDiagnostics.Count > 0;
    public bool IsMemoryActionInProgress => _memoryActionInProgress;
    public bool CanOptimizeMemory => HasMemoryData && !_memoryActionInProgress;
    public bool HasMemoryActionStatus => !string.IsNullOrWhiteSpace(MemoryActionStatus);
    public bool NeedsSensorAccess => Snapshot.SensorStatus.Contains("PawnIO", StringComparison.OrdinalIgnoreCase) ||
                                     Snapshot.SensorStatus.Contains("administrator", StringComparison.OrdinalIgnoreCase) ||
                                     Snapshot.TemperatureStatus.Contains("PawnIO", StringComparison.OrdinalIgnoreCase);
    public bool ShowSensors => _settings.ShowAllSensors;
    public bool ShowFloatingWidget => _settings.ShowFloatingWidget;
    public double RefreshSeconds => _settings.RefreshSeconds;
    public ThemePreference Theme => _settings.Theme;

    public void Start()
    {
        if (_cancellation is not null) return;
        _cancellation = new CancellationTokenSource();
        _ = SamplingLoopAsync(_cancellation.Token);
    }

    public void SetRefresh(double seconds)
    {
        if (seconds is not (1 or 2 or 3 or 5)) return;
        _settings = _settings with { RefreshSeconds = seconds };
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(RefreshSeconds));
    }

    public void SetTheme(ThemePreference theme)
    {
        _settings = _settings with { Theme = theme };
        _settingsService.Save(_settings);
        ThemeManager.Apply(theme);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(string.Empty);
    }

    public void ToggleSensors()
    {
        _settings = _settings with { ShowAllSensors = !_settings.ShowAllSensors };
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ShowSensors));
    }

    public void ToggleFloatingWidget()
    {
        _settings = _settings with { ShowFloatingWidget = !_settings.ShowFloatingWidget };
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ShowFloatingWidget));
    }

    public void RecheckSensors()
    {
        _sampler.RequestHardwareRecheck();
        OnPropertyChanged(nameof(SensorAccessHint));
    }

    public async Task OptimizeMemoryAsync()
    {
        if (!CanOptimizeMemory || _memoryActionInProgress) return;

        _memoryActionInProgress = true;
        MemoryActionStatus = "Measuring eligible user processes…";
        OnPropertyChanged(nameof(IsMemoryActionInProgress));
        OnPropertyChanged(nameof(CanOptimizeMemory));
        OnPropertyChanged(nameof(MemoryActionStatus));
        OnPropertyChanged(nameof(HasMemoryActionStatus));

        try
        {
            var result = await Task.Run(MemoryOptimizer.TrimCurrentUserSession);
            MemoryActionStatus = result.TrimmedProcesses > 0
                ? $"Trimmed {result.TrimmedProcesses} process{(result.TrimmedProcesses == 1 ? "" : "es")} · {FormatBytes((ulong)Math.Max(0, result.EstimatedBytesReleased))} working set released"
                : "No eligible user-process working sets could be trimmed";
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Memory optimization failed", exception);
            MemoryActionStatus = "Memory optimization was unavailable";
        }
        finally
        {
            _memoryActionInProgress = false;
            OnPropertyChanged(nameof(IsMemoryActionInProgress));
            OnPropertyChanged(nameof(CanOptimizeMemory));
            OnPropertyChanged(nameof(MemoryActionStatus));
            OnPropertyChanged(nameof(HasMemoryActionStatus));
        }
    }

    private async Task SamplingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_isSampling)
            {
                _isSampling = true;
                try
                {
                    var next = await Task.Run(_sampler.Sample, cancellationToken);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplySnapshot(next));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // The next cycle retries; the last good snapshot remains visible.
                    AppDiagnostics.Write("Sampling loop failed", exception);
                }
                finally
                {
                    _isSampling = false;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.RefreshSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplySnapshot(SystemSnapshot next)
    {
        Snapshot = next;
        Replace(Cores, next.Cores);
        Replace(Fans, next.Fans.Where(item => item.Rpm is >= 0 || item.Percent is >= 0));
        Replace(Processes, next.Processes);
        Replace(Sensors, _settings.ShowAllSensors ? next.Sensors : next.Sensors.Take(24));
        Push(_cpuHistory, next.CpuUsage);
        Push(_gpuHistory, next.GpuUsage ?? 0);
        Push(_powerHistory, CombinedPower ?? 0);
        OnPropertyChanged(nameof(CpuHistory));
        OnPropertyChanged(nameof(GpuHistory));
        OnPropertyChanged(nameof(PowerHistory));
        OnPropertyChanged(nameof(HasFans));
        OnPropertyChanged(nameof(HasProcesses));
        OnPropertyChanged(nameof(HasThermalOrMemoryData));
        OnPropertyChanged(nameof(HasFansOrDiskData));
        OnPropertyChanged(nameof(HasBatteryOrNetworkData));
        OnPropertyChanged(nameof(FanSummary));
        OnPropertyChanged(nameof(FanValue));
        OnPropertyChanged(nameof(FanSub));
        OnPropertyChanged(nameof(SensorDiagnostics));
        OnPropertyChanged(nameof(HasSensorDiagnostics));
        SnapshotUpdated?.Invoke(this, next);
    }

    private void RaiseAllDisplayProperties()
    {
        foreach (var name in new[]
        {
            nameof(MachineSummary), nameof(SensorStatus), nameof(CpuValue), nameof(CpuSub), nameof(GpuValue), nameof(GpuSub),
            nameof(CombinedPower), nameof(PowerValue), nameof(PowerSub), nameof(CompactSecondaryLabel), nameof(CompactSecondaryValue), nameof(CompactSecondarySub), nameof(CompactSecondaryHealth), nameof(HasCompactSecondary), nameof(HasCompactSecondarySub), nameof(CpuAverageTemperature), nameof(CpuMaxTemperature),
            nameof(GpuTemperature), nameof(GpuTemperatureSource), nameof(HasGpuTemperatureSource), nameof(DiskTemperature), nameof(MemoryValue), nameof(MemoryTotal), nameof(MemoryAvailable),
            nameof(MemoryPressure), nameof(DiskValue), nameof(DiskTotal), nameof(DiskRead), nameof(DiskWrite), nameof(NetworkDown),
            nameof(NetworkUp), nameof(BatteryValue), nameof(BatteryState), nameof(OverallLabel), nameof(OverallHealth), nameof(CpuHealth),
            nameof(GpuHealth), nameof(PowerHealth), nameof(MemoryHealth), nameof(DiskHealth), nameof(HasBattery),
            nameof(MotherboardTemperature), nameof(TemperatureStatus), nameof(SensorAccessHint), nameof(NeedsSensorAccess),
            nameof(FanValue), nameof(FanSub), nameof(HasCpuSub), nameof(HasCpuAverageTemperature), nameof(HasCpuMaxTemperature),
            nameof(HasGpuUsage), nameof(HasGpuSub), nameof(HasGpuTemperature), nameof(HasGpuData), nameof(HasPowerData), nameof(HasPowerSub), nameof(HasGpuOrPowerData), nameof(HasGpuAndPowerData),
            nameof(HasThermalReadings), nameof(HasMotherboardTemperature), nameof(HasDiskTemperature), nameof(HasMemoryData), nameof(HasDiskData),
            nameof(HasDiskRead), nameof(HasDiskWrite), nameof(HasNetworkData), nameof(HasNetworkAddress), nameof(HasProcesses),
            nameof(HasThermalOrMemoryData), nameof(HasFansOrDiskData), nameof(HasBatteryOrNetworkData), nameof(CanOptimizeMemory),
            nameof(MemoryActionStatus), nameof(HasMemoryActionStatus), nameof(SensorDiagnostics), nameof(HasSensorDiagnostics)
        }) OnPropertyChanged(name);
    }

    private IReadOnlyList<string> BuildSensorDiagnostics()
    {
        var diagnostics = new List<string>();
        AddDiagnostic(diagnostics, Snapshot.SensorStatus, "Basic Windows metrics");

        if (!HasCpuAverageTemperature && !HasCpuMaxTemperature)
            diagnostics.Add("CPU temperature: no readable channel");
        if (!HasGpuTemperature && (HasGpuData || !Snapshot.GpuName.Equals("GPU", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add($"GPU temperature: no readable channel ({Snapshot.GpuTemperatureSource})");
        if (!HasMotherboardTemperature)
            diagnostics.Add("Motherboard temperature: no readable channel");
        if (!HasFans)
            diagnostics.Add("Fan RPM: no readable channel");
        if (HasDiskData && !HasDiskTemperature)
            diagnostics.Add("SSD temperature: no readable channel");
        if (HasDiskData && !HasDiskRead)
            diagnostics.Add("Disk read rate: not exposed");
        if (HasDiskData && !HasDiskWrite)
            diagnostics.Add("Disk write rate: not exposed");
        if (!HasMemoryData)
            diagnostics.Add("Memory: unavailable");
        if (!HasNetworkData)
            diagnostics.Add("Network adapter: unavailable");
        if (!HasBattery)
            diagnostics.Add("Battery: no battery detected");
        AddDiagnostic(diagnostics, Snapshot.TemperatureStatus, "");

        return diagnostics
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDiagnostic(List<string> diagnostics, string? value, string ignoredValue)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.Equals(ignoredValue, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(value);
    }

    private static bool HasPositive(double? value) => value is > 0;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static void Push(Queue<double> queue, double value)
    {
        queue.Enqueue(value);
        while (queue.Count > HistoryLength) queue.Dequeue();
    }

    private static double? SumNullable(double? first, double? second) =>
        first is null && second is null ? null : (first ?? 0) + (second ?? 0);

    private static string JoinAvailable(params string?[] parts)
    {
        var available = parts.Where(part => !string.IsNullOrWhiteSpace(part) && part != "—").ToArray();
        return available.Length == 0 ? "—" : string.Join(" · ", available);
    }

    public static string FormatBytes(ulong bytes, bool oneDecimal = true)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
        return oneDecimal && value < 100 ? $"{value:0.0} {units[unit]}" : $"{value:0} {units[unit]}";
    }

    public static string FormatRate(double? bytesPerSecond) => bytesPerSecond is null ? "—" : $"{FormatBytes((ulong)Math.Max(0, bytesPerSecond.Value))}/s";
    private static string FormatTemperature(double? value) => value is > 0 ? $"{value:0}°" : "—";
    private static string FormatTemperatureDetailed(double? value) => value is > 0 ? $"{value:0.0}°C" : "—";
    private static string FormatMhz(double? value) => value is > 0 ? $"{value:0} MHz" : "—";
    private static string FormatGhz(double? value) => value is > 0 ? $"{value / 1000:0.00} GHz" : "—";
    private static string FormatWatts(double value) => value >= 10 ? $"{value:0} W" : $"{value:0.0} W";
    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
        : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m" : $"{uptime.Minutes}m";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _sampler.Dispose();
    }
}
