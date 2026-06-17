# TourBox Console Patch

这是一个本机托盘小工具，用来手动重启 TourBox Console。

默认行为：

- 双击托盘图标：重启 TourBox Console。
- 右键托盘图标：可以启动、关闭、重启 TourBox Console，或打开配置和日志。
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

## Windows Defender 和 SmartScreen

这个版本没有数字签名。第一次运行时，Windows 可能会显示未知发布者提醒。

如果 Defender 或 SmartScreen 拦截，请确认程序路径是你自己构建出来的：

```text
dist\TourBoxConsolePatch.exe
```

此工具不会联网，不会注入进程，不会扫描其他目录；它只会在你手动操作托盘图标时启动、关闭或重启配置里的 TourBox Console。
