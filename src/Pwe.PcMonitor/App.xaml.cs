using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Resources;
using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;
using Pwe.PcMonitor.ViewModels;
using Forms = System.Windows.Forms;

namespace Pwe.PcMonitor;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _currentTrayIcon;
    private Bitmap? _baseIcon;
    private MainWindow? _window;
    private MonitorViewModel? _viewModel;
    private AppSettingsService? _settingsService;
    private int _lastIconBucket = -1;
    private HealthState _lastIconHealth = (HealthState)(-1);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settingsService = new AppSettingsService();
        _viewModel = new MonitorViewModel(_settingsService);
        _window = new MainWindow(_viewModel);
        CreateTrayIcon();
        _viewModel.SnapshotUpdated += ViewModelOnSnapshotUpdated;
        _viewModel.Start();

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            _window.ShowNearTray();
        }
    }

    private void CreateTrayIcon()
    {
        _baseIcon = LoadBaseIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "PWE PC MONITOR",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ToggleWindow();
        };
        UpdateTrayIcon(0, HealthState.Calm);
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Open Dashboard", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var refresh = new Forms.ToolStripMenuItem("Refresh Every");
        foreach (var seconds in new[] { 1d, 2d, 3d, 5d })
        {
            var item = new Forms.ToolStripMenuItem($"{seconds:0} second{(seconds == 1 ? "" : "s")}") { Tag = seconds };
            item.Click += (_, _) => { _viewModel?.SetRefresh((double)item.Tag); RefreshTrayChecks(menu); };
            refresh.DropDownItems.Add(item);
        }
        menu.Items.Add(refresh);

        var theme = new Forms.ToolStripMenuItem("Theme");
        foreach (var value in Enum.GetValues<ThemePreference>())
        {
            var item = new Forms.ToolStripMenuItem(value.ToString()) { Tag = value };
            item.Click += (_, _) => { _viewModel?.SetTheme((ThemePreference)item.Tag); RefreshTrayChecks(menu); };
            theme.DropDownItems.Add(item);
        }
        menu.Items.Add(theme);

        var sensors = new Forms.ToolStripMenuItem("Show All Sensors");
        sensors.Click += (_, _) => { _viewModel?.ToggleSensors(); RefreshTrayChecks(menu); };
        menu.Items.Add(sensors);

        var startup = new Forms.ToolStripMenuItem("Launch at Login");
        startup.Click += (_, _) =>
        {
            StartupService.SetEnabled(!StartupService.IsEnabled);
            RefreshTrayChecks(menu);
        };
        menu.Items.Add(startup);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit PWE PC MONITOR", null, (_, _) => ExitApplication());
        menu.Opening += (_, _) => RefreshTrayChecks(menu);
        return menu;
    }

    private void RefreshTrayChecks(Forms.ContextMenuStrip menu)
    {
        if (_viewModel is null) return;
        foreach (Forms.ToolStripItem root in menu.Items)
        {
            if (root is not Forms.ToolStripMenuItem item) continue;
            if (item.Text == "Refresh Every")
            {
                foreach (Forms.ToolStripMenuItem child in item.DropDownItems)
                    child.Checked = child.Tag is double value && value == _viewModel.RefreshSeconds;
            }
            else if (item.Text == "Theme")
            {
                foreach (Forms.ToolStripMenuItem child in item.DropDownItems)
                    child.Checked = child.Tag is ThemePreference value && value == _viewModel.Theme;
            }
            else if (item.Text == "Show All Sensors") item.Checked = _viewModel.ShowSensors;
            else if (item.Text == "Launch at Login") item.Checked = StartupService.IsEnabled;
        }
    }

    private void ViewModelOnSnapshotUpdated(object? sender, SystemSnapshot snapshot)
    {
        if (_trayIcon is null) return;
        var power = (snapshot.CpuPowerWatts ?? 0) + (snapshot.GpuPowerWatts ?? 0);
        var temperature = snapshot.CpuTemperatureMax ?? snapshot.CpuTemperature;
        var text = $"PWE · CPU {snapshot.CpuUsage:0}% · {power:0} W · {(temperature is > 0 ? $"{temperature:0}°C" : "temp —")}";
        _trayIcon.Text = text[..Math.Min(text.Length, 63)];
        UpdateTrayIcon(snapshot.CpuUsage, snapshot.OverallHealth);
    }

    private void UpdateTrayIcon(double cpuUsage, HealthState health)
    {
        if (_trayIcon is null || _baseIcon is null) return;
        var bucket = (int)Math.Clamp(Math.Round(cpuUsage / 10), 0, 10);
        if (bucket == _lastIconBucket && health == _lastIconHealth) return;
        _lastIconBucket = bucket;
        _lastIconHealth = health;

        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(_baseIcon, new Rectangle(2, 0, 28, 28));
            using var track = new Pen(Color.FromArgb(90, 247, 245, 242), 2);
            var stateColor = health switch
            {
                HealthState.Hot => Color.FromArgb(232, 101, 78),
                HealthState.Warm => Color.FromArgb(245, 179, 53),
                _ => Color.FromArgb(247, 245, 242)
            };
            using var fill = new Pen(stateColor, 2);
            graphics.DrawLine(track, 4, 30, 28, 30);
            graphics.DrawLine(fill, 4f, 30f, (float)(4 + 24 * Math.Max(0.08, cpuUsage / 100)), 30f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            var next = (Icon)Icon.FromHandle(handle).Clone();
            _trayIcon.Icon = next;
            _currentTrayIcon?.Dispose();
            _currentTrayIcon = next;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Bitmap LoadBaseIcon()
    {
        var uri = new Uri("pack://application:,,,/Resources/AppIcon.png", UriKind.Absolute);
        StreamResourceInfo resource = GetResourceStream(uri) ?? throw new InvalidOperationException("App icon resource is missing.");
        using var stream = resource.Stream;
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    private void ToggleWindow()
    {
        if (_window?.IsVisible == true) _window.Hide(); else ShowWindow();
    }

    private void ShowWindow()
    {
        _window?.ShowNearTray();
    }

    public void ExitApplication()
    {
        _window?.AllowClose();
        _window?.Close();
        _viewModel?.Dispose();
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        _currentTrayIcon?.Dispose();
        _baseIcon?.Dispose();
        Shutdown();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
