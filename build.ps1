# Compiles the native Living Desk app with the .NET Framework compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$sources = @('ClaudeBg.cs', 'Scenes.cs', 'DeskRuntime.cs', 'DeskUi.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:"$PSScriptRoot\ClaudeBg.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" /r:System.Xaml.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
