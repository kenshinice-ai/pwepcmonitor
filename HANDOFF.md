# PWE PC MONITOR — handoff

This file holds what waits on Lee; PWE Desk reads its `## 等 Lee` section every morning.

## 等 Lee

- **[决定] 从悬浮窗做内存整理不弹确认框，是不是有意的** — 悬浮窗按钮（src/Pwe.PcMonitor/FloatingWindow.xaml.cs:222-226）和 Ctrl+Shift+M（同文件 :257）直接调 `OptimizeMemoryAsync()`，dashboard 路径（MainWindow.xaml.cs:182-189）先确认 · 推荐：有意就在代码里写一句注释说明原因；无意就让两条路径都确认 · 不定则同一个动作两处行为不同 · 自 2026-10-07
- **[决定] Mac 版的正式名：PWE Monitor 还是 PWE MAC MONITOR** — README.md:11 写 "PWE MAC MONITOR"；Mac 版自己的 README 和 .app 都叫 "PWE Monitor"（`../PWE Monitor/README.md:3`）· 推荐：PWE Monitor · 不定则本仓库术语表加不了产品名一行，README 和 Mac 版说法不一致 · 自 2026-10-07
