using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;
using Pwe.PcMonitor.ViewModels;

namespace Pwe.PcMonitor;

public partial class MainWindow : Window
{
    private readonly MonitorViewModel _viewModel;
    private bool _allowClose;
    private ContextMenu? _settingsMenu;

    public MainWindow(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SetRefreshSelection(viewModel.RefreshSeconds);
    }

    public void ShowNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        Height = Math.Min(860, Math.Max(620, workArea.Height - 26));
        Left = workArea.Right - Width - 13;
        Top = workArea.Bottom - Height - 13;
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void AllowClose() => _allowClose = true;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // The dashboard is a tray popover: clicking the desktop or another
        // window should dismiss it. The settings menu owns focus briefly, so
        // defer the check and keep the popover alive until that menu closes.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (IsVisible && !IsActive && _settingsMenu?.IsOpen != true) Hide();
        }));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } && double.TryParse(value, out var seconds))
        {
            _viewModel.SetRefresh(seconds);
            SetRefreshSelection(seconds);
        }
    }

    private void SetRefreshSelection(double seconds)
    {
        Refresh1.IsChecked = seconds == 1;
        Refresh2.IsChecked = seconds == 2;
        Refresh3.IsChecked = seconds == 3;
        Refresh5.IsChecked = seconds == 5;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateThemeMenu());
        menu.Items.Add(CreateDiagnosticsMenu());
        menu.Items.Add(CreateCheckedItem("Show All Sensors", _viewModel.ShowSensors, () => _viewModel.ToggleSensors()));
        menu.Items.Add(CreateCheckedItem("Show Floating Widget", _viewModel.ShowFloatingWidget, () => ((App)Application.Current).ToggleFloatingWindow()));
        menu.Items.Add(CreateCheckedItem("Launch at Login", StartupService.IsEnabled, () => StartupService.SetEnabled(!StartupService.IsEnabled)));
        menu.Items.Add(CreateActionItem("Get PawnIO installer", ((App)Application.Current).OpenPawnIoInstaller));
        menu.Items.Add(CreateActionItem("Recheck sensor access", ((App)Application.Current).RecheckSensors));
        menu.Items.Add(CreateActionItem("Restart with sensor access", ((App)Application.Current).RestartAsAdministrator));
        menu.Items.Add(CreateActionItem("Open sensor support guide", ((App)Application.Current).OpenSensorGuide));
        menu.Items.Add(new Separator());
        var hide = new System.Windows.Controls.MenuItem { Header = "Hide Dashboard" };
        hide.Click += (_, _) => Hide();
        menu.Items.Add(hide);
        var quit = new System.Windows.Controls.MenuItem { Header = "Quit PWE PC MONITOR" };
        quit.Click += (_, _) => ((App)System.Windows.Application.Current).ExitApplication();
        menu.Items.Add(quit);
        menu.PlacementTarget = SettingsButton;
        _settingsMenu = menu;
        menu.Closed += (_, _) =>
        {
            _settingsMenu = null;
            if (IsVisible && !IsActive) Hide();
        };
        menu.IsOpen = true;
    }

    private System.Windows.Controls.MenuItem CreateDiagnosticsMenu()
    {
        var root = new System.Windows.Controls.MenuItem { Header = "Sensor diagnostics" };
        var diagnostics = _viewModel.SensorDiagnostics;
        if (diagnostics.Count == 0)
        {
            root.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "All displayed channels are readable",
                IsEnabled = false
            });
            return root;
        }

        foreach (var diagnostic in diagnostics)
        {
            root.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = diagnostic,
                IsEnabled = false,
                ToolTip = diagnostic
            });
        }
        return root;
    }

    private System.Windows.Controls.MenuItem CreateThemeMenu()
    {
        var root = new System.Windows.Controls.MenuItem { Header = "Theme" };
        foreach (var theme in Enum.GetValues<ThemePreference>())
        {
            var item = new System.Windows.Controls.MenuItem { Header = theme.ToString(), IsCheckable = true, IsChecked = _viewModel.Theme == theme, Tag = theme };
            item.Click += (_, _) => _viewModel.SetTheme((ThemePreference)item.Tag);
            root.Items.Add(item);
        }
        return root;
    }

    private static System.Windows.Controls.MenuItem CreateCheckedItem(string title, bool value, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = title, IsCheckable = true, IsChecked = value };
        item.Click += (_, _) => action();
        return item;
    }

    private static System.Windows.Controls.MenuItem CreateActionItem(string title, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }

    private void RestartElevated_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).RestartAsAdministrator();

    private void PawnIoInstaller_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).OpenPawnIoInstaller();

    private void RecheckSensors_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).RecheckSensors();
}
