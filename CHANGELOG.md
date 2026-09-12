# Changelog

All notable changes to this project are documented here. Version numbers match
`<Version>` in `X3VolOsd/X3VolOsd.csproj`.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.0.0] — 2026-09-12

### Changed

- **Rebranded** from Vol OSD to **X3 Vol OSD** — executable, solution, installer
  artifacts, and autostart registry value are now `X3VolOsd`.
- **Read-only OSD only.** The overlay appears when the configured X3 knob endpoint
  changes volume. The app no longer writes system volume.
- **Knob device** setting replaces the old passthrough/relay options. Auto-detects
  `SPDIF Out (Sound Blaster X3)` on first run when unset.
- Diagnostics export filenames are now `X3VolOsd-diagnostics-*.txt`.
- User data folder moved to `%AppData%\X3VolOsd` (settings and `debug.log`).

### Added

- `KnobDeviceHelper` — identifies the knob source and sibling endpoints on the same
  X3 card (e.g. Speakers and SPDIF Out).
- `AppPaths` — migrates settings and debug log from legacy `%AppData%\VolOsd` on
  first run.
- Autostart migration from `VolOsd` to `X3VolOsd` in the registry.
- `KnobDeviceHelperTests` unit tests.

### Removed

- **Knob relay** (`KnobRelay`, `IAudioDeviceSource`, relay tests, FxSound registry
  stand-down logic).
- `KnobPassthroughDeviceId` setting and related Settings UI.
- `docs/knob-relay-fxsound-investigation.md` (relay-era investigation notes).

### Fixed

- Inno Setup upgrade path: second `taskkill` call for legacy `VolOsd.exe` now passes
  the required working-directory argument so the installer script compiles.

### Upgrade notes

If you used Vol OSD v1.x, exit any running `VolOsd.exe` before installing. On first
launch, v2.0 copies your old settings when `%AppData%\X3VolOsd` does not exist yet.
Historical v1.1 diagnostic samples are kept under `docs/` — see `docs/README.md`.

---

## [1.1.0] — 2026-09-12

Vol OSD (pre-rename). Last release that included knob relay.

### Added

- FxSound real-device detection via registry so relay could stand down when FxSound
  mirrored volume to the same hardware endpoint the knob reads.
- Static `KnobPassthroughDeviceId` fallback for other enhancer software.
- `KnobRelayTests` and tighter relay safety limits (rate limiter and longer-window
  budget for sustained drift).
- Relay write logging in the debug log.
- Investigation notes in `docs/knob-relay-fxsound-investigation.md` (removed in v2.0).

### Changed

- `AudioMonitor` and settings UI extended for passthrough/relay configuration.

---

## [1.0.0] — 2026-09-12

Initial release as **Vol OSD**.

### Added

- Tray app with Windows-style volume on-screen display.
- Core Audio monitoring of playback devices via NAudio.
- **Knob relay** — read knob delta on one endpoint and write to the Windows default
  device (with safety trips).
- Settings for position, theme, size, duration, colours, and knob/passthrough devices.
- Per-user autostart, diagnostics report export, and debug log.
- Inno Setup installer and `build.ps1` release pipeline.
- Unit tests for relay, settings, icons, and theme helpers.

[2.0.0]: https://github.com/ademcifci/vol-osd/releases/tag/v2.0.0
[1.1.0]: https://github.com/ademcifci/vol-osd/releases/tag/v1.1.0
[1.0.0]: https://github.com/ademcifci/vol-osd/releases/tag/v1.0.0
