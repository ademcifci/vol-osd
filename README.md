# X3 Vol OSD

A tiny tray app that shows a volume on-screen display when you turn the **Sound Blaster X3**
hardware knob. Windows does not show its native volume OSD for that knob because it changes the
card's endpoint volume directly instead of going through the path the native flyout listens for.

The app watches the X3's playback endpoint via the Core Audio API (`IAudioEndpointVolume`) and
draws its own overlay styled like the native Windows flyout. It is **read-only** — it never writes
system volume.

The overlay **only** appears when the configured knob device changes volume. Adjusting FxSound,
headphones, or any other output in Windows does not trigger it.

See [CHANGELOG.md](CHANGELOG.md) for version history.

## Requirements

The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64). The installer
checks for it and points you at the download if it's missing.

## Install

Two options, both framework-dependent (~800 KB each):

| Artifact | What it does |
|----------|----------------|
| **`X3VolOsd-<version>-setup.exe`** | Per-user install (no admin prompt) to `%LocalAppData%\Programs\X3VolOsd`, Start Menu entry, uninstaller. |
| **`X3VolOsd-<version>-portable.exe`** | Single exe — run from anywhere. |

If the .NET runtime is missing, the installer offers to open the download page; the portable exe
shows the standard .NET "You must install .NET" dialog.

**Autostart** records the exe's absolute path. If you move the portable exe, the app repoints the
registry entry the next time you run it.

Uninstalling the setup build removes the program and autostart entry but **leaves your settings** in
`%AppData%\X3VolOsd`.

### Upgrading from Vol OSD (v1.x)

X3 Vol OSD v2.0 replaced the old "knob relay" feature with read-only, knob-triggered OSD only. On
first run, if `%AppData%\X3VolOsd` does not exist yet, settings and the debug log are copied
automatically from `%AppData%\VolOsd`. The autostart registry entry is migrated from `VolOsd` to
`X3VolOsd`.

## Run in development

```
dotnet run --project X3VolOsd
```

Open the solution with `X3VolOsd.sln`.

## Tests

```
dotnet test X3VolOsd.Tests
```

`build.ps1` runs this suite and refuses to publish if it fails. Coverage focuses on
`KnobDeviceHelper`, `Settings` JSON round-trip, `IconFactory`, and `ThemeHelper`.

## Build a release

```
.\build.ps1
```

Produces in `dist\`:

- `X3VolOsd-<version>-portable.exe`
- `X3VolOsd-<version>-setup.exe`

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) at its default location. The version
comes from `<Version>` in `X3VolOsd/X3VolOsd.csproj`.

To bundle the runtime so no prerequisite is needed:

```
dotnet publish X3VolOsd -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

That yields a single ~160 MB exe with no separate installer or .NET install required.

## Usage

- Runs from the tray (speaker icon). No window opens on start.
- **Left-click** the tray icon → Settings.
- **Right-click** → Settings, Start with Windows, Save Diagnostics Report, Exit.

### Settings

| Setting | Purpose |
|---------|---------|
| Position / Theme / Size / Duration | Overlay appearance and how long it stays visible. |
| Colours | Per-theme background, icon & text, and bar fill (bar can follow the Windows accent). |
| **Knob device** | Which X3 playback endpoint triggers the OSD. Auto-detects `SPDIF Out (Sound Blaster X3)` on first run if unset. Pick **None** to disable the overlay entirely. |
| Start with Windows | Per-user autostart via `HKCU\...\Run` (`X3VolOsd`). |

Settings file: `%AppData%\X3VolOsd\settings.json`

### Knob behaviour

- Turning the physical knob always changes the X3 endpoint in Windows (that is the sound card
  driver — this app does not prevent it).
- The OSD shows **that endpoint's volume**, not the Windows default device or what you might be
  listening through elsewhere.
- The X3 driver keeps sibling endpoints (`Speakers` and `SPDIF Out`) in sync; either works in
  Settings.

### Diagnostics

Right-click the tray → **Save Diagnostics Report...** to export a plain-text file containing:

- App, OS, and .NET versions
- All active playback devices with current volume (`[DEFAULT]`, `[KNOB]` markers)
- Your settings JSON
- The debug log

Nothing sensitive is included (device names and volume levels only).

Debug log (append-only, auto-trimmed at 256 KB):

```
%AppData%\X3VolOsd\debug.log
```

To start a clean log before testing, delete that file while the app is not running (or clear its
contents), then restart the app.

## Troubleshooting

| Symptom | Things to check |
|---------|-----------------|
| No OSD when turning the knob | Knob device in Settings matches your X3 endpoint (try `SPDIF Out`). Confirm the knob is not at 0%/100% on the X3 endpoint — at the floor/ceiling the driver may report no further movement. |
| OSD when changing other volumes | Knob device should not be set to a device you change manually. Set to **None** if you only want to disable OSD entirely. |
| Settings lost after reinstall | Uninstall deliberately keeps `%AppData%\X3VolOsd`; only the program folder is removed. |
| Two tray icons | Exit the old `VolOsd.exe` if still running from a v1.x install. |

Attach a diagnostics report when asking for help.

## Project layout

```
X3VolOsd/           Main WPF tray application
X3VolOsd.Tests/     Unit tests
installer/          Inno Setup script (X3VolOsd.iss)
build.ps1           Test, publish, and build both release artifacts
CHANGELOG.md        Version history
docs/               Historical sample diagnostics (v1.1); see docs/README.md
```
