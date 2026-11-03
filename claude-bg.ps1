# Shows an image behind the Claude desktop window and makes that window semi-transparent.
# Prefer ClaudeBg.exe (build it with build.ps1); this script compiles and runs the same code without it.
# Run with Windows PowerShell 5.1: powershell -STA -WindowStyle Hidden -File claude-bg.ps1
# Everything is set from the tray icon ("Claude background") and saved in settings.ini.
# Exit from the tray icon to restore the window. Ctrl+Alt+B turns the background on and off.

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Path "$PSScriptRoot\ClaudeBg.cs" -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Core
[ClaudeBg]::Run($PSScriptRoot)
