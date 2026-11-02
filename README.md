# TS Launcher

A game launcher for the Touhou Project games patched with [thcrap](https://www.thpatch.net/wiki/Touhou_Patch_Center:Download). It finds every game thcrap knows about (folder copies and Steam copies), shows them with proper cover art, and lets you pick the language you want to play in, without hunting for the right shortcut.

## Features

- **One library for everything thcrap knows**: games come from thcrap's `games.js`, languages from your thcrap configurations (`en`, `fr`, `en-lol`, ...), plus any `thXX (lang)` shortcuts you already have.
- **Covers and banners** from [SteamGridDB](https://www.steamgriddb.com) (free API key), preferring the artist of your choice; pick any cover from a gallery or use your own picture.
- **Language grid** for each game, with your last language one click (or one Enter) away, plus game settings mode (`custom.exe`) and "play original".
- **Play time tracking** that follows thcrap's loader and Steam relaunches, a "Now playing" bar, hiding in the tray while you play, a stats page with weekly charts and session history.
- **Save backups**: `score.dat` is copied when a game starts and when it closes (last 10 versions), restorable in two clicks.
- **Replay browser** that reads difficulty, character, result and score from Touhou 10+ replays; rename, back up, delete, import.
- **Favorites, sorting, search and quick filters** (played, never played, Steam, folder), optional cards for the games you don't own yet.
- **Language manager**: give configurations friendly names, hide or delete the ones you don't use, open thcrap to add more.
- **Updates in one click**: a notice appears when a new release is out, and TS Launcher replaces itself and restarts.
- **Remembers its window** size, position and maximized state.
- **Keyboard and Xbox controller navigation**, menu sounds, accent themes (including one that follows each game's colour).
- **Built to be dependable**: crash reports instead of silent failures, safe mode if a start fails, settings and play-time files written atomically with daily backups and automatic recovery.

## Install

1. Download `TSLauncher.zip` from the [latest release](../../releases/latest).
2. Put `TSLauncher.exe` in a folder of its own, ideally next to your `thcrap` folder (it also finds thcrap one level up).
3. Run it. A short setup finds thcrap and optionally asks for your SteamGridDB key.

Requires Windows 10 or 11 (the .NET Framework 4.8 it uses is built in). Windows SmartScreen may warn the first time because the exe isn't code-signed: choose **More info › Run anyway**.

Coming from the old PowerShell "Touhou Launcher"? Put `TSLauncher.exe` where `TouhouLauncher.exe` was: settings, favorites, play time, covers and backups carry over.

## Your data

Everything is stored next to `TSLauncher.exe`; nothing is sent anywhere except cover requests to SteamGridDB (with your own key) and the update check against this repository's releases.

| File / folder | What it holds |
|---|---|
| `launcher-settings.json` | Settings, favorites, last language per game, SteamGridDB key |
| `playtime.json`, `sessions.csv` | Total play time per game, one line per session |
| `covers/` | Downloaded or custom covers (`th17.png`) and banners (`th17_hero.png`) |
| `backups/` | Save backups per game, replay backups, and daily copies of the files above (`_launcher/`) |
| `logs/`, `tracking.log` | Error reports and play-time detection details |

## Building

Open `src/TSLauncher.csproj` in Visual Studio 2022, or with the .NET SDK installed:

```
dotnet build src/TSLauncher.csproj -c Release -o out
```

The output is a single `TSLauncher.exe` (C#, WPF, targeting .NET Framework 4.8). Every push is built on Windows by GitHub Actions.

To release a new version, bump `AssemblyVersion` / `AssemblyFileVersion` (and `AppInfo.Version`) in the source, add an entry to `CHANGELOG.md` and push to `main`: the workflow tags it and publishes the release automatically.

| Source | Purpose |
|---|---|
| `Program.cs` | Entry point, single instance, crash reports, safe mode |
| `Util.cs`, `Data.cs` | Paths, logging, JSON, crash-safe files, settings, play stats |
| `Library.cs` | thcrap, games, languages and shortcuts |
| `Tracking.cs`, `Native.cs` | Launching and timing games, process list, controller, `.lnk` files |
| `Sgdb.cs`, `Extras.cs` | SteamGridDB covers, save backups, replays, sounds |
| `Update.cs` | Update check and in-place update |
| `Ui*.cs`, `Window.xaml` | The interface |

## Credits

- [thcrap](https://github.com/thpatch/thcrap) by the Touhou Patch Center, which does all the actual patching.
- Cover art from [SteamGridDB](https://www.steamgriddb.com) and its artists (telanus's covers are the default preference).
- Touhou Project is © Team Shanghai Alice. This is an unofficial fan tool.

## License

[MIT](LICENSE)
