using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pwe.PcMonitor.Services;

public sealed record MemoryTrimResult(
    int ScannedProcesses,
    int TrimmedProcesses,
    long EstimatedBytesReleased,
    int SkippedProcesses);

/// <summary>
/// Performs a narrow, opt-in working-set trim for large user-session processes.
/// It never terminates processes, purges the system standby list or writes to
/// hardware. Windows may immediately reuse the released pages, so the result
/// is deliberately reported as an estimate rather than a guaranteed free RAM
/// increase.
/// </summary>
public static class MemoryOptimizer
{
    private const long MinimumWorkingSetBytes = 128L * 1024 * 1024;
    private const int MaximumProcessesToTrim = 12;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon",
        "fontdrvhost", "dwm", "svchost", "MsMpEng", "SecurityHealthService", "PwePcMonitor"
    };

    public static MemoryTrimResult TrimCurrentUserSession()
    {
        var currentProcessId = Environment.ProcessId;
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var foregroundProcessId = GetForegroundProcessId();
        var candidates = new List<ProcessCandidate>();
        var scanned = 0;
        var skipped = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                scanned++;
                if (process.Id == currentProcessId ||
                    process.Id == foregroundProcessId ||
                    process.SessionId != currentSessionId ||
                    ProtectedProcessNames.Contains(process.ProcessName))
                {
                    continue;
                }

                var workingSet = process.WorkingSet64;
                if (workingSet >= MinimumWorkingSetBytes)
                    candidates.Add(new ProcessCandidate(process.Id, workingSet));
            }
            catch
            {
                skipped++;
            }
            finally
            {
                process.Dispose();
            }
        }

        var trimmed = 0;
        long released = 0;
        foreach (var candidate in candidates.OrderByDescending(item => item.WorkingSet).Take(MaximumProcessesToTrim))
        {
            var handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, candidate.Id);
            if (handle == IntPtr.Zero)
            {
                skipped++;
                continue;
            }

            try
            {
                if (!EmptyWorkingSet(handle))
                {
                    skipped++;
                    continue;
                }

                trimmed++;
                using var refreshed = Process.GetProcessById(candidate.Id);
                refreshed.Refresh();
                released += Math.Max(0, candidate.WorkingSet - refreshed.WorkingSet64);
            }
            catch
            {
                skipped++;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return new MemoryTrimResult(scanned, trimmed, released, skipped);
    }

    private static int GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    private readonly record struct ProcessCandidate(int Id, long WorkingSet);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
