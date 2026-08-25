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
    public const string GuideUrl = "https://github.com/namazso/PawnIO.Setup/releases";
    public const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

    public static bool IsPawnIoInstalled
    {
        get
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
    }

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
        return OpenUrl(GuideUrl, "Sensor support guide");
    }

    /// <summary>
    /// Opens the official PawnIO installer download in the user's browser. The
    /// app deliberately does not download or execute a kernel installer itself:
    /// the user can review the official release and approve the UAC prompt in
    /// the normal Windows shell.
    /// </summary>
    public static bool OpenInstaller()
    {
        return OpenUrl(InstallerUrl, "PawnIO installer");
    }

    private static bool OpenUrl(string url, string description)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write($"{description} could not be opened", exception);
            return false;
        }
    }
}
