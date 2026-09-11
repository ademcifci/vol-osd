# Vol OSD

A tiny tray app that shows a volume on-screen display for changes Windows itself
doesn't show one for — e.g. the Sound Blaster X3's hardware knob and Creative's
own mixer, which change the audio endpoint volume directly instead of going
through the `WM_APPCOMMAND` path the native OSD listens for.

It watches every active playback device's volume via the Core Audio API
(`IAudioEndpointVolume`), so it reacts to *any* source of a volume change — and
not just the current default device, since audio-enhancement software can sit in
front of the real hardware. It draws its own overlay styled like the native
Windows flyout.

## Requirements

The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
(x64). The installer checks for it and points you at the download if it's missing.

## Install

Two options, both framework-dependent:

- **`VolOsd-<version>-setup.exe`** — installs per-user (no admin prompt) to
  `%LocalAppData%\Programs\VolOsd`, adds a Start Menu entry and an uninstaller.
  Uninstalling also removes the autostart entry, but leaves your settings alone.
- **`VolOsd-<version>-portable.exe`** — a single ~760 KB exe, run it from
  anywhere. Settings still live in `%AppData%\VolOsd`, so it isn't fully
  self-contained on a USB stick.

If the .NET runtime is missing, the installer offers to open the download page;
the portable exe shows the standard .NET "You must install .NET to run this
application" dialog, which links to the same place.

Autostart records the exe's absolute path. If you move the portable exe, the app
repoints the entry at its new location the next time you run it.

## Run in development

```
dotnet run --project VolOsd
```

## Tests

```
dotnet test VolOsd.Tests
```

Focused on `KnobRelay` (the component with the actual bug history: every past regression there was
a pure logic error, reproducible without touching real hardware via a fake `IAudioDeviceSource`),
plus `Settings` (JSON round-trip, including the enum-as-string regression) and `IconFactory` (the
dropped-glyph and PNG-in-small-icon regressions). `build.ps1` runs this suite and refuses to publish
if it fails.

## Build a release

```
.\build.ps1
```

Produces both artifacts in `dist\`. Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php)
at its default location. The version comes from `<Version>` in `VolOsd.csproj` —
bump it there and both artifacts follow.

To bundle the runtime instead so it runs with no prerequisite at all, publish with
`--self-contained -p:IncludeNativeLibrariesForSelfExtract=true`. That gives a single
~160 MB exe needing no installer and no .NET install.

## Usage

- Runs from the tray (speaker icon). No window opens on start.
- Left-click the tray icon for **Settings**, right-click for the menu
  (Settings, Start with Windows, Save Diagnostics Report, Exit).
- Settings: overlay position, size, display duration, theme, and per-theme
  colours (background, icon & text, bar fill). The bar fill follows your Windows
  accent colour by default.
- Settings are stored at `%AppData%\VolOsd\settings.json`.

If something's not working, right-click the tray icon → **Save Diagnostics
Report...**. It bundles the app/OS/.NET versions, every audio device's current
volume, your settings, and the log into one text file you pick a location for —
attach that to a bug report. Nothing in it is sensitive (device names and volume
levels only).

## Making a hardware knob control the default device

A volume knob on a sound card only moves that card's own volume. If you listen
through something else — headphones, or an enhancer routed elsewhere — the knob
does nothing audible.

Settings → **Knob controls default device** fixes that: pick the card with the
knob, and its *movement* (not its absolute position) is applied to whatever
you're currently listening through instead. Off by default.

It stands down automatically whenever the knob's card *is* what you're listening
through (directly, or via an enhancer that mirrors it) — there the knob already
works, and relaying would fight it. Detection is automatic; there's nothing to
switch when you change outputs.

This only ever reads the knob's own device, never writes to it. An earlier
version did write to it — parking it mid-range so it wouldn't go dead at 0%/100%
while spinning, then restoring it after — which seemed safely contained to a
device nothing was listening through. It was not: audio-enhancement software
(FxSound, specifically) can mirror whatever device it's currently routed to, a
relationship with no discoverable signal, and that parking leaked straight
through the mirror into the real output, audibly. The fix is to never write to
the knob device at all, not to detect that specific case, since the same class
of problem could exist with software this hasn't been tested against.

Caveats worth knowing:

- Cards often expose several endpoints (the X3 has both `Speakers` and
  `SPDIF Out`) and the driver keeps them in sync. They're treated as one unit;
  picking either works.
- Volume moves in the knob's own step size, so it won't always land on the exact
  number you started from.
- Because the knob's device is never written to, turning it past 0% or 100%
  makes it go dead (no further movement to relay) until turned back the other
  way — same as it would with no software involved at all.
- If volume ever moves faster than a real knob could produce, the feature
  disables itself for the rest of the session and shows a tray notification.
  Restart the app to re-enable it.

**Safety.** This feature writes system volume in response to hardware events
with no confirmation step, so a bug here has a worse failure mode than most:
volume running away. Two independent backstops exist on top of the stand-down
logic above:

- Writing to a device with sibling endpoints (e.g. the X3's `Speakers` and
  `SPDIF Out`) can echo back as a notification on the sibling shortly after,
  which would otherwise look exactly like a fresh knob turn. That echo is
  suppressed unconditionally for a short window, regardless of its exact value.
- If volume ever moves faster than any real knob could produce, the feature
  disables itself for the rest of the session — not a timed pause, since that
  could just repeat whatever caused it — and shows a tray notification saying
  so. Restarting the app is what re-enables it.
