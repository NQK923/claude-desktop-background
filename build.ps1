# Compiles ClaudeBg.cs into ClaudeBg.exe next to it, with the .NET Framework compiler that ships with Windows.
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /out:"$PSScriptRoot\ClaudeBg.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "$PSScriptRoot\ClaudeBg.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
