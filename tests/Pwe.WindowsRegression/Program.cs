using System.Reflection;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Pwe.PcMonitor;
using Pwe.PcMonitor.Models;
using Pwe.PcMonitor.Services;
using Pwe.PcMonitor.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.InitializeComponent();
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
}
