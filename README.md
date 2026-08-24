<div align="center">

# PWE PC MONITOR

**A calm Windows hardware monitor from Paradise Production.**

First runnable version: **0.1.0**

</div>

PWE PC MONITOR is the Windows companion to PWE MAC MONITOR. It keeps the wing mark, navy/amber palette, typography, calm/warm/hot status language, compact dashboard and 1–5 second sampling rhythm while using Windows-native telemetry and LibreHardwareMonitor.

## Current features

- Windows notification-area icon with CPU activity rule and health colour.
- Left-click dashboard; right-click settings and quit menu.
- CPU usage, frequency, temperature, package power and per-core bars when available.
- GPU usage, frequency, temperature and power when available.
- Memory, system drive capacity, network throughput/address, battery and top processes.
- Read-only fan RPM display. **There is no fan-control or hardware-write path.**
- 89-sample CPU, GPU and power history.
- System, dark and light themes using the PWE brand palette.
- 1, 2, 3 or 5 second refresh intervals.
- Optional full sensor list.
- Launch-at-login setting stored for the current Windows user.
- Graceful fallback to basic Windows metrics if enhanced hardware sensors are unavailable.

## Requirements

- Windows 10 version 2004 or later, or Windows 11.
- x64 PC for the first packaged build.
- Some motherboard, CPU-temperature and fan sensors require LibreHardwareMonitor's PawnIO hardware-access layer and may not be exposed by every PC or laptop.

Missing or unsupported measurements are shown as `—`; the app does not invent or estimate unavailable sensor values.

## Download a public Windows build

Use the repository's [Releases](https://github.com/kenshinice-ai/pwepcmonitor/releases) page. Each release includes:

- `pwe-pc-monitor-win-x64-<version>.zip`, a self-contained Windows x64 build;
- `pwe-pc-monitor-win-x64-<version>.sha256`, the SHA-256 checksum.

The `Actions` artifact is kept as a short-lived CI diagnostic (30 days). Releases are the stable public download path.

## Run a packaged build

1. Download the versioned zip from the public Releases page.
2. Extract the archive to a normal user-writable folder.
3. Run `PwePcMonitor.exe`.
4. Look for the PWE wing in the Windows notification area. Windows may place a new icon in the overflow menu until it is pinned.

The published artifact is self-contained and does not require a separate .NET installation.

If Windows SmartScreen shows an unknown-publisher warning, choose **More info** and verify that the file came from the PWE PC MONITOR GitHub Release. The first public builds are not Authenticode-signed yet.

If a machine has a hardware-specific sensor problem, start `PwePcMonitor.exe --safe-mode` once. Safe mode keeps the dashboard and Windows-native CPU, memory, disk, network, battery and process readings while skipping enhanced sensor-driver access. The app writes startup and sampling diagnostics to `%LOCALAPPDATA%\PWE\PC Monitor\logs\latest.log`.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet restore PwePcMonitor.slnx
dotnet build PwePcMonitor.slnx --configuration Release
./scripts/build-windows.ps1 -Runtime win-x64 -PackageVersion dev
```

The publish output is written to `artifacts/win-x64`; the zip and checksum are written to `artifacts/`.

## Architecture

```text
src/Pwe.PcMonitor/
  Models/                 snapshots, readings and health rules
  Services/
    WindowsMetricsReader  basic CPU, memory, disk, network, battery and process data
    SystemSampler         read-only LibreHardwareMonitor adapter and graceful fallback
    ThemeManager          brand palette and system theme selection
    StartupService        current-user launch-at-login entry
  ViewModels/             sampling loop, history and formatted UI state
  Controls/               lightweight sparkline renderer
  App.*                   tray lifecycle and dynamic icon
  MainWindow.*            dashboard and settings surface
```

The application runs as the signed-in user (`asInvoker`). It does not contain a privileged helper, does not write fan controls, and does not transmit telemetry.

## Health colours

- **Calm:** normal text colour. No attention colour is spent on a normal reading.
- **Warm:** PWE amber.
- **Hot:** coral, reserved for a genuine warning.

CPU/GPU temperature defaults are warm at 75 °C and hot at 92 °C. Storage temperature defaults are warm at 55 °C and hot at 68 °C. These are presentation thresholds only; they do not change hardware behaviour.

## Verification boundary

The solution can be compiled from macOS with Windows targeting enabled. The GitHub Actions workflow performs the authoritative Windows build and launches the published EXE in a five-second safe-mode smoke test. The notification-area UI, Windows APIs, enhanced sensor access, sleep/resume behaviour and hardware compatibility still need verification on real Windows PCs.

## Licences and brand

Source code is MIT licensed. LibreHardwareMonitor is MPL-2.0. Bundled fonts use the SIL Open Font License 1.1. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PWE names, the Paradise wing mark and related brand assets are not granted under the MIT source-code licence. See [LICENSE](LICENSE).
