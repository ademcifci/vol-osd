# X3 Vol OSD

A tiny tray app that shows a volume on-screen display when you turn the **Sound Blaster X3**
hardware knob. Windows does not show its native volume OSD for that knob because it changes the
card's endpoint volume directly instead of going through the path the native flyout listens for.

The app watches the X3's playback endpoint via the Core Audio API (`IAudioEndpointVolume`) and
draws its own overlay styled like the native Windows flyout. It is **read-only** — it never writes
system volume.

## Requirements

The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64). The installer
checks for it and points you at the download if it's missing.

## Install

Two options, both framework-dependent:

- **`X3VolOsd-<version>-setup.exe`** — installs per-user (no admin prompt) to
  `%LocalAppData%\Programs\X3VolOsd`, adds a Start Menu entry and an uninstaller.
- **`X3VolOsd-<version>-portable.exe`** — a single exe, run it from anywhere. Settings live in
  `%AppData%\X3VolOsd`.

If the .NET runtime is missing, the installer offers to open the download page.

Autostart records the exe's absolute path. If you move the portable exe, the app repoints the entry
the next time you run it.

Upgrading from the old **Vol OSD** app: settings and the debug log are copied automatically from
`%AppData%\VolOsd` on first run if the new folder does not exist yet.

## Run in development

```
dotnet run --project X3VolOsd
```

## Tests

```
dotnet test X3VolOsd.Tests
```

## Build a release

```
.\build.ps1
```

Produces both artifacts in `dist\`. Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) at its
default location. The version comes from `<Version>` in `X3VolOsd.csproj`.

## Usage

- Runs from the tray (speaker icon). No window opens on start.
- Left-click the tray icon for **Settings**, right-click for the menu (Settings, Start with Windows,
  Save Diagnostics Report, Exit).
- Settings: overlay position, size, display duration, theme, colours, and which X3 endpoint is the
  knob device.
- Settings are stored at `%AppData%\X3VolOsd\settings.json`.

On first run, if no knob device is configured, the app auto-selects `SPDIF Out (Sound Blaster X3)` if
present (otherwise any X3 endpoint). The overlay **only** appears when that card's volume changes —
not when you adjust FxSound, headphones, or other devices in Windows.

The X3 driver keeps sibling endpoints (`Speakers` and `SPDIF Out`) in sync; either works in Settings.

If something's not working, right-click the tray icon → **Save Diagnostics Report...** and attach
the text file to a bug report.
