using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Pwe.PcMonitor.ViewModels;

namespace Pwe.PcMonitor;

public partial class FloatingWindow : Window
{
    private bool _allowClose;

    public FloatingWindow(MonitorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void ShowWidget()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Top + 24;
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
    }

    public void AllowClose() => _allowClose = true;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseWidget_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (DataContext is MonitorViewModel viewModel && viewModel.ShowFloatingWidget)
            viewModel.ToggleFloatingWidget();
    }
}
