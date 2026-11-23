<p align="center"><img src="docs/logo.png" width="96" alt="Pulp logo"></p>
<h1 align="center">Pulp</h1>
<p align="center">Clipboard history and link cleaner for Windows.</p>
<p align="center"><a href="../../releases/latest"><b>Download the latest release</b></a></p>

## Features
- **Searchable history** of text, links, files and images/screenshots. Copying the same thing twice just moves it to the top.
- **Ctrl+Alt+V** opens Pulp; Enter pastes into the app you were just using. Shift+Enter pastes as plain text.
- **Filters** (All / Text / Links / Images / Files / Pinned) and **pins** that are never cleaned up.
- **Drag rows out**: images drop as `.png` files into Discord, a browser or Explorer.
- **Link cleaner**: strips tracking parameters (`utm_*`, `fbclid`, `si`, `igsh`...), shortens Amazon and noon product links, and unwraps redirect links (Google, Facebook, Outlook Safe Links, YouTube, Reddit, Steam...). Only listed parameters are removed, and you can undo any cleaned link. Add your own rules in Settings.
- **Private by default**: skips anything password managers mark as private, plus an ignore list (KeePass, 1Password, Bitwarden and others).
- Normal resizable window that hides to the tray, starts with Windows.

## What's new in 1.3.4
- New font: **IBM Plex Sans** for English and **IBM Plex Sans Arabic** for Arabic. They are made as a pair, so English and Arabic clips look like they belong together. The fonts are inside Pulp.exe, nothing gets installed on your PC.
- **Bigger text** in the history list (12 pt instead of 10.5 pt, and the grey line under each clip is bigger too). Rows are a little taller to fit.

## Install
1. Download `Pulp-1.3.4.zip` from [Releases](../../releases/latest) and unzip it into its own folder.
2. Run `Pulp.exe`. If SmartScreen warns, click **More info > Run anyway** (the exe is unsigned).

Runs on Windows 10/11 with nothing to install. History is stored in `%APPDATA%\Pulp`.

## Build
```
cd src
dotnet build -c Release
```
On Linux it builds against Mono's .NET Framework 4.8 reference assemblies (`mono-devel`). `logo/make_ico.py` regenerates `pulp.ico`.

## How it works
C# WinForms on .NET Framework 4.8. SQLite through Windows' own built-in `winsqlite3.dll` (no packages), images stored as PNG with thumbnails, `AddClipboardFormatListener` for capture and `SendInput` for pasting.

## License
MIT. The bundled IBM Plex fonts are under the SIL Open Font License (`src/fonts/OFL.txt`).
