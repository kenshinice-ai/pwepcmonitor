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
    private FloatingWindow? _floatingWindow;
    private MonitorViewModel? _viewModel;
    private AppSettingsService? _settingsService;
    private int _lastIconBucket = -1;
    private HealthState _lastIconHealth = (HealthState)(-1);
    private bool _smokeTest;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.Write("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            ShowFailure("The dashboard encountered an unexpected error. Details were saved to the local diagnostic log.");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppDiagnostics.Write("AppDomain.UnhandledException", exception);
            else AppDiagnostics.Write($"AppDomain.UnhandledException: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var safeMode = e.Args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
        AppDiagnostics.Clear();
        AppDiagnostics.Write($"Starting PWE PC MONITOR {typeof(App).Assembly.GetName().Version}; safeMode={safeMode}; smokeTest={_smokeTest}");

        try
        {
            _settingsService = new AppSettingsService();
            _viewModel = new MonitorViewModel(_settingsService, enableEnhancedSensors: !safeMode);
            _window = new MainWindow(_viewModel);
            _floatingWindow = new FloatingWindow(_viewModel);
            CreateTrayIcon();
            _viewModel.SnapshotUpdated += ViewModelOnSnapshotUpdated;
            _viewModel.Start();

            if (_viewModel.ShowFloatingWidget) _floatingWindow.ShowWidget();

            if (_smokeTest)
            {
                // Hosted Windows runners do not provide an interactive desktop.
                // Constructing the complete app is still useful, but calling
                // Window.Show there can wait on the unavailable shell.
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ExitApplication();
                };
                timer.Start();
            }
            else if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                _window.ShowNearTray();
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Startup failed", exception);
            ShowFailure($"PWE PC MONITOR could not start.\n\n{exception.GetType().Name}: {exception.Message}\n\nDiagnostic log:\n{AppDiagnostics.LogPath}");
            Shutdown(1);
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
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new Forms.ToolStripProfessionalRenderer(new PweColorTable())
        };
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

        var floating = new Forms.ToolStripMenuItem("Show Floating Widget");
        floating.Click += (_, _) => ToggleFloatingWindow();
        menu.Items.Add(floating);

        menu.Items.Add("Get PawnIO installer", null, (_, _) => OpenPawnIoInstaller());
        menu.Items.Add("Recheck sensor access", null, (_, _) => RecheckSensors());
        menu.Items.Add("Restart with sensor access", null, (_, _) => RestartAsAdministrator());
        menu.Items.Add("Open sensor support guide", null, (_, _) => OpenSensorGuide());

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
            else if (item.Text == "Show Floating Widget") item.Checked = _viewModel.ShowFloatingWidget;
            else if (item.Text == "Launch at Login") item.Checked = StartupService.IsEnabled;
        }
    }

    private void ViewModelOnSnapshotUpdated(object? sender, SystemSnapshot snapshot)
    {
        try
        {
            if (_trayIcon is null) return;
            var power = (snapshot.CpuPowerWatts ?? 0) + (snapshot.GpuPowerWatts ?? 0);
            var temperature = snapshot.CpuTemperatureMax ?? snapshot.CpuTemperature;
            var text = $"PWE · CPU {snapshot.CpuUsage:0}% · {power:0} W · {(temperature is > 0 ? $"{temperature:0}°C" : "temp —")}";
            _trayIcon.Text = text[..Math.Min(text.Length, 63)];
            UpdateTrayIcon(snapshot.CpuUsage, snapshot.OverallHealth);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Tray update failed", exception);
        }
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
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/AppIcon.png", UriKind.Absolute);
            StreamResourceInfo resource = GetResourceStream(uri) ?? throw new InvalidOperationException("App icon resource is missing.");
            using var stream = resource.Stream;
            using var loaded = new Bitmap(stream);
            return new Bitmap(loaded);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("Brand icon unavailable; using the Windows fallback icon", exception);
            return SystemIcons.Application.ToBitmap();
        }
    }

    private static void ShowFailure(string message)
    {
        try
        {
            if (!Environment.UserInteractive) return;
            MessageBox.Show(message, "PWE PC MONITOR", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // A message box is best-effort during process startup.
        }
    }

    private void ToggleWindow()
    {
        if (_window?.IsVisible == true) _window.Hide(); else ShowWindow();
    }

    private void ShowWindow()
    {
        _window?.ShowNearTray();
    }

    public void ToggleFloatingWindow()
    {
        if (_viewModel is null || _floatingWindow is null) return;
        _viewModel.ToggleFloatingWidget();
        if (_viewModel.ShowFloatingWidget) _floatingWindow.ShowWidget();
        else _floatingWindow.Hide();
    }

    public void RestartAsAdministrator()
    {
        if (SensorAccessService.IsElevated)
        {
            ShowFailure("PWE PC MONITOR is already running with administrator access.");
            return;
        }

        if (SensorAccessService.TryRestartElevated()) ExitApplication();
        else ShowFailure("Administrator restart was cancelled or unavailable.");
    }

    public void OpenSensorGuide()
    {
        if (!SensorAccessService.OpenGuide()) ShowFailure("The sensor support guide could not be opened.");
    }

    public void OpenPawnIoInstaller()
    {
        if (!SensorAccessService.OpenInstaller()) ShowFailure("The official PawnIO installer could not be opened.");
    }

    public void RecheckSensors()
    {
        _viewModel?.RecheckSensors();
    }

    public void ExitApplication()
    {
        _window?.AllowClose();
        _window?.Close();
        _floatingWindow?.AllowClose();
        _floatingWindow?.Close();
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

    private sealed class PweColorTable : Forms.ProfessionalColorTable
    {
        private static Color Background => ThemeManager.IsDark ? Color.FromArgb(21, 34, 57) : Color.White;
        private static Color Hover => ThemeManager.IsDark ? Color.FromArgb(44, 58, 81) : Color.FromArgb(237, 234, 228);
        private static Color Border => ThemeManager.IsDark ? Color.FromArgb(38, 52, 75) : Color.FromArgb(227, 223, 216);

        public override Color ToolStripDropDownBackground => Background;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
