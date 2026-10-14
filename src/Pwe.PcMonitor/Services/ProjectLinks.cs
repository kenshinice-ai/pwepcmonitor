namespace Pwe.PcMonitor.Services;

/// <summary>
/// Where the source lives, and the one page for every free PWE tool.
/// </summary>
/// <remarks>
/// The support page is shared with PWE Monitor and PWE Lumen Bar, so how to give something back can
/// change without shipping a build. The app links to it from the Settings menu and nowhere else: no
/// prompt, no count of days, nothing locked (Lee, 2026-10-14).
/// </remarks>
public static class ProjectLinks
{
    public const string Repository = "https://github.com/kenshinice-ai/pwepcmonitor";
    public const string Support = "https://pwestudio.site/support";

    /// <summary>Opens a page in the default browser; a failure is logged, never thrown.</summary>
    public static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write($"{url} could not be opened", exception);
        }
    }
}
