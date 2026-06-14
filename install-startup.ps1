$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $root "dist\TourBoxConsolePatch.exe"

if (-not (Test-Path $exePath)) {
    & (Join-Path $root "build.ps1")
}

$startup = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startup "TourBox Console Patch.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = Split-Path -Parent $exePath
$shortcut.Description = "Restart TourBox Console when Clip Studio Paint is idle or loses focus"
$shortcut.Save()

Write-Host "Startup shortcut created: $shortcutPath"
