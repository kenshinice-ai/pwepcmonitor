using System.Text;

namespace Pwe.PcMonitor.Services;

/// <summary>
/// Writes a small local diagnostic trail so a startup failure can be diagnosed
/// without requiring administrative access or a debugger attached to the app.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object Gate = new();

    public static string LogPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "PWE", "PC Monitor", "logs", "latest.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never become a second startup failure.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write($"{message}: {exception}");

    public static void Clear()
    {
        try
        {
            if (File.Exists(LogPath)) File.Delete(LogPath);
        }
        catch
        {
            // The next write can still create or append to the existing file.
        }
    }
}
