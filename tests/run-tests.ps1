# Builds ClaudeBg.exe, compiles tests\Tests.cs against it and runs them. Needs no Claude window.
$root = Split-Path $PSScriptRoot
& "$root\build.ps1"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$fw\csc.exe" /nologo /target:exe /out:"$PSScriptRoot\Tests.exe" `
    /r:"$root\ClaudeBg.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& "$PSScriptRoot\Tests.exe" "$root\ClaudeBg.exe"
if ($LASTEXITCODE -ne 0) { throw "$LASTEXITCODE test(s) failed" }
