# Create workspace — design

Date: 2026-11-10 · Status: approved in chat, awaiting spec review · Owner: Trung

## Purpose

Give developers a standard, clean folder structure in one step:

- **New setup:** someone starting fresh gets a ready-to-use workspace.
- **Gradual migration:** someone with a messy setup gets a clean structure first, drops old folders into `Inbox`, and sorts them over time with TidyFile's suggestions.

TidyFile is used only by the owner for now. The design must not block offering it to others later, but builds nothing just for that (one built-in template, no template files, no command line).

This replaces the "Starter layouts" roadmap candidate in CLAUDE.md.

## Decisions (agreed in chat)

| Topic | Decision |
|---|---|
| Structure | One built-in **developer** template, two levels, plain names (no numbering), sample sub-folders included |
| Entry point | Tray menu → **Create workspace…** opens a small window |
| TidyFile integration | Yes, through two checkboxes, both ticked by default: **watch Inbox** and **suggest into the workspace** |
| Safety | Only creates what is missing. Never overwrites, moves or deletes the user's files. **Undo last setup** removes only what it created and is still untouched |
| Open/Delete buttons | None. Undo replaces Delete; the "created" notification opens the folder |

## The template

Base folder default: `D:\Workspace` if drive D: exists, else `%USERPROFILE%\Workspace`.

```
<base>\
  Inbox\
  Projects\
    Work\
    Personal\
  Sandbox\
  Resources\
    Books\
    Courses\
    Tools\
    Assets\
  Archive\
    <current year>\        e.g. 2026, fixed at creation time
  README.md
```

12 folders inside `<base>`, plus `<base>` itself when it doesn't exist yet, and one file.

### README.md content

Written once, never overwritten. `<base>` is replaced by the chosen base folder; in the git snippet it uses forward slashes.

````markdown
# Workspace

| Folder | Put here |
|---|---|
| `Inbox` | Anything not sorted yet. TidyFile watches this folder: files you put here get folder suggestions. Moving from an old setup? Move files in a few at a time and let TidyFile suggest where each one goes. |
| `Projects\Work` | Repositories for your job |
| `Projects\Personal` | Your own repositories |
| `Sandbox` | Experiments, demos, throwaway code |
| `Resources` | `Books`, `Courses`, `Tools` (installers, portable tools), `Assets` (images, icons, design files) |
| `Archive` | Finished things, one folder per year |

## Tips

- Keep repositories shallow (`Projects\Work\my-repo`). Windows limits paths to 260 characters, and `node_modules` hits that quickly in deep folders.
- Use names without spaces. Spaces break some scripts and build tools.

## Different git email for work and personal repos

Add this to `~/.gitconfig`:

```ini
[includeIf "gitdir/i:<base>/Projects/Work/"]
    path = ~/.gitconfig-work
```

and put your work identity in `~/.gitconfig-work`:

```ini
[user]
    email = you@company.com
```

Repositories under `Projects\Work` then commit with your work email; everything else keeps your default one.
````

## User experience

**Window "Create workspace"**

- **Base folder:** a text box plus Browse…. The preview updates as the path changes.
- **Preview:** read-only list of every folder and the README. Items that already exist are marked "already exists, kept".
- **Watch Inbox:** a checkbox, ticked by default.
- **Suggest into the workspace:** a checkbox, ticked by default. It shows what it changes: "Installers → Resources\Tools, add rule Ebooks → Resources\Books". It notes "Installers rule not found, skipped" when that applies.
- **Create** is disabled when the base folder isn't a full path or its drive doesn't exist.
- **Undo last setup** is visible only when a setup record exists. It shows that setup's base folder and date.
- **Previous setup warning:** if a record exists and the user clicks Create, the window asks first: "The previous setup can no longer be undone. Continue?"

**After Create:** a notification "Workspace created in D:\Workspace. Click to open." If anything was skipped or failed, the notification says so.

**After Undo:** a notification listing what was kept, e.g. "Undone. Kept 2 folders that contain files: Projects\Work, Inbox".

**Settings window open:** both Create and Undo first ask the user to close Settings. Settings holds a copy of the rules and settings, and saving it would overwrite the changes.

## Behaviour

### Create, in order

Each step records only what it actually did.

