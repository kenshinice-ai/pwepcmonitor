using System.Reflection;
using System.IO;
using System.Xml.Linq;
using System.Windows.Markup;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pwe.PcMonitor.Controls;
using Pwe.PcMonitor;
using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;
using Pwe.PcMonitor.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Reuse the production resources without starting the production App.
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
        foreach (var element in source.Descendants().Where(element => element.Name.NamespaceName == "clr-namespace:Pwe.PcMonitor.Converters"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:Pwe.PcMonitor.Converters;assembly=PwePcMonitor");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            new XAttribute(XNamespace.Xmlns + "converters", "clr-namespace:Pwe.PcMonitor.Converters;assembly=PwePcMonitor"),
            source.Root!.Element(presentation + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        var vm = new MonitorViewModel(new AppSettingsService(), false);
        var apply = typeof(MonitorViewModel).GetMethod("ApplySnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var count = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            count++;
            Console.WriteLine($"PASS {name}");
        }
        foreach (var gpu in new[] { false, true })
        foreach (var battery in new[] { false, true })
        foreach (var power in new[] { false, true })
        {
            apply.Invoke(vm, [new SystemSnapshot { GpuUsage = gpu ? 10 : null, HasBattery = battery, CpuPowerWatts = power ? 350 : null }]);
            Check(vm.CompactSecondaryLabel == (gpu ? "GPU" : battery ? "BAT" : power ? "POWER" : ""), "Compact metric selection");
            Check(vm.CompactSecondaryHealth == (!gpu && !battery && power ? HealthState.Hot : HealthState.Calm), "Compact metric health follows value");
        }
        apply.Invoke(vm, [new SystemSnapshot { CpuUsage = 50, GpuUsage = 30 }]);
        apply.Invoke(vm, [new SystemSnapshot()]);
        Check(!vm.HasCpuData && vm.CpuValue == "", "Unavailable CPU hidden");
        Check(double.IsNaN(vm.CpuHistory.Last()) && double.IsNaN(vm.GpuHistory.Last()), "History gaps are not zeros");
        apply.Invoke(vm, [new SystemSnapshot { CpuUsage = 0 }]);
        Check(vm.HasCpuUsage && vm.CpuValue == "0%", "Recovered zero CPU visible");
        var widget = new FloatingWindow(vm);
        typeof(FloatingWindow).GetMethod("SetDetailOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, [true]);
        var popup = (Popup)widget.FindName("DetailPopup");
        Check(!popup.IsOpen, "Hidden widget cannot open popup");
        widget.HideWidget();
        Check(!popup.IsOpen, "Hide resets popup");

        ThemeManager.StartFollowingSystem();
        ThemeManager.StartFollowingSystem();
        ThemeManager.StopFollowingSystem();
        Check(true, "System appearance listener starts and stops safely");
        var palette = typeof(ThemeManager).GetMethod("ApplyPalette", BindingFlags.NonPublic | BindingFlags.Static)!;
        palette.Invoke(null, [true, false, false]);
        Check(((SolidColorBrush)app.Resources["WidgetSurfaceBrush"]).Color.A == 255, "Transparency disabled means opaque widget");
        palette.Invoke(null, [true, true, true]);
        Check(((SolidColorBrush)app.Resources["TextBrush"]).Color == SystemColors.WindowTextColor, "High contrast uses system text");
        Check(((SolidColorBrush)app.Resources["WidgetSurfaceBrush"]).Color == SystemColors.WindowColor, "High contrast uses opaque system surface");
        palette.Invoke(null, [true, false, true]);
        Check(((SolidColorBrush)app.Resources["WidgetSurfaceBrush"]).Color.A < 255, "Transparency returns when enabled");

        var animated = new Border { Opacity = 0.35 };
        var fade = new ReversibleFade(animated);
        var obsoleteCompletion = false;
        fade.To(false, true, () => obsoleteCompletion = true);
        Check(Math.Abs(animated.Opacity - 0.35) < 0.02, "Fade starts from displayed value");
        fade.To(true, false);
        Pump(app, TimeSpan.FromMilliseconds(250));
        Check(animated.Opacity == 1 && !obsoleteCompletion, "Reversal invalidates old close callback");
        fade.To(false, false);
        Check(animated.Opacity == 0, "Reduced motion settles immediately");

        apply.Invoke(vm, [new SystemSnapshot
        {
            Timestamp = DateTimeOffset.Now, MachineName = "PWE DEMO", ProcessorName = "Illustrative Windows PC",
            CpuUsage = 7, CpuClockMhz = 2310, CpuTemperature = 63, GpuUsage = 1,
            MemoryTotal = 8_000_000_000, MemoryAvailable = 2_100_000_000,
            DiskTotal = 256_000_000_000, DiskFree = 65_000_000_000, HasBattery = true, BatteryPercent = 100
        }]);
        var compact = (FrameworkElement)widget.FindName("CompactSurface");
        var detail = (FrameworkElement)widget.FindName("DetailSurface");
        // Render controls without a hidden Window imposing its previous layout.
        ((Panel)compact.Parent).Children.Remove(compact);
        popup.Child = null;
        compact.DataContext = vm;
        detail.DataContext = vm;
        detail.Opacity = 1;
        Directory.CreateDirectory("artifacts");
        var fullWidth = Capture(compact, "artifacts/widget-compact-dark.png");
        Capture(detail, "artifacts/widget-detail-dark.png");
        Check(fullWidth > 240 && fullWidth < 290, "Compact full-metric width stays small");
        ThemeManager.Apply(ThemePreference.Light);
        Capture(detail, "artifacts/widget-detail-light.png");
        apply.Invoke(vm, [new SystemSnapshot { CpuUsage = 7 }]);
        var sparseWidth = Capture(compact, "artifacts/widget-compact-sparse.png");
        Check(sparseWidth < fullWidth - 60, $"Unsupported metrics shrink compact width ({fullWidth} to {sparseWidth})");
        ThemeManager.Apply(vm.Theme);
        widget.AllowClose();
        widget.Close();
        vm.DisposeAsync().GetAwaiter().GetResult(); // No sampling task was started.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var running = new MonitorViewModel(new AppSettingsService(), false);
            running.Start();
            var stopped = running.DisposeAsync().AsTask();
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            stopped.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            timeout.Stop();
            Check(stopped.IsCompletedSuccessfully, "Exit waits for sampling without blocking Dispatcher");
        }
        Console.WriteLine($"{count} Windows regression checks passed.");
    }

    private static void Pump(Application app, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static double Capture(FrameworkElement element, string path)
    {
        // A presentation source is required for WPF's visibility/layout coercion.
        // Give each capture an auto-sized host instead of reusing a hidden window.
        var host = new Window
        {
            Content = element, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false
        };
        host.Show();
        try
        {
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Pump(Application.Current, TimeSpan.FromMilliseconds(50));
        // Data changed while this reusable control had no presentation parent.
        // Its child is current, but the detached root may retain its old measure.
        element.InvalidateMeasure();
        host.UpdateLayout();
        Console.WriteLine($"RENDER {path}: host={host.ActualWidth}, element={element.ActualWidth}, desired={element.DesiredSize.Width}");
        LogLayout(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return element.ActualWidth;
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void LogLayout(DependencyObject node, int depth = 0)
    {
        if (node is FrameworkElement e && depth < 4)
            Console.WriteLine($"LAYOUT {depth} {e.GetType().Name} {e.Visibility}: actual={e.ActualWidth}, desired={e.DesiredSize.Width}, width={e.Width}, min={e.MinWidth}");
        if (depth >= 3) return;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            LogLayout(VisualTreeHelper.GetChild(node, i), depth + 1);
    }

}
