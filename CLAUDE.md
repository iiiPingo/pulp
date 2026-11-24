# Pulp

Clipboard history and link cleaner for Windows. Normal resizable window that hides to the tray. C# WinForms, .NET Framework 4.8, no packages.

## Build and test
- `cd src && dotnet build -c Release`. On Linux it builds against Mono's reference assemblies (`mono-devel`). `logo/make_ico.py` regenerates `src/pulp.ico`.
- Version lives in `src/Pulp.csproj` (`<Version>`). Bump it every release; Minibar compares it with the GitHub tag.
- Keep the csproj's Mono overrides inside the PropertyGroup conditioned on `'$(OS)' != 'Windows_NT'`, so it still builds on Windows.
- Release asset: `Pulp-x.y.z.zip` containing `Pulp/Pulp.exe`, `Pulp.exe.config`, `README.txt`, `FONT-LICENSE.txt`.
- Tests: `tests/run.sh` builds the font and zoom tests with Mono and runs them (needs `mono-devel`).

## Gotchas
- History is a SQLite database through Windows' built-in `winsqlite3.dll` (P/Invoke, no packages). Images are stored as PNG with thumbnails. It lives in `%APPDATA%\Pulp`. Never touch or commit it: it holds the user's clipboard history.
- Capture uses `AddClipboardFormatListener`; pasting uses `SendInput`. Skip anything password managers mark as private.
- The link cleaner (`Links.cs`) only removes listed tracking parameters and every cleaned link can be undone. Don't make it remove unknown parameters.
- `Pulp.exe --tray` starts hidden (used by Start with Windows). `Pulp.exe --exit` closes it cleanly (message `Pulp.Exit.v1`). Minibar relies on both.
- The history list uses smooth scrolling and a custom scroll bar (drag or click the track). Keep painting cheap.
- IBM Plex Sans and Plex Sans Arabic are built into `Pulp.exe` (`src/fonts`, SIL Open Font License) and loaded for this process only by `Fonts.cs`, never installed. GDI+ and GDI each need their own registration, so keep both. The zip carries the license as `FONT-LICENSE.txt`.
- History window zoom (`Zoom.cs`): 80% to 150% in 10% steps, by mouse wheel and keyboard, saved in settings.ini. A missing or out of range value falls back to 100%.

## Releasing
When a change is ready to ship, leave it fully done, so the release can go out without rebuilding or repackaging:
1. Bump the version (see Build and test), add the "what's new" to the README and to `README.txt` in the zip (a "NEW IN x.y.z" section at the top, plain words), and run the tests.
2. Build the exe from the final source, last, after the final change. The exe you leave is the one that ships. Copy it to `C:\Claude Projects\Pulp\Pulp.exe` so it can be tried.
3. Build the full release zip `Pulp-x.y.z.zip`: one top folder `Pulp/` with `Pulp.exe`, `Pulp.exe.config`, `README.txt` and `FONT-LICENSE.txt` (the bundled fonts' license). Save it to `C:\Claude Projects\Releases\Pulp-x.y.z\`. A zip with only the exe is not a release zip.
4. Push the branch. Push to `main` when the owner says push or release. Never force-push. Tags are plain versions (`vX.Y.Z`) and are created on GitHub when the owner publishes.
5. Do not create the GitHub release or tag yourself. The owner attaches the zip and clicks Publish. The "My project apps" chat prepares the release page and checks it afterwards; it does not rebuild.
6. Finish with one message: what changed in short plain bullets (these become the release notes, no dashes), the version, the exe's SHA256, the zip path, the tests run, and what only real Windows can check.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Pulp (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, clipboard data, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so. "Push", "release" and "update GitHub" count as that go-ahead: then push to `main`.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (clipboard, paste, tray, drag and drop).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
