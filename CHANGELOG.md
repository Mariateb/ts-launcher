# Changelog

## 1.2.0 — 2026-11-02

- **Languages per game**: choose which games offer each language, for patches made for one game only (like a Mountain of Faith–only translation).
  - In a game's window, right-click a language: *Only show in this game*, *Don't show in this game*, *Choose games…*, *Rename…*, *Hide everywhere*.
  - In Settings > Languages, the new *Games…* button opens a checklist, with *All games* to undo it.

## 1.1.0 — 2026-11-02

- **Language manager** (Settings > Languages): rename how a language appears, hide the ones you don't use, delete a thcrap configuration (Recycle Bin), and open thcrap to add new ones; the library refreshes when you come back.
- **Quick filters** next to the sort button: All, Played, Never played, Steam, Folder.
- **Remembers the window**: size, position and maximized state, kept on a visible screen.
- **Update notice**: checks GitHub at startup and installs a new version in one click (download, replace, restart). Can be turned off in Settings > About, with a "Check now" button.
- Notices no longer replace each other: a second one waits until the first is closed.

## 1.0 — 2026-11-02

First release of TS Launcher, a full rewrite of the PowerShell "Touhou Launcher" as a compiled C# / WPF application.

- Starts almost instantly; the library scan and cover loading run in the background, covers are decoded at display size and cached.
- Crash protection: readable error dialog with "Keep going", crash reports in `logs/`, safe mode offered after a failed start, single instance.
- Settings safety: atomic writes, `.bak` copies, daily backups in `backups/_launcher/`, automatic recovery of damaged files.
- First-run setup: finds thcrap, optional SteamGridDB key, theme and preferences.
- Everything from the old launcher: rows with favorites, Continue banner, language grid, cover gallery, replays, save backups, stats, themes, sounds, controller, tray and keyboard navigation.
- Reads the old launcher's files, so nothing is lost when switching.
