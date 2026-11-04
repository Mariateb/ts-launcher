# Changelog

## 1.4.0 — 2026-11-04

- Give me a game to play: a new button above your games (or the R key) starts a random game. The less you've played a game, the more likely it comes up: its chance is 1 / (1 + hours played), so a game you've never touched is ten times as likely as one with 9 hours. The last pick is less likely to come up again right away. The game is shown for a second before it starts, and Esc cancels.
- Settings > Random game: choose the language games start in (each game's last language, or one language for all, with the last language as a fallback for games that don't have it), and choose which games are in the draw, with each game's current chance shown.
- Games that are already running are never picked. Games you've never started in any language are skipped, since there's no language to start them in yet.
- Help no longer mentions the Play banner that went away in 1.3.2.

## 1.3.3.1 — 2026-11-03

- The card you're on gets a white outline with a thin dark gap around the art, like Steam, instead of the white glow that spread over the row title and was cut off.
- The scroll arrows at the ends of a row are small round buttons. The dark panels they used to sit on showed as a box around the cards.
- Fixed: smooth scrolling could keep running in the background forever after a row was rebuilt, using CPU and sometimes slowing the launcher down.
- Fixed: pointing at a card with the mouse while another was selected with the keyboard left two cards highlighted. The mouse now takes over the selection, and a row scrolling under a still pointer no longer steals it.
- The blurred background picture is cheaper to draw, so changing games is smoother on modest PCs.
- The build now tests the start-up with sample covers and checks screenshots before each release.

## 1.3.3 — 2026-11-03

The fixes from 1.3.2.1, released again as 1.3.3 so that TS Launcher 1.3.2 offers them as an update. 1.3.2 doesn't recognise four-part version numbers like 1.3.2.1.

- Button hints (A Open, X Play...) only appear when a controller is connected, and the letters are centred in their buttons.
- The last played game uses its cover, a little wider than the other cards, instead of its banner.
- The glow around the card you're on is no longer cut off at the edges of the row.
- The card you're on now pushes its neighbours aside instead of slipping behind them.
- The whole launcher uses the Tahoma font, including the error window.
- Update notes are plain text, so the update window no longer shows Markdown symbols, whichever version you update from.

## 1.3.2.1 — 2026-11-03

Fixes for the new look.

- Button hints (A Open, X Play...) only appear when a controller is connected, and the letters sit centred in their buttons.
- The last played game uses its cover, a little wider than the other cards, instead of its banner.
- The glow around the card you're on is no longer cut off at the edges of the row.
- The card you're on now pushes its neighbours aside instead of slipping behind them.
- The whole launcher uses the Tahoma font.
- Update notes appear as plain text in the update window, without Markdown symbols.
- Updates now understand four-part versions like this one. TS Launcher 1.3.2 doesn't, so 1.3.3 repeats these fixes for it.

## 1.3.2 — 2026-11-03

A new look, inspired by Steam's Big Picture mode. No change to how games, languages or your data work.

- **Navigation on the left**: Library, Languages, Stats, Settings and Help are now in a sidebar instead of the bottom bar. LB / RB on a controller still switch pages.
- **Languages has its own tab**, so renaming, hiding or limiting a language to certain games is no longer buried in Settings.
- **New home**: your last game is shown as a wide tile at the top, followed by the others you played recently. Its name, when you last played, your play time and your language appear underneath.
- **Background picture**: the game you're on shows blurred behind the library (its banner, or its cover). In Settings > Appearance you can turn it off or always use one game.
- **Cleaner cards**: covers show only their art until you land on one. Then it zooms slightly with a white outline, and its name and details appear.
- Simpler filters and sort shown as tabs, flatter buttons, on/off switches in Settings, and button hints at the bottom right, as on a console.

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
