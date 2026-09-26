using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

var cpu = new CpuCounterBaseline();
Check(cpu.Read(100, 200, 100) is null, "CPU first reading initializes baseline");
Check(cpu.Read(150, 300, 100) == 50, "CPU interval usage");
Check(cpu.Read(150, 300, 100) is null, "CPU no counter progress is unknown");
Check(cpu.Read(1, 2, 3) is null, "CPU rollback resets baseline");
cpu.Reset();
Check(cpu.Read(10, 20, 30) is null, "CPU failure reset requires warmup");

var network = new NetworkCounterBaseline();
var at = DateTimeOffset.UtcNow;
Check(network.Read("A", 0, 0, at).Down is null, "Network first reading unknown");
Check(network.Read("A", 200, 100, at.AddSeconds(2)) == (100d, 50d), "Network zero baseline is valid");
Check(network.Read("B", 1000000, 1000000, at.AddSeconds(4)).Down is null, "Network switch cannot spike");
Check(network.Read("B", 1000000, 1000000, at.AddSeconds(6)) == (0d, 0d), "Real idle network is zero");
Check(network.Read("B", 1, 1, at.AddSeconds(8)).Down is null, "Network rollback unknown");
network.Reset();
Check(network.Read("B", 100, 100, at.AddSeconds(10)).Up is null, "Reconnect warms up");

GpuTemperatureReading Reading(string name, double value, string id = "adapter/0") =>
    new(GpuVendor.Nvidia, "Same GPU", name, value, id);
double? Resolve(params GpuTemperatureReading[] values) => GpuTemperatureProvider.Resolve(values, [GpuVendor.Nvidia]).Temperature;
Check(Resolve() is null, "No GPU temperature stays missing");
foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 170, -30 })
    Check(Resolve(Reading("GPU Core", value)) is null, $"Invalid temperature rejected: {value}");
foreach (var name in new[] { "GPU Hot Spot", "GPU Hotspot", "GPU Memory Junction", "GPU Board", "GPU Power" })
    Check(Resolve(Reading(name, 90)) is null, $"Secondary temperature excluded: {name}");
Check(Resolve(Reading("GPU Core", 60), Reading("GPU Hot Spot", 90)) == 60, "Core wins over hotspot");
Check(Resolve(Reading("GPU Edge", 65)) == 65, "Edge remains supported");
var pair = new[] { Reading("GPU Core", 55), Reading("GPU Core", 75, "adapter/1") };
Check(Resolve(pair) == 75 && Resolve(pair.Reverse().ToArray()) == 75, "Same-name adapters retain independent cores");
var empty = new SystemSnapshot();
Check(empty.CpuUsage is null && empty.Timestamp is null, "Failed snapshot is not fresh zero");
Check(empty.NetworkDownBytesPerSecond is null, "Missing network is not zero");
Check((empty with { CpuUsage = 0 }).CpuUsage == 0, "Real zero CPU retained");
Check(MemoryOptimizer.CanTrimIdentity(10, 100, 100, 20, 30), "Matching background process eligible");
Check(!MemoryOptimizer.CanTrimIdentity(10, 100, 101, 20, 30), "Reused PID skipped");
Check(!MemoryOptimizer.CanTrimIdentity(10, 100, 100, 10, 30), "Current foreground skipped");
Check(!MemoryOptimizer.CanTrimIdentity(10, 100, 100, 20, 10), "Previous foreground skipped");
Console.WriteLine($"{checks} regression checks passed.");
