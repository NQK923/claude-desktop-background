# Compiles ClaudeBg.cs into ClaudeBg.exe next to it, with the .NET Framework compiler that ships with Windows.
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:"$PSScriptRoot\ClaudeBg.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" /r:System.Xaml.dll `
    "$PSScriptRoot\ClaudeBg.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
