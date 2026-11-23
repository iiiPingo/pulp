# Pulp

Clipboard history and link cleaner for Windows. Normal resizable window that hides to the tray. C# WinForms, .NET Framework 4.8, no packages.

## Build and test
- `cd src && dotnet build -c Release`. On Linux it builds against Mono's reference assemblies (`mono-devel`). `logo/make_ico.py` regenerates `src/pulp.ico`.
- Version lives in `src/Pulp.csproj` (`<Version>`). Bump it every release; Minibar compares it with the GitHub tag.
- Keep the csproj's Mono overrides inside the PropertyGroup conditioned on `'$(OS)' != 'Windows_NT'`, so it still builds on Windows.
- Release asset: `Pulp-x.y.z.zip` containing `Pulp/Pulp.exe`, `Pulp.exe.config`, `README.txt`.

## Gotchas
- History is a SQLite database through Windows' built-in `winsqlite3.dll` (P/Invoke, no packages). Images are stored as PNG with thumbnails. It lives in `%APPDATA%\Pulp`. Never touch or commit it: it holds the user's clipboard history.
- Capture uses `AddClipboardFormatListener`; pasting uses `SendInput`. Skip anything password managers mark as private.
- The link cleaner (`Links.cs`) only removes listed tracking parameters and every cleaned link can be undone. Don't make it remove unknown parameters.
- `Pulp.exe --tray` starts hidden (used by Start with Windows). `Pulp.exe --exit` closes it cleanly (message `Pulp.Exit.v1`). Minibar relies on both.
- The history list uses smooth scrolling and a custom scroll bar (drag or click the track). Keep painting cheap.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Pulp (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, clipboard data, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (clipboard, paste, tray, drag and drop).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
