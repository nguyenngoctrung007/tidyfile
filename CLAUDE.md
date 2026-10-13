# CLAUDE.md — TidyFile

Context for Claude Code working in this repo. Keep this file up to date when plans or decisions change.

## What this is

TidyFile is a personal Windows tray app (owner: Trung). It watches folders (default: Downloads, Desktop). When a new file finishes arriving, a small popup in the bottom-right corner suggests up to 3 destination folders. One click moves the file. Every move is logged and can be undone.

Goals, in priority order: **fast and light** (about 0% CPU when idle, small memory), **safe** (never overwrites, never deletes, everything can be undone), **unobtrusive** (the popup never steals focus).

## Build, test, run

```powershell
dotnet build
dotnet test
dotnet run --project src/TidyFile.App
```

- Target framework: `net10.0`, set once in `Directory.Build.props` (`<TidyTfm>`). Changing it to `net8.0` also works.
- Always run `dotnet build` and `dotnet test` after changes, and fix all errors before reporting done.
- User data lives in `%LOCALAPPDATA%\TidyFile`: `settings.json`, `rules.json`, `history.jsonl`, `tidyfile.log`.

## Layout

```
src/TidyFile.Core/        Platform-neutral logic, unit tested. ImplicitUsings ON.
  Rules/                  Rule, RuleEngine (Suggest / Explain / Validate), RulesProvider (auto-reload + Save), DefaultRules
  Files/                  FileFilter (ignore temp/partial files), FileReadiness (wait until written), FileMover (never overwrite)
  History/                HistoryStore (append-only JSONL), MoveService (move + undo, records history)
  Paths/PathTokens.cs     {Downloads} {Desktop} {Documents} {Pictures} {Music} {Videos} {Home} + %ENV%; Expand / Shorten / Tokenize
  Storage/                AppPaths, AppSettings, JsonFile (atomic save, .bad backup on corrupt JSON)
src/TidyFile.App/         WPF app. ImplicitUsings OFF (see pitfalls).
  App.xaml.cs             Startup wiring, single instance, SaveSettings, StartWatcher
  Services/               WatcherService, PopupManager (one popup at a time + queue), TrayIcon (WinForms NotifyIcon), StartupRegistration
  Views/                  SuggestionPopup, SettingsWindow
  ViewModels/             ObservableObject, SettingsViewModel, RuleItem, FolderItem
tests/TidyFile.Core.Tests/  xUnit 2.9.2
tests/TidyFile.App.Tests/   xUnit, net10.0-windows: SettingsViewModel, RuleItem (no windows created)
```

## Key decisions (don't change without a reason)

- **C# / .NET / WPF**, chosen for speed, tray support and a single exe. The tray icon uses **WinForms `NotifyIcon`**, so the app project uses **no NuGet packages**. Keep it that way unless there's a strong reason.
- **FileSystemWatcher**, not polling. Listen to both `Created` and `Renamed`: browsers download to `.crdownload` / `.part` and rename the file at the end.
- **Files that already exist when a watcher starts are "known"** and never prompt. Office's save-via-temp-file therefore doesn't trigger popups.
- **Files TidyFile writes itself** (moves, undo) are announced with `MoveService.ExpectIncoming` → `WatcherService.IgnoreNext`, and cancelled on failure.
- **Ranking:** extension + name pattern = 0.9, name pattern only = 0.8, extension only = 0.6, website only = 0.55; a website condition adds 0.05 (so ext + name + website = 0.95, name + website = 0.85). Every condition a rule sets must match. Ties go to the rule higher in the list.
- **Website matching:** a rule's `sources` match the page domain (`ReferrerUrl`) or the file domain (`HostUrl`, often a CDN), including subdomains (`github.com` matches `objects.github.com`, not `notgithub.com`). Domains are normalized by `DownloadSource.DomainOf` (lower case, no `www.`, full URLs accepted). Duplicate destinations are removed. The folder the file is already in is never suggested.
- **Rule destinations must be fully qualified after expanding tokens.** Store `{Documents}\...` style paths where possible (`PathTokens.Tokenize`).
- **Name patterns are case-insensitive .NET regex with a 200 ms timeout** (guards against ReDoS).
- **History is an append-only JSONL file.** Phase 2 "learning" will read from it.
- **The Settings window edits copies.** Nothing goes live until Save. Changing watched folders recreates the watcher without a restart.
- **UI thread:** watcher events arrive on the thread pool and are marshalled with `Dispatcher.InvokeAsync`. PopupManager, the popups, the tray icon and RulesProvider's `ProblemsFound` must only be used on the UI thread.

