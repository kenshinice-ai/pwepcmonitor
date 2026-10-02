using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Pwe.PcMonitor.ViewModels;
using Pwe.PcMonitor.Controls;
using Pwe.PcMonitor.Services;

namespace Pwe.PcMonitor;

public partial class FloatingWindow : Window
{
    private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(450);
    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly MonitorViewModel _viewModel;
    private readonly ReversibleFade _detailFade;
    private bool _allowClose;
    private bool _compactPointerOver;
    private bool _detailPointerOver;
    private bool _detailOpen;
    private DateTimeOffset _resultVisibleUntil;
    private bool _positioned;

    public FloatingWindow(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _detailFade = new ReversibleFade(DetailSurface);

        DetailPopup.PlacementTarget = CompactSurface;
        DetailPopup.CustomPopupPlacementCallback = PlaceDetailPopup;

        _expandTimer = new DispatcherTimer { Interval = ExpandDelay };
        _expandTimer.Tick += ExpandTimer_Tick;
        _collapseTimer = new DispatcherTimer { Interval = CollapseDelay };
        _collapseTimer.Tick += CollapseTimer_Tick;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DetailSurface.LostKeyboardFocus += (_, _) => ScheduleCollapse();
        ThemeManager.AppearanceChanged += AppearanceChanged;
        SizeChanged += (_, e) =>
        {
            // Keep the right edge anchored when readable metrics appear/disappear.
            if (_positioned && e.WidthChanged) Left -= e.NewSize.Width - e.PreviousSize.Width;
        };
    }

    public void ShowWidget()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        UpdateLayout();
        PlaceOnPointerScreen();
    }

    public void AllowClose()
    {
        _allowClose = true;
        ResetDisclosure();
    }

    public void HideWidget()
    {
        ResetDisclosure();
        _compactPointerOver = false;
        _detailPointerOver = false;
        _resultVisibleUntil = default;
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        ResetDisclosure();
        if (_allowClose)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ThemeManager.AppearanceChanged -= AppearanceChanged;
            return;
        }

        e.Cancel = true;
        HideWidget();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.Handled) return;
        var start = e.GetPosition(this);
        // Let a click remain a click; enter native drag only after the system threshold.
        _dragOrigin = start;
    }

    private Point? _dragOrigin;

    private void CompactSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragOrigin = null; return; }
        if (_dragOrigin is not Point start) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragOrigin = null;
        ResetDisclosure();
        DragMove();
    }

    private void CompactSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        _compactPointerOver = true;
        _collapseTimer.Stop();
        if (DetailPopup.IsOpen && !_detailOpen) { SetDetailOpen(true); return; }
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
        if (!_detailOpen) SetDetailOpen(true);
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
        if (DateTimeOffset.UtcNow < _resultVisibleUntil)
        {
            ScheduleCollapse();
            return;
        }
        if (!_compactPointerOver && !_detailPointerOver && !DetailSurface.IsKeyboardFocusWithin && !_viewModel.IsMemoryActionInProgress)
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
            if (!IsVisible || !_viewModel.ShowFloatingWidget || _allowClose) return;
            _collapseTimer.Stop();
            _detailOpen = true;
            if (DetailPopup.IsOpen)
            {
                _detailFade.To(true, ThemeManager.AnimationsEnabled);
                return;
            }
            DetailSurface.Opacity = 0;
            DetailPopup.IsOpen = true;
            return;
        }

        _expandTimer.Stop();
        _collapseTimer.Stop();
        _detailOpen = false;
        _detailFade.To(false, DetailPopup.IsOpen && ThemeManager.AnimationsEnabled,
            () => { if (!_detailOpen) DetailPopup.IsOpen = false; });
    }

    private void DetailPopup_Opened(object? sender, EventArgs e)
    {
        _detailFade.To(true, ThemeManager.AnimationsEnabled);
    }

    private void AppearanceChanged(object? sender, EventArgs e)
    {
        if (!ThemeManager.AnimationsEnabled && DetailPopup.IsOpen)
            _detailFade.To(_detailOpen, false, () => { if (!_detailOpen) DetailPopup.IsOpen = false; });
    }

    private void ResetDisclosure()
    {
        StopHoverTimers();
        _detailOpen = false;
        _detailFade.To(false, false);
        DetailPopup.IsOpen = false;
        _dragOrigin = null;
    }

    private void CloseWidget_Click(object sender, RoutedEventArgs e)
    {
        SetDetailOpen(false);
        HideWidget();
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
        if (e.Key == Key.Escape && _detailOpen)
        {
            e.Handled = true;
            SetDetailOpen(false);
            Focus();
            return;
        }
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            SetDetailOpen(true);
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_detailOpen) DetailSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
            return;
        }
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

        if (_detailOpen) _resultVisibleUntil = DateTimeOffset.UtcNow.AddSeconds(4);
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

        Left = Math.Max(topLeft.X + 12, bottomRight.X - ActualWidth - 24);
        Top = topLeft.Y + 24;
        _positioned = true;
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
