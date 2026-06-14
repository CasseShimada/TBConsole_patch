$ErrorActionPreference = "Stop"

$startup = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startup "TourBox Console Patch.lnk"

if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath
    Write-Host "Startup shortcut removed: $shortcutPath"
} else {
    Write-Host "Startup shortcut was not found."
}
