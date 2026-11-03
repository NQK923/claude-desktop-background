# Claude Desktop Background

Puts an image behind the Claude desktop app window on Windows and makes that window semi-transparent, so the image shows through.

> Unofficial. Not affiliated with or endorsed by Anthropic. It does not modify the Claude app: it draws its own window under Claude's and changes that window's opacity through standard Windows APIs.

## Features

- Background image follows the Claude window as it moves, resizes, minimizes and closes (event-driven, no CPU while idle)
- Separate opacity while you use Claude and while you are in another app
- `Ctrl+Alt+B` (or double-click the tray icon) turns it on and off, for screen sharing or reading closely
- Blur, dim and tint (black, dark red, dark blue or any color)
- Separate dim for Windows light and dark mode
- Image position: left/center/right and top/middle/bottom
- Slideshow from a folder, every 5/15/30/60 minutes
- Change or reload the image while it runs; the tray icon is cut from the image
- Restores Claude's window when it exits, crashes or Windows signs out

## Requirements

Windows 10/11 with .NET Framework 4.x (built in). No other dependencies.

## Install

1. Clone or download this repository.
2. Put a background image next to the files, named `bg.jpg` (or pick one later from the tray menu).
3. Build the exe:

   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1
   ```

4. Run `ClaudeBg.exe`. A "Claude background" icon appears in the tray.

To start it with Windows, put a shortcut to `ClaudeBg.exe` in `shell:startup`.

Without building, `claude-bg.ps1` compiles and runs the same code:

```powershell
powershell -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File claude-bg.ps1
```

## Usage

Everything is set from the tray icon's menu and saved in `settings.ini` next to the program. Choose **Exit (restore Claude window)** to quit.

If the process is killed from Task Manager it cannot restore Claude's window; run it again, or restart Claude.

## Limitation

Windows applies opacity to a whole window, so Claude's text becomes as transparent as its background. Use a higher "Opacity while using Claude", more dim or blur, or `Ctrl+Alt+B` when you need the text crisp.

## License

[MIT](LICENSE)