## Pitfalls we already hit

- **`out _` inside a lambda with ONE `_` parameter.** In `(_, e) => dict.TryRemove(k, out _)`, the `_` is a real parameter (the sender), so `out _` binds to it and gives CS1503. Name the parameter (`sender`) instead.
- **WPF + WinForms type clashes.** `Application`, `MessageBox`, `Button` and others exist in both. The App project has ImplicitUsings off. Never import `System.Windows` and `System.Windows.Forms` in the same file; fully qualify instead (see `HookUnhandledExceptions`).
- **Errors in WinForms menu handlers** go to `System.Windows.Forms.Application.ThreadException`, not WPF's `DispatcherUnhandledException`. Both are hooked.
- **Visual Studio can save its stale buffer over files changed outside it.** Tell the user to choose "Reload" if VS asks.
- **"Start with Windows" stores the path of the exe that is currently running.** Ticking it while running from `bin\Debug` points Windows at the debug build.
- **A running TidyFile locks its `bin\Debug` DLLs**, so `dotnet build` fails with MSB3027. Exit it from the tray, or build elsewhere: `dotnet build --artifacts-path <folder>` then `dotnet test --no-build --artifacts-path <folder>`.
- **`UseWPF` drops `System.IO` from the implicit usings** (WPF has its own `Path` shape). The App test project adds `<Using Include="System.IO" />`.
- **Tuple element names `Rest` and `ItemN` are reserved** (CS8126).
- **`HistoryStore` caches the file after the first read.** Hand edits to `history.jsonl` while the app runs are not seen until restart. Tests that check reading from disk must use a new `HistoryStore`.

## Status

- **Phase 1 — MVP:** done and committed. Tray app, watchers, rules, popup, history, undo.
- **Phase 1.5 — Settings window:** done and committed. Tabs for General, Watched folders and Rules (editor, ▲▼ reordering, live pattern errors, "Test a file name"). The tray menu now has Settings… (also opened by double-click), Pause, Recent moves, Open data folder and Exit.

### Review items (Phase 1.5)

Fixed:

1. **Corrupt `rules.json`.** `RulesProvider.LoadDocument(out warning)` returns the warning; the Settings window opens on the Rules tab with a red banner saying saving will replace the file.
2. **Half-succeeding save.** `App.SaveSettings` writes both files, then applies the live settings and restarts the watcher; the registry step is last, in its own try/catch that only shows a tray warning.
3. **Unsaved changes.** `SettingsViewModel.IsDirty`; `SettingsWindow.ConfirmClose()` asks "Discard them?" on ✕ / Esc / Cancel. Tray Exit asks first too, because a close during shutdown can't be cancelled.
4. **Rule names.** `RuleEngine.Validate` requires a name (unnamed rules are skipped). `RuleEngine.FindDuplicateNames` feeds a "Save anyway?" warning in Settings; the engine itself doesn't report duplicates.
5. **`TrayIcon.OpenWithShell`** replaced by `OpenDataFolder()` (no Notepad fallback).
6. **Tests for the App view models.** `TidyFile.App.Tests`: dirty tracking, extension parsing, duplicate folders and names, moving/adding/deleting rules, `Validate()`, `ApplyTo`.
- Also fixed: `{Downloads}` uses the real Downloads folder (`KnownFolders`, via `SHGetKnownFolderPath`) instead of assuming `%USERPROFILE%\Downloads`; `WatcherService.IgnoreNext` drops expired entries (moves into unwatched folders never cleared them); an unreadable `settings.json` at startup falls back to defaults with a warning instead of crashing; WFO0003 suppressed (WPF reads DPI awareness from `app.manifest`).

