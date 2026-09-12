# Documentation

## User-facing docs

All current documentation lives in the repository root:

- **[README.md](../README.md)** — what the app does, install, usage, build, and troubleshooting.
- **[CHANGELOG.md](../CHANGELOG.md)** — version history (v1.x Vol OSD through v2.0 X3 Vol OSD).

## Sample diagnostics reports

The `VolOsd-diagnostics-*.txt` files in this folder are **historical captures** from **Vol OSD v1.1**
(before the v2.0 rename to X3 Vol OSD). They show the old knob-relay feature, safety trips, and
FxSound-related log noise. They are kept as reference only — they do not reflect the current app,
which is read-only and shows the OSD only when the X3 knob moves.

To generate a current report: run the app → right-click the tray icon → **Save Diagnostics Report...**
