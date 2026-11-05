# Claude Desktop Background — Living Desk

A small native Windows companion for Claude Desktop: animated scenes behind the window, a little desk companion named **Nếp**, and focus sessions that grow a plant over time. Photos and slideshows are still available.

> Unofficial. Not affiliated with or endorsed by Anthropic. The app draws its own background window and changes Claude's window opacity through standard Windows APIs. It does not modify Claude or read conversations.

## Living Desk

Open **Scene Studio** from the tray menu to choose a scene and see it immediately. A new installation opens Studio automatically; no background image or running Claude window is needed to explore the preview.

| Scene | What happens |
| --- | --- |
| **Rainy Study** | Rain on a window, a distant city, a warm desk, steam and a plant that reflects completed focus sessions. |
| **Aquarium** | Fish, rising bubbles, plants and soft underwater light. |
| **Night Train** | A passing landscape, distant lights and a layered train window. |
| **Image** | Your existing photo, with the original dim, blur, tint, alignment and slideshow controls. |

All three scenes are drawn with native GDI+. They work offline, without downloaded assets, a browser runtime or additional packages. Motion is time-based and adjustable; the center is kept quieter for reading.

### Focus, Relax and Present

| Mode | Behavior |
| --- | --- |
| **Focus** | Gentler scene motion and a quieter companion. Start a focus session in Studio or from Nếp's dock. |
| **Relax** | More expressive motion and a livelier companion. |
| **Present** | Makes Claude opaque and hides the scene and companion. The Studio preview remains still. |

Focus sessions default to 25 minutes, with a duration from 5 to 120 minutes. You can pause, resume, reset, or finish a session early. Automatic completion and **Finish** each award one completed session; **Reset** cancels without adding progress. The timer uses an elapsed-time clock, and paused time does not count. Changing the configured duration affects the next session.

Completed sessions are saved in `settings.ini`; the Rainy Study plant and Nếp's celebration reflect that progress. An unfinished running session is not restored after the program exits.

### Nếp and Scene Studio

Nếp is drawn by the app and lives in a separate interactive window beside Claude. It looks toward the pointer, responds to a greeting and celebrates completed sessions. Where a full dock does not fit, it becomes a compact companion that opens Studio when clicked. Its position is kept within the monitor's working area.

Studio provides a live scene preview, mode and motion controls, a switch for animation and the companion, focus duration and timer controls, and quick opacity/dim adjustments. Changes apply immediately and are saved. The existing tray menu remains available for photo-specific controls and system preferences.

Animations stop when they are not needed. Hidden, minimized or unavailable Claude windows do not keep their scene rendering active; an open Studio can still animate its own preview. A running focus session can continue while Claude is hidden. Windows animation preferences and the app's animation switch are respected.

**Upgrading from the photo-only app:** an existing settings file without Living Desk preferences keeps **Image** mode and leaves Nếp disabled. Choose a new scene in Studio whenever you want to switch. New installations start with Rainy Study and Nếp enabled.

## Existing background features

- Backgrounds follow multiple Claude windows through moving, resizing, minimizing, closing and virtual desktop changes.
- Opacity eases between focused and unfocused settings. Window transitions keep the background clipped to avoid flashes around the animating window.
- Matches Windows 11 rounded corners, including square corners when maximized or snapped.
- Optional capture exclusion for the background and companion, and an optional **Start with Windows** setting.
- Loads JPEG, PNG, BMP, GIF and TIFF; other formats depend on installed Windows codecs. Large images are resized on load, and EXIF orientation is applied.
- Separate dim settings for Claude's light and dark themes, plus image blur, tint and alignment.
- Folder slideshows shuffle without repeating an image before the deck is exhausted. Decoding happens in the background; slideshows run only in Image mode and pause when Claude is out of sight.
- `Ctrl+Alt+B` or double-clicking the tray icon enables/disables the effect. Exiting restores Claude's window.

## Requirements

Windows 10/11 with .NET Framework 4.x, which is included with Windows. No other runtime dependencies.

## Install or try a development build

1. Download `ClaudeBg.exe` from a [release](../../releases), or download the **ClaudeBg** artifact from a successful [Build workflow](../../actions/workflows/build.yml) for the branch you want to try. A development branch can contain features that are not in the latest release yet.
2. Put the executable in a folder of its own and run it. On a fresh installation, Scene Studio opens automatically.
3. Open Claude Desktop to see the scene behind it. Use the **Claude background** tray menu to reopen Studio, change preferences or exit. Tick **Start with Windows** if desired.

For a photo, select **Image** and choose a file in Studio, or use **Change image** in the tray menu. An existing `bg.jpg` can still be selected in Image mode.

The executable is not code-signed, so Windows may show a SmartScreen prompt. Release notes include a SHA256 hash for the release executable; you can also build from source.

### Build from source

Run in Windows PowerShell 5.1:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

To build and run the tests, with no Claude window required:

```powershell
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

Tests cover the existing image/window utilities, settings migration and validation, pause/resume and exactly-once focus completion, native scene rendering, companion placement and Studio construction. They write scene and UI preview PNGs to `tests\output`; CI uploads these as **LivingDesk-previews** for visual review.

You can also compile and run through the script:

```powershell
powershell -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File claude-bg.ps1
```

## Commands and settings

| Command | Does |
| --- | --- |
| `ClaudeBg.exe --studio` | Opens Scene Studio, starting the app if necessary. |
| `ClaudeBg.exe --toggle` | Enables/disables the effect, like `Ctrl+Alt+B`. |
| `ClaudeBg.exe --next` | Shows the next slideshow image while in Image mode. |
| `ClaudeBg.exe --restore` | Exits and restores Claude's window; if the app was killed, makes Claude's window opaque again. |

Preferences and progress are saved in `settings.ini` next to the executable. If that folder is not writable, the app uses `%LOCALAPPDATA%\ClaudeBg`; **Open settings folder** shows the actual location. Image and slideshow paths in the settings file may be relative to that folder. Errors go to `claude-bg.log`, which trims itself past 1 MB.

### Source layout

| File | Responsibility |
| --- | --- |
| `ClaudeBg.cs` | Windows integration, attachment lifecycle, settings, image loading and tray menu. |
| `Scenes.cs` | The three procedural scenes and their shared frame data. |
| `DeskRuntime.cs` | Frame scheduling, focus session state and coordination with Claude windows. |
| `DeskUi.cs` | Scene Studio, live preview, Nếp and companion placement. |

## Window transparency limitation

Windows applies opacity to the whole Claude window, including its text. For clearer reading, increase **Opacity while using Claude**, lower motion or dim the background; use **Present** or `Ctrl+Alt+B` when you want full opacity. Scene Studio previews show the scene itself, not a simulation of Claude's actual text rendering.

The scene under Claude is decorative; Nếp and Studio provide their own interactive surfaces. Color-key transparency was tested in the original app and does not work with Claude's GPU-rendered window. Interactive desktop behavior such as real monitor transitions, Claude updates and screen-sharing tools should also be checked on your Windows machine.

## License

[MIT](LICENSE)
