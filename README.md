# TidyFile

A lightweight Windows tray app that watches your Downloads and Desktop and suggests where each new file should go.

When a new file arrives, a small card appears in the bottom-right corner. It suggests up to 3 destination folders. Click one and the file is moved. Every move is logged, and you can undo it from the tray menu.

## Status

**Phase 1 (MVP)** is done:

- [x] Tray icon with Pause, Recent moves (undo), Edit rules, Edit settings, Start with Windows, Exit
- [x] Watches Downloads and Desktop with change notifications from Windows, so it uses about 0% CPU while idle
- [x] Waits for downloads to finish and ignores partial and temp files (`.crdownload`, `.part`, `~$…`, `.tmp`, shortcuts)
- [x] Rules by file extension and file name pattern, ranked by how specific they are
- [x] Popup with Move, Browse… and Skip; it never steals focus and pauses while your mouse is over it
- [x] Safe moves: never overwrites (uses `name (1).ext`), history log, undo
- [x] Rules reload automatically when you save `rules.json`

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

  If you only have .NET 8, change `<TidyTfm>` in `Directory.Build.props` to `net8.0`.

## Build and run

```powershell
dotnet build
dotnet test
dotnet run --project src/TidyFile.App
```

Or open `TidyFile.sln` in Visual Studio 2022 or later and press F5.

To make a single `.exe` you can copy anywhere:

```powershell
dotnet publish src/TidyFile.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Configuration

Everything lives in `%LOCALAPPDATA%\TidyFile`. The tray menu has **Open TidyFile data folder**.

| File | What it is |
|---|---|
| `settings.json` | Folders to watch, popup duration, number of suggestions. Restart the app after changing it. |
| `rules.json` | Your rules. Changes apply on the next file, no restart needed. |
| `history.jsonl` | One line per move or undo. |
| `tidyfile.log` | Diagnostics. |

### Writing rules

Rules are checked against each new file. A rule that matches both the extension and the name pattern ranks higher than one that only matches the extension.

```jsonc
{
  "version": 1,
  "rules": [
    {
      "name": "Invoices & receipts",
      "extensions": [ "pdf" ],
      "namePattern": "invoice|receipt|bill",   // case-insensitive regular expression
      "destination": "{Documents}\\Finance"
    },
    {
      "name": "Project X",
      "namePattern": "^projx[-_]",             // no extensions = any file type
      "destination": "D:\\Work\\ProjectX"
    }
  ]
}
```

Folder tokens you can use: `{Home}`, `{Desktop}`, `{Documents}`, `{Downloads}`, `{Pictures}`, `{Music}`, `{Videos}`. Environment variables such as `%OneDrive%` also work.

## Project layout

```
src/
  TidyFile.Core/        Rules, file moves, history (no UI; unit tested)
  TidyFile.App/         WPF tray app: watcher, popup, tray menu
tests/
  TidyFile.Core.Tests/  xUnit tests
```

## Roadmap

| Phase | What you get |
|---|---|
| 1. MVP ✅ | Tray app, watchers, rules, popup, history, undo |
| 2. Smart | Use the download website as a signal, learn from your choices, "Always do this", auto-move for trusted rules |
| 3. Cleanup | Scan existing clutter with batch suggestions, find duplicates |
| 4. AI | Optional AI suggestions for files no rule matches, with a privacy switch |
