# Changelog

## 1.3.0 — 2026-11-02

- **thprac support**: a *thprac* button in each game's window (main games 6–20 and Great Fairy Wars). The game starts as usual through thcrap in your language, then `thprac --attach` hooks in. thprac.exe is found automatically near TS Launcher, thcrap or the games, or set in Settings > thprac.
- **Screenshots** in each game's window: thumbnails of the snapshot folder, click to open, right-click to copy, show in folder or delete.
- **Backup folder** (Settings > Backups): keep backups in any folder (OneDrive, Google Drive…), existing ones are copied over.
- **Import backups** from another PC or a copy: adds save and replay backups, optionally restores the newest save of each game (current save backed up first) and merges play time and session history. Restores now put files where this PC keeps the save, even if the backup came from elsewhere.
- **Import Steam play time** for Steam copies, keeping whichever is higher.
- Faster:
  - Covers are kept as small resized copies (`covers/.thumbs`), so they appear almost instantly on every start after the first.
  - Covers are decoded at the size they're shown (card size and screen DPI), and banners at a single size.
  - The cover gallery keeps its lists and thumbnails for the session instead of downloading them again after each pick.
  - Typing in the search bar no longer rebuilds the Continue banner each keystroke.
  - Hiding a language or choosing its games no longer rescans the disk.
  - The "Now playing" bar only updates its timers each second.
  - Without a controller, TS Launcher checks for one every 2 seconds instead of 30 times a second.
  - With the "Per game" theme, colours change after you rest on a game for a moment, not on every card you sweep past.
- Fix: a deleted `playtime.json` or `launcher-settings.json` no longer comes back from the daily backups (only a damaged file is recovered).

## 1.2.1 — 2026-11-02

- Games that aren't from Steam are now labelled **Local** instead of "Folder" (cards, game window, filters).
- Faster:
  - No blurred shadow on every card any more, only on the focused one, so scrolling and hovering redraw much less.
  - While a game is starting, the launcher only looks at new processes instead of opening every process on the PC once a second.
  - The library scan skips TS Launcher's own covers, backups and logs folders (about 14× faster with a few months of backups).
  - A finished cover download redraws the cards instead of rescanning thcrap.
  - Shortcuts are read with one shared helper instead of one per file.
  - Startup code is pre-compiled in the background from the second launch on.

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