Later:

7. **Debounce the rule tester.** It rebuilds the RuleEngine on every keystroke. Wait about 150 ms after typing stops if the rule list grows large.
8. **History file grows forever.** Reading is fixed (kept in memory since Phase 2.3, learning uses the last 5000 moves), but `history.jsonl` itself is never trimmed. Compact it once it gets large (e.g. > 10 MB).

## Roadmap

- **Phase 2 — Smart.** Agreed with the owner on 2026-10-13. One commit per step, in this order:
  1. ✅ **Moves off the UI thread.** `MoveService.MoveAsync` / `UndoAsync` run on the thread pool, one move at a time (a semaphore, so two moves can't pick the same free name). `OnExit` waits up to 1 minute for a running move.
  2. ✅ **Download source.** `Files/DownloadSource` reads the `Zone.Identifier` alternate data stream (`path + ":Zone.Identifier"`, `HostUrl=` / `ReferrerUrl=`). Show the domain in the popup ("from github.com"), and let rules match on it (`"sources": ["github.com"]`). The domain is a rule condition and a learning signal, **never a suggestion on its own** (browsers don't always write it). It never leaves the PC.
  3. ✅ **Learn from choices.** `Learning/ChoiceLearner` (pure, tested): only past moves of the **same extension** count. Evidence groups: same website (0.70–0.85), shared name word (0.65–0.80), extension only (0.50–0.65), plus up to 0.15 for consistency (share of those moves that went to the folder). Needs **net ≥ 2 moves** (undone moves subtract); **max 0.85**, below an extension + name rule; skips deleted folders and the file's own folder; considers the last 5000 moves. `ChoiceLearner.Merge` combines with rule suggestions; for a shared folder it **keeps the rule's entry** (so `RuleName` is still recorded) with the higher confidence. `HistoryEntry.Source` stores the website. `HistoryStore` loads once and keeps entries in memory.
  4. ✅ **"Always do this".** A checkbox in the popup ("Always do this for PDF files from bank.com"); picking any destination (suggestion or Browse…) with it ticked moves the file, then `RuleFactory.FromExample` builds a rule: the extension, plus the website when known, **never a name pattern** (one example is too little). `RulesProvider.AddToTop` inserts it **at the top** (wins ties with older rules), renames on a name clash, skips an identical enabled rule, and **refuses if rules.json can't be read**. One toast for both; clicking it opens Settings on that rule (`TrayIcon.ShowInfo(..., onClick)`). Known gap: if the Settings window is open with unsaved edits, saving it removes the new rule (the user is told).
  5. ✅ **Auto-move.** `Rule.AutoMove`, **off by default**, ticked per rule in Settings. `RuleEngine.AutoMoveChoice` looks only at the **best rule suggestion** (before merging with learned ones): learned suggestions never auto-move and never block a trusted rule. Decided in `PopupManager.Enqueue` on arrival (not behind an open popup). Toast "Moved automatically … Click to undo" (`PopupManager.UndoRequested` → `App.OnUndoRequested`). If the move fails, the file gets the normal popup instead (`_askAnyway`). Logged like any move.
  6. **Group bursts** into one popup: deferred, revisit after 1–5 are in use.
- **Phase 3 — Cleanup:** scan the existing clutter in watched folders and offer batch suggestions; detect duplicates by hash.
- **Phase 4 — AI (optional):** AI suggestions for files no rule matches. Send only metadata (name, extension, size, domain), never file contents, and add a privacy switch in Settings.

## Working style for this repo

- Keep changes small, and keep Core free of UI so it can be tested.
- Add or adjust xUnit tests for Core changes.
- Update `README.md` (user-facing) and this file (decisions and status) when features land.
- The owner likes concise explanations and checks the staged files before committing. Don't commit or push unless asked.
