# Builds and tests the app, including native scene previews. Needs no Claude window.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& "$root\build.ps1"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$sources = Get-ChildItem "$PSScriptRoot\*.cs" | ForEach-Object { $_.FullName }
# Keep the test executable beside the app so direct references resolve without a custom loader.
& "$fw\csc.exe" /nologo /codepage:65001 /target:exe /out:"$root\Tests.exe" `
    /r:"$root\ClaudeBg.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& "$root\Tests.exe" "$root\ClaudeBg.exe" "$PSScriptRoot\output"
if ($LASTEXITCODE -ne 0) { throw "$LASTEXITCODE test(s) failed" }
