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
    private static int _lastExternalForeground;
    private static IntPtr _foregroundHook;
    private static readonly WinEventCallback ForegroundCallback = (_, _, _, _, _, _, _) => ObserveForeground();

    public static void StartForegroundTracking()
    {
        ObserveForeground();
        // WINEVENT_OUTOFCONTEXT; retain the delegate for the lifetime of the hook.
        _foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, ForegroundCallback, 0, 0, 0);
        if (_foregroundHook == IntPtr.Zero) AppDiagnostics.Write("Foreground tracking unavailable; working-set trim disabled");
    }

    public static void StopForegroundTracking()
    {
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
    }

    private static void ObserveForeground()
    {
        var id = GetForegroundProcessId();
        if (id != 0 && id != Environment.ProcessId) Volatile.Write(ref _lastExternalForeground, id);
    }

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon",
        "fontdrvhost", "dwm", "svchost", "MsMpEng", "SecurityHealthService", "PwePcMonitor"
    };

    public static MemoryTrimResult TrimCurrentUserSession()
    {
        if (_foregroundHook == IntPtr.Zero) throw new InvalidOperationException("Foreground protection is unavailable.");
        var currentProcessId = Environment.ProcessId;
        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var foregroundProcessId = GetForegroundProcessId();
        var previousForegroundId = Volatile.Read(ref _lastExternalForeground);
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
                    process.Id == previousForegroundId ||
                    process.SessionId != currentSessionId ||
                    ProtectedProcessNames.Contains(process.ProcessName))
                {
                    continue;
                }

                var workingSet = process.WorkingSet64;
                if (workingSet >= MinimumWorkingSetBytes)
                    candidates.Add(new ProcessCandidate(process.Id, workingSet, process.StartTime.ToUniversalTime().ToFileTimeUtc()));
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
                // Check identity on the same handle used for the trim; a PID
                // can be reused between enumeration and OpenProcess.
                if (!GetProcessTimes(handle, out var creation, out _, out _, out _) ||
                    !CanTrimIdentity(candidate.Id, candidate.Created, creation,
                        GetForegroundProcessId(), Volatile.Read(ref _lastExternalForeground)))
                {
                    skipped++;
                    continue;
                }
                if (!EmptyWorkingSet(handle))
                {
                    skipped++;
                    continue;
                }

                trimmed++;
                using var refreshed = Process.GetProcessById(candidate.Id);
                refreshed.Refresh();
                if (refreshed.StartTime.ToUniversalTime().ToFileTimeUtc() == candidate.Created)
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

    private readonly record struct ProcessCandidate(int Id, long WorkingSet, long Created);

    internal static bool CanTrimIdentity(int id, long expectedCreation, long actualCreation, int foreground, int previousForeground) =>
        expectedCreation == actualCreation && id != foreground && id != previousForeground;

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

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
