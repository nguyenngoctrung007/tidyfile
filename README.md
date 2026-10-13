# TidyFile

A lightweight Windows tray app that watches your Downloads and Desktop and suggests where each new file should go.

When a new file arrives, a small card appears in the bottom-right corner. It suggests up to 3 destination folders. Click one and the file is moved. Every move is logged, and you can undo it from the tray menu.

## Status

**Phase 1 (MVP)** is done:

- [x] Tray icon with Settings, Pause, Recent moves (undo), Exit
- [x] Watches Downloads and Desktop with change notifications from Windows, so it uses about 0% CPU while idle
- [x] Waits for downloads to finish and ignores partial and temp files (`.crdownload`, `.part`, `~$…`, `.tmp`, shortcuts)
- [x] Rules by file extension and file name pattern, ranked by how specific they are
- [x] Popup with Move, Browse… and Skip; it never steals focus and pauses while your mouse is over it
- [x] Safe moves: never overwrites (uses `name (1).ext`), history log, undo
- [x] Rules reload automatically when you save `rules.json`

**Phase 1.5** is done:

- [x] Settings window (tray → **Settings…**, or double-click the tray icon)
  - **General:** start with Windows, popup duration, number of suggestions
  - **Watched folders:** add or remove folders; changes apply without a restart
  - **Rules:** add, edit, enable or disable, delete and reorder rules, with a live check of name patterns
  - **Test a file name:** see which rules would match before you save

**Phase 2** is in progress:

- [x] Large moves (e.g. to another drive) run in the background, so the popup and tray never freeze
- [x] Shows which website a download came from ("from github.com"), and rules can match on it
- [ ] Learn from your choices
- [ ] "Always do this" button
- [ ] Move without asking, for rules you trust (opt-in per rule)

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

Use the **Settings** window for everyday changes. Underneath, everything lives in `%LOCALAPPDATA%\TidyFile` (tray menu → **Open TidyFile data folder**), and you can still edit the files by hand.

| File | What it is |
|---|---|
| `settings.json` | Folders to watch, popup duration, number of suggestions. If you edit it by hand, restart the app. |
| `rules.json` | Your rules. Changes apply on the next file, no restart needed. |
| `history.jsonl` | One line per move or undo. |
| `tidyfile.log` | Diagnostics. |

### Writing rules

Rules are checked against each new file. A file must match every condition a rule sets. The more specific the rule, the higher it ranks: extension + name pattern beats name pattern alone, which beats extension alone, and a website makes any rule a little more specific.

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
    },
    {
      "name": "GitHub releases",
      "extensions": [ "zip", "msi" ],
      "sources": [ "github.com" ],             // the website it was downloaded from; sub-sites match too
      "destination": "{Downloads}\\GitHub"
    }
  ]
}
```

The website comes from a note browsers attach to downloaded files. It is read on your PC and never sent anywhere. Not every browser or drive keeps it (FAT/exFAT drives and some private modes don't), and a rule with `sources` never matches a file whose website isn't known.

Folder tokens you can use: `{Home}`, `{Desktop}`, `{Documents}`, `{Downloads}`, `{Pictures}`, `{Music}`, `{Videos}`. Environment variables such as `%OneDrive%` also work.

## Project layout

```
src/
  TidyFile.Core/        Rules, file moves, history (no UI; unit tested)
  TidyFile.App/         WPF tray app: watcher, popup, tray menu
tests/
  TidyFile.Core.Tests/  xUnit tests for the core
  TidyFile.App.Tests/   xUnit tests for the Settings window's view models
```

## Roadmap

| Phase | What you get |
|---|---|
| 1. MVP ✅ | Tray app, watchers, rules, popup, history, undo |
| 2. Smart | Use the download website as a signal, learn from your choices, "Always do this", auto-move for trusted rules |
| 3. Cleanup | Scan existing clutter with batch suggestions, find duplicates |
| 4. AI | Optional AI suggestions for files no rule matches, with a privacy switch |
