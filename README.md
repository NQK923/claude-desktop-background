# Claude Desktop Background

Puts an image behind the Claude desktop app window on Windows and makes that window semi-transparent, so the image shows through.

> Unofficial. Not affiliated with or endorsed by Anthropic. It does not modify the Claude app: it draws its own window under Claude's and changes that window's opacity through standard Windows APIs.

## Features

- Background image follows each Claude window as it moves, resizes, minimizes, closes or changes virtual desktop (event-driven)
- Works with several Claude windows at once
- Smooth: attaches the moment Claude opens, fades opacity instead of jumping, and keeps the image out of sight while Windows animates opening, restoring, maximizing, snapping or dragging the window, so it never shows around the animating window. Claude is opaque whenever its image is not behind it. Respects Windows' "Animation effects" setting
- Option to keep the image out of screenshots and screen sharing (Teams, Zoom, Snipping Tool)
- Start with Windows from the tray menu
- Loads JPEG, PNG, BMP, GIF and TIFF; WebP, HEIC, AVIF and JPEG XL when Windows has their codec (free "... Image Extensions" in the Microsoft Store). Large photos are shrunk to your screen size on load
- Separate opacity while you use Claude and while you are in another app
- `Ctrl+Alt+B` (or double-click the tray icon) turns it on and off, for screen sharing or reading closely
- Blur, dim and tint (black, dark red, dark blue or any color)
- Separate dim for Claude's light and dark theme, detected from Claude's own window
- Image position: left/center/right and top/middle/bottom
- Slideshow from a folder, every 5/15/30/60 minutes
- Change or reload the image while it runs; the tray icon is cut from the image
- Restores Claude's window when it exits, crashes or Windows signs out
- Command line `--toggle`, `--next` and `--restore` for other hotkey tools or a Stream Deck

## Requirements

Windows 10/11 with .NET Framework 4.x (built in). No other dependencies.

## Install

1. Download `ClaudeBg.exe` from the [latest release](../../releases/latest) into a folder of its own.
2. Put a background image next to it named `bg.jpg`, or pick one later from the tray menu.
3. Run `ClaudeBg.exe`. A "Claude background" icon appears in the tray. Tick **Start with Windows** in its menu to keep it.

The exe is not code-signed, so Windows SmartScreen may warn on first run ("More info", then "Run anyway"). Each release lists the file's SHA256 so you can check the download, or build it yourself from source.

### Build it yourself

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Without building, `claude-bg.ps1` compiles and runs the same code:

```powershell
powershell -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File claude-bg.ps1
```

## Usage

Everything is set from the tray icon's menu and saved in `settings.ini` next to the program. Choose **Exit (restore Claude window)** to quit.

| Command | Does |
| --- | --- |
| `ClaudeBg.exe --toggle` | Turns the background on or off, like `Ctrl+Alt+B` |
| `ClaudeBg.exe --next` | Shows the next slideshow image |
| `ClaudeBg.exe --restore` | Exits and restores Claude's window; if the program was killed, makes Claude's window opaque again |

Errors are written to `claude-bg.log` next to the program.

## Limitation

Windows applies opacity to a whole window, so Claude's text becomes as transparent as its background. Use a higher "Opacity while using Claude", more dim or blur, or `Ctrl+Alt+B` when you need the text crisp.

Color-key transparency (only the background color see-through) was tested and does not work: Claude draws with the GPU, and Windows ignores a color key for such windows.

## License

[MIT](LICENSE)
