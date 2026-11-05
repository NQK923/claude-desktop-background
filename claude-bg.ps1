# Runs Living Desk: procedural scenes, a companion and focus sessions around Claude on Windows.
# Prefer ClaudeBg.exe (build it with build.ps1); this script compiles and runs the same code without it.
# Run with Windows PowerShell 5.1: powershell -STA -WindowStyle Hidden -File claude-bg.ps1
# Open Scene Studio from the tray icon ("Claude background"); preferences are saved in settings.ini.
# Exit from the tray icon to restore the window. Ctrl+Alt+B turns the background on and off.

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, PresentationCore, WindowsBase, System.Xaml
$sources = @('ClaudeBg.cs', 'Scenes.cs', 'DeskRuntime.cs', 'DeskUi.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Add-Type -Path $sources -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Core, PresentationCore, WindowsBase, System.Xaml
[ClaudeBg]::Run($PSScriptRoot)
