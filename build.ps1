$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "dist"
$exePath = Join-Path $outDir "TourBoxConsolePatch.exe"
$sourcePath = Join-Path $root "Program.cs"
$assemblyInfoPath = Join-Path $root "AssemblyInfo.cs"
$manifestPath = Join-Path $root "app.manifest"
$iconPath = Join-Path $root "assets\app.ico"

$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "Cannot find the .NET Framework C# compiler. Please make sure .NET Framework 4.x is enabled."
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    /codepage:65001 `
    /win32manifest:$manifestPath `
    /win32icon:$iconPath `
    /out:$exePath `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $sourcePath `
    $assemblyInfoPath

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with compiler exit code $LASTEXITCODE."
}

Write-Host "Build completed: $exePath"
