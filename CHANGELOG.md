# Changelog

## 1.0 — 2026-11-02

First release of TS Launcher, a full rewrite of the PowerShell "Touhou Launcher" as a compiled C# / WPF application.

- Starts almost instantly; the library scan and cover loading run in the background, covers are decoded at display size and cached.
- Crash protection: readable error dialog with "Keep going", crash reports in `logs/`, safe mode offered after a failed start, single instance.
- Settings safety: atomic writes, `.bak` copies, daily backups in `backups/_launcher/`, automatic recovery of damaged files.
- First-run setup: finds thcrap, optional SteamGridDB key, theme and preferences.
- Everything from the old launcher: rows with favorites, Continue banner, language grid, cover gallery, replays, save backups, stats, themes, sounds, controller, tray and keyboard navigation.
- Reads the old launcher's files, so nothing is lost when switching.
