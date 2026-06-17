# TourBox Console Patch

这是一个本机后台小工具，用来缓解 TourBox Console 在 Clip Studio Paint 使用场景下失灵的问题。

默认行为：

- 当 `CLIPStudioPaint.exe` 正在前台并且你 60 秒没有操作键盘/鼠标时，关闭 TourBox Console。
- 当你曾经切到 Clip Studio Paint，之后 Clip Studio Paint 失去焦点超过 5 秒时，关闭 TourBox Console。
- 当你切回 Clip Studio Paint 时，会确保 TourBox Console 已启动。
- 刚切回 Clip Studio Paint 后有 2 秒宽限，不会因为系统之前已经空闲很久而立刻关闭 TourBox Console。
- 启动 TourBox Console 后会自动隐藏主窗口，不在底部任务栏显示，只保留 TourBox 自己的托盘图标。

## 生成程序

在这个目录里右键空白处打开 PowerShell，然后运行：

```powershell
.\build.ps1
```

生成后的程序在：

```text
dist\TourBoxConsolePatch.exe
```

双击即可运行。运行后会出现在系统托盘区。

程序图标来自 `assets/icon.png`，构建时会嵌入 `assets/app.ico`。

## 开机自启

运行：

```powershell
.\install-startup.ps1
```

取消开机自启：

```powershell
.\uninstall-startup.ps1
```

## 配置

第一次运行后会生成：

```text
dist\TourBoxConsolePatch.ini
```

可调整项目：

```ini
TourBoxPath=C:\Program Files\TourBox Console\TourBox Console.exe
ClipStudioPath=C:\Program Files\CELSYS\CLIP STUDIO 1.5\CLIP STUDIO PAINT\CLIPStudioPaint.exe
IdleSeconds=60
ForegroundGraceSeconds=2
FocusLostDelaySeconds=5
PollIntervalMs=1000
StopOnIdle=True
StopOnFocusLost=True
HideTourBoxWindowAfterStart=True
LogFilePath=%LOCALAPPDATA%\TourBoxConsolePatch\patch.log
```

`HideTourBoxWindowAfterStart=True` 表示工具启动 TourBox Console 后，会自动隐藏 TourBox Console 主窗口，只保留它自己的托盘图标。

修改配置后，需要退出并重新打开 `TourBoxConsolePatch.exe`。

## 日志

托盘图标右键可以打开日志。默认日志位置：

```text
%LOCALAPPDATA%\TourBoxConsolePatch\patch.log
```

## ActivityWatch

此工具没有依赖 ActivityWatch。它直接读取 Windows 当前前台窗口和系统空闲时间，因此即使 ActivityWatch 没有启动也能工作。

## Windows Defender 和 SmartScreen

这个版本没有数字签名。第一次运行时，Windows 可能会显示未知发布者提醒。

如果 Defender 或 SmartScreen 拦截，请确认程序路径是你自己构建出来的：

```text
dist\TourBoxConsolePatch.exe
```

此工具不会联网，不会注入进程，不会扫描其他目录；它只会读取当前前台窗口、读取系统空闲时间，并在条件满足时关闭或启动配置里的 TourBox Console。
