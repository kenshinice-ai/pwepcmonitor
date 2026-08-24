using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Pwe.PcMonitor.Services;

/// <summary>
/// Keeps privileged sensor setup behind explicit user actions. The monitor itself
/// remains asInvoker and never silently installs a kernel driver.
/// </summary>
public static class SensorAccessService
{
    public const string GuideUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases";

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool TryRestartElevated()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception)
        {
            // User cancelled the UAC prompt, or the shell rejected elevation.
            return false;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Elevated sensor restart failed", exception);
            return false;
        }
    }

    public static bool OpenGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GuideUrl) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Sensor support guide could not be opened", exception);
            return false;
        }
    }
}
