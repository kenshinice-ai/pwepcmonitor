# v0.8.0 修复执行与验证记录

日期：2026-09-26。对应 [v0.7.2 Review](review-v0.7.2-optimization-plan.md)。保持深蓝/金色翅膀、紧凑图标、悬停详情与不可用项隐藏；不增加风扇控制。

## 修复范围

| Review | 本次实现 | 验证边界 |
| --- | --- | --- |
| R01 存储归属 | 系统卷名称与容量保持一致；未映射物理盘温度/吞吐只保留在 All Sensors，取消错配汇总 | 静态检查；有意不猜测卷到物理盘的映射 |
| R02 GPU 温度 | 去掉原始集合回退；排除无效、热点/显存/板卡温度；按硬件 ID 分组；多 GPU 明确聚合语义 | 合成异常值、次级通道、同名双 GPU 测试；真实厂商设备仍待验收 |
| R03 缺失状态 | CPU/网络可空；失败快照无成功时间；隐藏 CPU 和健康徽标；历史 NaN 缺口由绘图器断开 | 快照/基线测试，Windows ViewModel 检查 |
| R04 生命周期 | 保存采样和内存任务；取消、等待完成后释放硬件，退出前停止 UI 更新 | Windows 回归中反复 Start/Dispose，真实驱动退出仍待实测 |
| R05 悬浮窗 | 统一 HideWidget，清理 Popup/计时器/标记；隐藏时禁止展开；键盘焦点保护、F2/Escape、完成反馈保留 | 隐藏 Popup 回归检查；鼠标、混合 DPI 待实机 |
| R06 内存整理 | 前台事件跟踪；排除当前/原前台；在操作句柄上复核进程创建时间；未能建立跟踪时不执行整理；结果明确为估算 | 进程身份/前台排除纯逻辑测试；实际工作集操作与应用切换待实机 |
| R07 网络 | 基线绑定网卡 ID；稳定选择当前网卡；切换、断线、计数回退重新预热 | 首次、零流量、切换、回退、重连测试 |
| R08 指标颜色 | 电池优先于功耗健康色，与显示值优先级一致 | Windows 8 种 GPU/电池/功耗组合检查 |
| R09 发布检查 | 超时/非零退出/缺失里程碑失败；safe/enhanced 实际采样并释放；打包脚本检查 dotnet 退出码 | CI 执行双回归套件、4 种冒烟结果判断、两种采样模式 |

内存整理保持手动、有上限、同会话范围，不终止进程、不清空系统待机列表。这里的会话检查不等同于验证进程用户 SID。前台变化存在操作系统事件与执行间的正常竞争窗口，执行前再次检查以缩小窗口，不承诺零竞争。

前台跟踪使用 Windows 事件而非新增高频轮询；遵循 [SetWinEventHook 的消息循环要求](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)。身份核验使用同一操作句柄的 [GetProcessTimes 创建时间](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes)。

## 验证命令

```powershell
dotnet build PwePcMonitor.slnx -c Release
dotnet run --project tests/Pwe.Regression -c Release
dotnet run --project tests/Pwe.WindowsRegression -c Release
./scripts/test-smoke-validation.ps1
./scripts/build-windows.ps1 -Runtime win-x64 -PackageVersion v0.8.0
```

本地 macOS：32 项纯逻辑断言通过；应用及 Windows 回归项目 Release 编译通过，0 警告、0 错误。Windows 回归、PowerShell 与打包产物的运行结果以本次提交对应的 GitHub Actions 记录为准，不能以跨平台编译代替。测试项目直接使用生产源文件/项目引用，无新的生产依赖。

## 明确保留的实机验收

- 普通权限、管理员、PawnIO 有/无，各 GPU 厂商的温度来源。
- 多显示器 100%/150%/200% 缩放和屏幕边缘展开。
- 实际工作应用→PWE→整理内存，确认原工作应用受到保护。
- 真实硬件慢采样、睡眠恢复、退出及重新检查传感器。
- 高对比度、减少透明度、系统主题变化与键盘完整路径。

本次只修复已确认逻辑及必要交互，不宣称上述实机验收完成，也不将示意 SVG 当作截图。系统偏好跟随和混合 DPI 坐标重构留待实机证据后处理。