1. **Folders:** create the missing ones, parents first. Existing folders are untouched.
2. **README.md:** write it only if no file exists at that path. Record a SHA-256 of the written content.
3. **Watch Inbox** (if ticked): add `<base>\Inbox` to `settings.WatchFolders` unless an entry already expands to the same full path (case-insensitive, trailing separator ignored). Save `settings.json`, apply to the live settings, restart the watcher.
4. **Suggest into the workspace** (if ticked), as one read-modify-write of `rules.json`:
   - **Installers redirect.** Target: the first rule that is enabled, has no name pattern and no sources, and has `exe` among its extensions. Identified by content, not by name. Record its name, old destination and new destination (`<base>\Resources\Tools`, run through `PathTokens.Tokenize`), then change the destination. If no such rule exists, skip it and say so in the preview.
   - **Ebooks rule.** Add `{ name: "Ebooks", extensions: [epub, mobi], destination: <base>\Resources\Books }` at the **bottom** of the list, with a unique name (` (2)` suffix on a clash). Skip if an enabled rule already has `epub` among its extensions. Record the final name.
   - If `rules.json` can't be read, skip this step, keep the file untouched, and report it.
5. **Record:** save `workspace-setup.json` in the data folder (`AppPaths.WorkspaceSetupFile`). It is saved even after a failure, as long as something was created.

### Undo, in order

Every step only reverts what is still exactly as TidyFile left it.

| Step | Reverts | Kept when… |
|---|---|---|
| Ebooks rule | Removes the rule with the recorded name | it no longer has exactly the recorded extensions and destination |
| Installers redirect | Restores the old destination | the rule is gone or its destination is no longer the recorded new one |
| Watch Inbox | Removes the entry from `WatchFolders`; saves; restarts the watcher | — |
| README.md | Deletes the file | its SHA-256 differs from the recorded one |
| Folders | Deletes recorded folders, deepest first, with a **non-recursive** delete | the folder is not empty |

After Undo the record is deleted. The non-recursive delete is the safety net: Windows refuses to delete a non-empty folder, so user files can never be removed.

### Errors

- **Folder creation fails** (no permission, drive removed): stop, keep and save the record of what was created, report the error. Undo can still clean up.
- **Saving settings or rules fails:** the folders stay. The record covers only what was applied, and the error is reported.
- **The record file is unreadable:** treat it as no record (no Undo button) and log a warning.

## Components

Core (`src/TidyFile.Core/Workspace/`, unit tested, no UI):

| Unit | Responsibility |
|---|---|
| `WorkspaceTemplate` | Built-in developer template as data: relative folder paths (with a `{Year}` placeholder), README text, rule changes |
| `WorkspacePlanner` | Computes a `WorkspacePlan` from the template, base folder, current settings and rules, and the year: folders to create vs existing, README, whether Inbox is already watched, the Installers rule found or not, whether Ebooks is needed. Reads only; drives the preview |
| `WorkspaceSetup` | `Apply(plan, options)` creates folders and the README and edits copies of settings and rules, returning the `WorkspaceRecord`. `Undo(record, settings, rules)` returns what was kept. Saving settings and rules and restarting the watcher stay in App |
| `WorkspaceRecord` | The record plus its JSON load and save (`JsonFile`) |

`AppPaths` gains `WorkspaceSetupFile` (`workspace-setup.json`).

App:

| Unit | Responsibility |
|---|---|
| `WorkspaceWindow` + `WorkspaceViewModel` | The window above. The view model holds the base folder, preview lines, the two checkboxes, `CanCreate` and the undo info |
| `TrayIcon` | Menu item "Create workspace…" under Settings…, raising `WorkspaceRequested` |
| `App` | Opens the window (blocked while Settings is open). Applies and undoes by calling Core, then saves settings and rules, updates the live settings and restarts the watcher, reusing the existing `SaveSettings` / `StartWatcher` logic |

## Testing

Core (xUnit, `TempFolder`):

- The plan on an empty base, and on a base where some folders and the README already exist.
- Apply creates only missing folders and never overwrites an existing README.
- The Installers rule is found by content (also when renamed), and skipped when absent or disabled.
- Ebooks is skipped when an enabled rule already covers `epub`. Name clashes get ` (2)`.
- The Inbox entry isn't added twice (different casing, trailing slash).
- Undo deletes empty folders and keeps folders containing a file. It keeps a README whose content changed, and restores Installers only if its destination is unchanged. It removes Ebooks only if unchanged and removes the Inbox entry.
- A failure part-way still records the folders created so far.
- The record round-trips through JSON. An unreadable record is treated as none.

App (view model, no windows):

- The default base folder with and without drive D:.
- The preview updates when the base folder changes.
- `CanCreate` is false for a relative path or a missing drive.
- The previous-setup warning is flagged when a record exists.

## Out of scope

More templates or user template files, a command line (`--init-workspace`), unticking individual folders, moving existing files into the workspace (that is Phase 3), and undoing more than the last setup.

## Delivery

Three commits, each built and tested: (1) Core and tests, (2) window, tray and App wiring, (3) README and CLAUDE.md (replacing the "Starter layouts" candidate). Per CLAUDE.md, nothing is committed until the owner asks.
