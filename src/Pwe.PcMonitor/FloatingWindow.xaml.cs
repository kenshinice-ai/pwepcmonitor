using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Pwe.PcMonitor.ViewModels;

namespace Pwe.PcMonitor;

public partial class FloatingWindow : Window
{
    private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(500);
    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly MonitorViewModel _viewModel;
    private bool _allowClose;
    private bool _compactPointerOver;
    private bool _detailPointerOver;
    private bool _detailOpen;

    public FloatingWindow(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        DetailPopup.PlacementTarget = CompactSurface;
        DetailPopup.CustomPopupPlacementCallback = PlaceDetailPopup;

        _expandTimer = new DispatcherTimer { Interval = ExpandDelay };
        _expandTimer.Tick += ExpandTimer_Tick;
        _collapseTimer = new DispatcherTimer { Interval = CollapseDelay };
        _collapseTimer.Tick += CollapseTimer_Tick;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public void ShowWidget()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        PlaceOnPointerScreen();
    }

    public void AllowClose()
    {
        _allowClose = true;
        StopHoverTimers();
        DetailPopup.IsOpen = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        StopHoverTimers();
        DetailPopup.IsOpen = false;
        if (_allowClose)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CompactSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        _compactPointerOver = true;
        _collapseTimer.Stop();
        if (!_detailOpen)
        {
            _expandTimer.Stop();
            _expandTimer.Start();
        }
    }

    private void CompactSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        _compactPointerOver = false;
        _expandTimer.Stop();
        ScheduleCollapse();
    }

    private void DetailSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        _detailPointerOver = true;
        _collapseTimer.Stop();
    }

    private void DetailSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        _detailPointerOver = false;
        ScheduleCollapse();
    }

    private void ExpandTimer_Tick(object? sender, EventArgs e)
    {
        _expandTimer.Stop();
        if (_compactPointerOver) SetDetailOpen(true);
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_compactPointerOver && !_detailPointerOver && !_viewModel.IsMemoryActionInProgress)
            SetDetailOpen(false);
    }

    private void ScheduleCollapse()
    {
        if (!_detailOpen || _compactPointerOver || _detailPointerOver || _viewModel.IsMemoryActionInProgress)
            return;

        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void SetDetailOpen(bool open)
    {
        if (open)
        {
            _collapseTimer.Stop();
            _detailOpen = true;
            DetailPopup.IsOpen = true;
            return;
        }

        _expandTimer.Stop();
        _collapseTimer.Stop();
        _detailOpen = false;
        DetailPopup.IsOpen = false;
    }

    private void DetailPopup_Opened(object? sender, EventArgs e)
    {
        DetailSurface.Opacity = 0;
        var translate = new TranslateTransform(0, -6);
        DetailSurface.RenderTransform = translate;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        DetailSurface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = easing
        });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = easing
        });
    }

    private void CloseWidget_Click(object sender, RoutedEventArgs e)
    {
        SetDetailOpen(false);
        Hide();
        if (_viewModel.ShowFloatingWidget) _viewModel.ToggleFloatingWidget();
    }

    private async void OptimizeMemory_Click(object sender, RoutedEventArgs e)
    {
        SetDetailOpen(true);
        await _viewModel.OptimizeMemoryAsync();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e) => await TryOptimizeMemoryShortcutAsync(e);

    private async void DetailSurface_PreviewKeyDown(object sender, KeyEventArgs e) => await TryOptimizeMemoryShortcutAsync(e);

    private async Task TryOptimizeMemoryShortcutAsync(KeyEventArgs e)
    {
        var memoryShortcut = ModifierKeys.Control | ModifierKeys.Shift;
        if (e.Key != Key.M || (Keyboard.Modifiers & memoryShortcut) != memoryShortcut || !_viewModel.CanOptimizeMemory)
            return;

        e.Handled = true;
        SetDetailOpen(true);
        await _viewModel.OptimizeMemoryAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MonitorViewModel.IsMemoryActionInProgress)) return;

        if (_viewModel.IsMemoryActionInProgress)
        {
            SetDetailOpen(true);
            return;
        }

        ScheduleCollapse();
    }

    private void PlaceOnPointerScreen()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var workArea = screen.WorkingArea;
        var topLeft = transform?.Transform(new Point(workArea.Left, workArea.Top))
                      ?? new Point(workArea.Left, workArea.Top);
        var bottomRight = transform?.Transform(new Point(workArea.Right, workArea.Bottom))
                          ?? new Point(workArea.Right, workArea.Bottom);

        Left = Math.Max(topLeft.X + 12, bottomRight.X - Width - 24);
        Top = topLeft.Y + 24;
    }

    private static CustomPopupPlacement[] PlaceDetailPopup(Size popupSize, Size targetSize, Point offset)
    {
        const double gap = 6;
        return
        [
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + gap), PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, -popupSize.Height - gap), PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(0, targetSize.Height + gap), PopupPrimaryAxis.Vertical)
        ];
    }

    private void StopHoverTimers()
    {
        _expandTimer.Stop();
        _collapseTimer.Stop();
    }
}
