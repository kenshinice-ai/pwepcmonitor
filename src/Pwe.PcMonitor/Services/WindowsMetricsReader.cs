using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Pwe.PcMonitor.Models;

namespace Pwe.PcMonitor.Services;

internal sealed class WindowsMetricsReader
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private DateTimeOffset _previousNetworkAt;
    private long _previousNetworkIn;
    private long _previousNetworkOut;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)> _processBaselines = [];

    public BasicMetrics Read()
    {
        var now = DateTimeOffset.UtcNow;
        var (networkName, ip, down, up) = ReadNetwork(now);
        var (total, available) = ReadMemory();
        var (diskName, diskTotal, diskFree) = ReadDisk();
        var (hasBattery, batteryPercent, charging, onAc) = ReadBattery();

        return new BasicMetrics(
            ReadCpuUsage(),
            ReadProcessorName(),
            total,
            available,
            diskName,
            diskTotal,
            diskFree,
            networkName,
            ip,
            down,
            up,
            TimeSpan.FromMilliseconds(GetTickCount64()),
            hasBattery,
            batteryPercent,
            charging,
            onAc,
            ReadProcesses(now));
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        var idleDelta = idleValue - _previousIdle;
        var kernelDelta = kernelValue - _previousKernel;
        var userDelta = userValue - _previousUser;
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        var total = kernelDelta + userDelta;
        return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
    }

    private static string ReadProcessorName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Windows PC";
        }
        catch
        {
            return "Windows PC";
        }
    }

    private static (ulong Total, ulong Available) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? (status.TotalPhysical, status.AvailablePhysical)
            : (0, 0);
    }

    private static (string Name, long Total, long Free) ReadDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            return ($"{drive.Name.TrimEnd('\\')} · {drive.VolumeLabel}".TrimEnd(' ', '·'), drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch
        {
            return ("System drive", 0, 0);
        }
    }

    private (string Name, string Ip, double Down, double Up) ReadNetwork(DateTimeOffset now)
    {
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                               item.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .Select(item => new
                {
                    Interface = item,
                    Stats = item.GetIPStatistics(),
                    Ip = item.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString()
                })
                .OrderByDescending(item => item.Stats.BytesReceived + item.Stats.BytesSent)
                .FirstOrDefault();

            if (active is null) return ("Network", "—", 0, 0);
            var elapsed = (now - _previousNetworkAt).TotalSeconds;
            var down = elapsed > 0 && _previousNetworkIn > 0
                ? Math.Max(0, active.Stats.BytesReceived - _previousNetworkIn) / elapsed
                : 0;
            var up = elapsed > 0 && _previousNetworkOut > 0
                ? Math.Max(0, active.Stats.BytesSent - _previousNetworkOut) / elapsed
                : 0;
            _previousNetworkAt = now;
            _previousNetworkIn = active.Stats.BytesReceived;
            _previousNetworkOut = active.Stats.BytesSent;
            return (active.Interface.Name, active.Ip ?? "—", down, up);
        }
        catch
        {
            return ("Network", "—", 0, 0);
        }
    }

    private static (bool Present, int Percent, bool Charging, bool OnAc) ReadBattery()
    {
        try
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;
            var present = status.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery &&
                          status.BatteryLifePercent >= 0;
            var percent = present ? (int)Math.Round(status.BatteryLifePercent * 100) : 0;
            var charging = present && status.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
            var onAc = status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            return (present, percent, charging, onAc);
        }
        catch
        {
            return (false, 0, false, false);
        }
    }

    private IReadOnlyList<ProcessReading> ReadProcesses(DateTimeOffset now)
    {
        var readings = new List<ProcessReading>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    seen.Add(process.Id);
                    var cpu = process.TotalProcessorTime;
                    var usage = 0d;
                    if (_processBaselines.TryGetValue(process.Id, out var previous))
                    {
                        var elapsed = (now - previous.At).TotalSeconds;
                        if (elapsed > 0)
                        {
                            usage = Math.Max(0, (cpu - previous.Cpu).TotalSeconds / elapsed * 100 / Environment.ProcessorCount);
                        }
                    }
                    _processBaselines[process.Id] = (cpu, now);
                    readings.Add(new ProcessReading(process.ProcessName, usage, process.WorkingSet64));
                }
                catch
                {
                    // Protected and exiting processes are expected to be unreadable.
                }
            }
        }

        foreach (var stale in _processBaselines.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _processBaselines.Remove(stale);
        }
        return readings.OrderByDescending(item => item.CpuPercent).ThenByDescending(item => item.MemoryBytes).Take(6).ToArray();
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME value) =>
        ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idle,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernel,
        out System.Runtime.InteropServices.ComTypes.FILETIME user);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

internal sealed record BasicMetrics(
    double CpuUsage,
    string ProcessorName,
    ulong MemoryTotal,
    ulong MemoryAvailable,
    string DiskName,
    long DiskTotal,
    long DiskFree,
    string NetworkName,
    string IpAddress,
    double NetworkDown,
    double NetworkUp,
    TimeSpan Uptime,
    bool HasBattery,
    int BatteryPercent,
    bool BatteryCharging,
    bool BatteryOnAc,
    IReadOnlyList<ProcessReading> Processes);
