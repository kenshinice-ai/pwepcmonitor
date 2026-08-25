using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace Pwe.PcMonitor.Services;

internal enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

internal readonly record struct GpuTemperatureReading(
    GpuVendor Vendor,
    string HardwareName,
    string SensorName,
    double Value);

internal sealed record GpuTemperatureResult(
    double? Temperature,
    string Source,
    bool HasRecognizedVendor)
{
    public static GpuTemperatureResult Empty { get; } = new(null, "GPU provider not detected", false);
}

/// <summary>
/// Chooses a stable GPU-core reading from LibreHardwareMonitor's vendor backends.
/// The library uses the vendor driver APIs when they are present (NVAPI, ADL or
/// Intel GCL/IGCL); no vendor DLL is copied into the PWE package.
/// </summary>
internal static class GpuTemperatureProvider
{
    public static GpuVendor Detect(IHardware hardware)
    {
        var type = hardware.HardwareType.ToString();
        if (type.Equals("GpuNvidia", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (type.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (type.Equals("GpuIntel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;

        // Do not infer a GPU vendor from an arbitrary motherboard or CPU name.
        // LHM has used both explicit GPU hardware types and descriptive names
        // across versions, so only continue with name matching for GPU-like
        // hardware entries.
        var looksLikeGpu = type.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) ||
                           hardware.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                           hardware.Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
                           hardware.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                           hardware.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                           hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeGpu) return GpuVendor.Unknown;

        var name = hardware.Name;
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RTX", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;

        return GpuVendor.Unknown;
    }

    public static bool IsTemperatureSensor(IHardware hardware, string name)
    {
        if (hardware.HardwareType.ToString().Equals("Cpu", StringComparison.OrdinalIgnoreCase)) return false;
        return Detect(hardware) != GpuVendor.Unknown ||
               name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Memory Junction", StringComparison.OrdinalIgnoreCase);
    }

    public static GpuTemperatureResult Resolve(
        IEnumerable<GpuTemperatureReading> readings,
        IEnumerable<GpuVendor> detectedVendors)
    {
        var values = readings
            .Where(item => item.Value is > -20 and < 150)
            .ToArray();
        var vendors = detectedVendors
            .Where(item => item != GpuVendor.Unknown)
            .Distinct()
            .OrderBy(item => item)
            .ToArray();
        var hasRecognizedVendor = vendors.Length > 0;
        var source = BuildSource(vendors, values.Length > 0);

        if (values.Length == 0)
        {
            return new GpuTemperatureResult(null, source, hasRecognizedVendor);
        }

        // A GPU can expose core, hotspot, memory and board sensors. Select one
        // core/edge reading per physical adapter first, then use the hottest
        // adapter core for the dashboard's single GPU value.
        var selected = values
            .GroupBy(item => item.HardwareName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => SensorPriority(item.Vendor, item.SensorName))
                .ThenBy(item => item.SensorName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();

        return new GpuTemperatureResult(
            selected.Length == 0 ? null : selected.Max(item => item.Value),
            source,
            hasRecognizedVendor);
    }

    private static string BuildSource(IReadOnlyCollection<GpuVendor> vendors, bool hasTemperature)
    {
        if (vendors.Count == 0)
        {
            return hasTemperature ? "LibreHardwareMonitor GPU sensor" : "GPU provider not detected";
        }

        var names = string.Join(" + ", vendors.Select(DisplayName));
        return hasTemperature
            ? $"{names} via LibreHardwareMonitor"
            : $"{names} driver backend · temperature unavailable";
    }

    private static int SensorPriority(GpuVendor vendor, string name)
    {
        if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Equals("GPU Edge", StringComparison.OrdinalIgnoreCase)) return 98;
        if (name.Equals("GPU", StringComparison.OrdinalIgnoreCase)) return 96;
        if (name.Contains("Graphics", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("Temperature", StringComparison.OrdinalIgnoreCase) && !IsSecondary(name)) return 80;
        if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)) return 45;
        if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase)) return 40;
        if (name.Contains("Board", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Power", StringComparison.OrdinalIgnoreCase)) return 35;
        return vendor == GpuVendor.Unknown ? 20 : 60;
    }

    private static bool IsSecondary(string name) =>
        name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Board", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Power", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA NVAPI",
        GpuVendor.Amd => "AMD ADL",
        GpuVendor.Intel => "Intel IGCL",
        _ => "GPU"
    };
}
