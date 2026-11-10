# Create Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tray command "Create workspace…" that creates a standard developer folder structure with a README, optionally watches its Inbox and points two rules at it, and can undo everything it created that is still untouched.

**Architecture:** All logic lives in a new `TidyFile.Core/Workspace` namespace: a data-only template, a read-only planner (drives the preview), a setup class that applies and undoes, and a JSON record. Core edits *copies* of `AppSettings` and `RulesDocument`; the App saves them, updates live settings and restarts the watcher, reusing existing patterns. The WPF window is a thin view over a view model.

**Tech Stack:** C# / .NET 10, WPF + WinForms tray (no NuGet packages in the app), xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-11-10-create-workspace-design.md`

## Global Constraints

- Never overwrite, move or delete user files. Folders are removed only with a **non-recursive** `Directory.Delete(path, recursive: false)`.
- Create only what is missing; record only what was actually created or changed.
- Template folders, in order, parents first: `Inbox`, `Projects`, `Projects\Work`, `Projects\Personal`, `Sandbox`, `Resources`, `Resources\Books`, `Resources\Courses`, `Resources\Tools`, `Resources\Assets`, `Archive`, `Archive\{Year}`.
- Default base folder: `D:\Workspace` if `D:\` exists, else `%USERPROFILE%\Workspace`.
- README.md text is copied verbatim from the spec; `<base>` in it is replaced by the base folder with forward slashes.
- Installers rule = the first rule that is enabled, has no name pattern and no sources, and has `exe` among its extensions. Its new destination is `<base>\Resources\Tools`, tokenized with `PathTokens.Tokenize`.
- Ebooks rule = `{ Name: "Ebooks", Extensions: [epub, mobi], Destination: Tokenize(<base>\Resources\Books) }`, added at the **bottom**, unique name; skipped if an enabled rule already has `epub`.
- The record file is `workspace-setup.json` in the data folder (`AppPaths.WorkspaceSetupFile`).
- App project: ImplicitUsings OFF; never import `System.Windows` and `System.Windows.Forms` in the same file.
- Build and test after every task: `dotnet build` then `dotnet test` (if a debug TidyFile is running, add `--artifacts-path <folder>` to both, per CLAUDE.md).
- Commits: only with the owner's go-ahead (CLAUDE.md), and no co-author trailer.

## Review Focus

1. **The base folder already holds the user's files, or is a drive root such as `D:\`:** Create adds only missing items, and Undo never removes the base folder or anything not recorded. Pinned in Task 5.
2. **The base folder is typed relative (`Workspace`), with a trailing slash, with forward slashes, or on a missing drive:** it is normalized, or rejected with a message. Pinned in Task 2.
3. **A file sits where a template folder should go** (e.g. a file named `Inbox`): Create stops with an error and records only the folders made before it. Pinned in Task 4.
4. **The base folder is itself a watched folder** (e.g. Downloads): writing README.md must not raise a suggestion popup, so Apply announces the README path before writing. Pinned in Task 4.
5. **Create is run again on a complete workspace:** nothing new is recorded, so the App must not replace the previous undo record with an empty one. Pinned in Task 4 (`IsEmpty`) and used in Task 7.

---

### Task 1: Workspace template

**Files:**
- Create: `src/TidyFile.Core/Workspace/WorkspaceTemplate.cs`
- Test: `tests/TidyFile.Core.Tests/WorkspaceTemplateTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WorkspaceTemplate
  {
      public static WorkspaceTemplate Developer { get; }
      public string Name { get; }                          // "Developer"
      public IReadOnlyList<string> FolderPatterns { get; } // order from Global Constraints, "{Year}" placeholder
      public string InboxFolder { get; }                   // "Inbox"
      public string ToolsFolder { get; }                   // @"Resources\Tools"
      public string BooksFolder { get; }                   // @"Resources\Books"
      public IReadOnlyList<string> FoldersFor(int year);   // "{Year}" replaced
      public string ReadmeFor(string baseFolder);          // spec README, "<base>" → base with '/' separators
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Developer_template_has_the_twelve_folders_parents_first()
{
    var folders = WorkspaceTemplate.Developer.FoldersFor(2026);
    Assert.Equal(new[]
    {
        "Inbox", "Projects", @"Projects\Work", @"Projects\Personal", "Sandbox",
        "Resources", @"Resources\Books", @"Resources\Courses", @"Resources\Tools", @"Resources\Assets",
        "Archive", @"Archive\2026",
    }, folders);
}

[Fact]
public void Readme_uses_the_base_folder_with_forward_slashes_in_the_git_snippet()
{
    var readme = WorkspaceTemplate.Developer.ReadmeFor(@"D:\Workspace");
    Assert.StartsWith("# Workspace", readme);
    Assert.Contains("[includeIf \"gitdir/i:D:/Workspace/Projects/Work/\"]", readme);
    Assert.DoesNotContain("<base>", readme);
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceTemplateTests`
Expected: build error, because `WorkspaceTemplate` does not exist.

- [ ] **Step 3: Implement `WorkspaceTemplate`**

Store the README as a raw string literal copied from the spec's "README.md content" block. `ReadmeFor` replaces `<base>` with `baseFolder.TrimEnd('\\', '/').Replace('\\', '/')`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceTemplateTests`
Expected: 2 passed.

---

### Task 2: Planner (preview, default and validation of the base folder)

**Files:**
- Create: `src/TidyFile.Core/Workspace/WorkspacePlanner.cs` (contains `PlannedFolder`, `WorkspacePlan`, `WorkspacePlanner`)
- Test: `tests/TidyFile.Core.Tests/WorkspacePlannerTests.cs`

**Interfaces:**
- Consumes: `WorkspaceTemplate` (Task 1); existing `AppSettings`, `RulesDocument`, `Rule`, `PathTokens.Expand/Tokenize`, `RuleEngine.NormalizeExtension` (internal, visible to Core).
- Produces:
  ```csharp
  public sealed record PlannedFolder(string RelativePath, string FullPath, bool Exists);
  public sealed record WorkspacePlan(
      string BaseFolder, bool BaseExists, IReadOnlyList<PlannedFolder> Folders,
      string ReadmePath, bool ReadmeExists, string ReadmeContent,
      string InboxPath, string InboxEntry, bool InboxAlreadyWatched,
      bool RulesReadable, string? InstallersRuleName, string ToolsDestination,
      bool EbooksNeeded, string BooksDestination);
  public static class WorkspacePlanner
  {
      public static string DefaultBaseFolder(Func<string, bool>? directoryExists = null);
      public static string? ValidateBaseFolder(string baseFolder);      // null = OK, else a message
      public static string NormalizeBaseFolder(string baseFolder);      // Path.GetFullPath, no trailing separator except a drive root
      public static WorkspacePlan Plan(WorkspaceTemplate template, string baseFolder,
          AppSettings settings, RulesDocument? rules, int year);        // rules null = rules.json unreadable
      public static Rule? FindInstallersRule(RulesDocument rules);
  }
  ```
  `InboxEntry` = `PathTokens.Tokenize(InboxPath)` (what gets added to `WatchFolders`). `ToolsDestination`/`BooksDestination` are tokenized. When `rules` is null: `RulesReadable=false`, `InstallersRuleName=null`, `EbooksNeeded=false`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Default_base_is_D_Workspace_when_D_exists()
    => Assert.Equal(@"D:\Workspace", WorkspacePlanner.DefaultBaseFolder(p => p == @"D:\"));

[Fact] public void Default_base_falls_back_to_the_user_profile()
    => Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Workspace"),
                    WorkspacePlanner.DefaultBaseFolder(_ => false));

[Theory]
[InlineData("Workspace")]          // relative
[InlineData("")]
[InlineData(@"Q:\Workspace")]      // assumes no Q: drive on the test machine
public void Invalid_base_folders_are_rejected(string path) => Assert.NotNull(WorkspacePlanner.ValidateBaseFolder(path));

[Fact] public void Base_folder_is_normalized()
{
    using var temp = new TempFolder();
    Assert.Equal(temp.Path, WorkspacePlanner.NormalizeBaseFolder(temp.Path + @"\"));
    Assert.Equal(temp.Path, WorkspacePlanner.NormalizeBaseFolder(temp.Path.Replace('\\', '/')));
    Assert.Null(WorkspacePlanner.ValidateBaseFolder(temp.Path + @"\"));
}

[Fact] public void Plan_on_an_empty_base_creates_everything()
{
    using var temp = new TempFolder();
    var plan = Plan(temp.Combine("Ws"), DefaultRules.Create());
    Assert.False(plan.BaseExists);
    Assert.Equal(12, plan.Folders.Count);
    Assert.All(plan.Folders, f => Assert.False(f.Exists));
    Assert.False(plan.ReadmeExists);
    Assert.Equal("Installers", plan.InstallersRuleName);
    Assert.True(plan.EbooksNeeded);
}

[Fact] public void Plan_marks_existing_folders_and_readme()
{
    using var temp = new TempFolder();
    Directory.CreateDirectory(temp.Combine("Ws", "Projects", "Work"));
    temp.CreateFile(Path.Combine("Ws", "README.md"));
    var plan = Plan(temp.Combine("Ws"), DefaultRules.Create());
    Assert.True(plan.Folders.Single(f => f.RelativePath == @"Projects\Work").Exists);
    Assert.True(plan.Folders.Single(f => f.RelativePath == "Projects").Exists);
    Assert.False(plan.Folders.Single(f => f.RelativePath == "Inbox").Exists);
    Assert.True(plan.ReadmeExists);
}

[Fact] public void Installers_rule_is_found_by_content_not_name()
{
    var rules = new RulesDocument { Rules = {
        new Rule { Name = "Exe with pattern", Extensions = { "exe" }, NamePattern = "setup", Destination = @"C:\A" },
        new Rule { Name = "Off", Enabled = false, Extensions = { "exe" }, Destination = @"C:\B" },
        new Rule { Name = "My setups", Extensions = { ".EXE", "msi" }, Destination = @"C:\C" } } };
    Assert.Equal("My setups", WorkspacePlanner.FindInstallersRule(rules)?.Name);
    Assert.Null(WorkspacePlanner.FindInstallersRule(new RulesDocument()));
}

[Fact] public void Ebooks_not_needed_when_an_enabled_rule_covers_epub()
{
    using var temp = new TempFolder();
    var rules = new RulesDocument { Rules = { new Rule { Name = "Books", Extensions = { "epub" }, Destination = @"C:\Books" } } };
    Assert.False(Plan(temp.Combine("Ws"), rules).EbooksNeeded);
}

[Fact] public void Inbox_already_watched_ignores_case_and_trailing_slash()
{
    using var temp = new TempFolder();
    var settings = new AppSettings { WatchFolders = { temp.Combine("WS", "INBOX") + @"\" } };
    var plan = WorkspacePlanner.Plan(WorkspaceTemplate.Developer, temp.Combine("Ws"), settings, new RulesDocument(), 2026);
    Assert.True(plan.InboxAlreadyWatched);
}

[Fact] public void Unreadable_rules_plan_no_rule_changes()
{
    using var temp = new TempFolder();
    var plan = Plan(temp.Combine("Ws"), rules: null);
    Assert.False(plan.RulesReadable);
    Assert.Null(plan.InstallersRuleName);
    Assert.False(plan.EbooksNeeded);
}

private static WorkspacePlan Plan(string baseFolder, RulesDocument? rules) =>
    WorkspacePlanner.Plan(WorkspaceTemplate.Developer, baseFolder, new AppSettings(), rules, 2026);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspacePlannerTests`
Expected: build error, because `WorkspacePlanner` does not exist.

- [ ] **Step 3: Implement `WorkspacePlanner`**

`ValidateBaseFolder` returns messages for an empty path, a path that is not fully qualified (`Path.IsPathFullyQualified`), and a missing drive root (`Directory.Exists(Path.GetPathRoot(full))`). "Already watched" compares `NormalizeBaseFolder(PathTokens.Expand(entry))` with `InboxPath`, using `OrdinalIgnoreCase`. `Plan` reads the file system only.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspacePlannerTests`
Expected: all passed.

---

### Task 3: Setup record

**Files:**
- Create: `src/TidyFile.Core/Workspace/WorkspaceRecord.cs`
- Modify: `src/TidyFile.Core/Storage/AppPaths.cs` (add `WorkspaceSetupFile`)
- Test: `tests/TidyFile.Core.Tests/WorkspaceRecordTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WorkspaceRecord
  {
      public string BaseFolder { get; set; } = "";
      public DateTimeOffset CreatedAt { get; set; }
      public List<string> CreatedFolders { get; set; } = new();   // full paths, creation order (parents first; base first if created)
      public string? ReadmePath { get; set; }
      public string? ReadmeSha256 { get; set; }                  // hex of the file's bytes as written
      public string? AddedWatchFolder { get; set; }              // exact entry added to WatchFolders
      public string? RedirectedRuleName { get; set; }
      public string? RedirectedFrom { get; set; }
      public string? RedirectedTo { get; set; }
      public string? AddedRuleName { get; set; }
      public string? AddedRuleDestination { get; set; }
      [JsonIgnore] public bool IsEmpty { get; }                  // nothing created or changed
      public static WorkspaceRecord? Load(string path);          // null if missing; null + Log.Warn if unreadable
      public void Save(string path);                             // JsonFile.Save
      public static void Delete(string path);                    // no error if missing
  }
  // AppPaths: public string WorkspaceSetupFile => Path.Combine(Root, "workspace-setup.json");
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Round_trips_through_json()
{
    using var temp = new TempFolder();
    var path = temp.Combine("workspace-setup.json");
    var record = new WorkspaceRecord { BaseFolder = @"D:\Workspace", CreatedAt = DateTimeOffset.Now,
        CreatedFolders = { @"D:\Workspace", @"D:\Workspace\Inbox" }, ReadmeSha256 = "ab12",
        RedirectedRuleName = "Installers", RedirectedFrom = @"{Downloads}\Installers", RedirectedTo = @"D:\Workspace\Resources\Tools" };
    record.Save(path);
    var back = WorkspaceRecord.Load(path)!;
    Assert.Equal(record.CreatedFolders, back.CreatedFolders);
    Assert.Equal(record.RedirectedFrom, back.RedirectedFrom);
    Assert.False(back.IsEmpty);
}

[Fact] public void Missing_or_unreadable_record_is_none()
{
    using var temp = new TempFolder();
    Assert.Null(WorkspaceRecord.Load(temp.Combine("nope.json")));
    Assert.Null(WorkspaceRecord.Load(temp.CreateFile("bad.json", "{ broken")));
}

[Fact] public void New_record_is_empty()
    => Assert.True(new WorkspaceRecord { BaseFolder = @"D:\Workspace" }.IsEmpty);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceRecordTests`
Expected: build error.

- [ ] **Step 3: Implement `WorkspaceRecord` and `AppPaths.WorkspaceSetupFile`**

`Load` uses `JsonSerializer.Deserialize<WorkspaceRecord>(File.ReadAllText(path), JsonFile.Options)` and catches `JsonException` and `IOException`. Don't use `JsonFile.LoadOrCreate`: it would create the file and keep a `.bad` copy.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceRecordTests`
Expected: 3 passed.

---

### Task 4: Apply

**Files:**
- Create: `src/TidyFile.Core/Workspace/WorkspaceSetup.cs` (with `WorkspaceOptions`, `WorkspaceApplyResult`)
- Modify: `src/TidyFile.Core/Rules/RuleFactory.cs`: extract the renaming loop of `AddToTop` into `public static string UniqueName(RulesDocument document, string name)`, and use it from `AddToTop` (existing `RuleFactoryTests` must still pass).
- Test: `tests/TidyFile.Core.Tests/WorkspaceSetupApplyTests.cs`

**Interfaces:**
- Consumes: `WorkspacePlan` (Task 2), `WorkspaceRecord` (Task 3), `RuleFactory.UniqueName`.
- Produces:
  ```csharp
  public sealed record WorkspaceOptions(bool WatchInbox, bool SuggestIntoWorkspace);
  public sealed record WorkspaceApplyResult(WorkspaceRecord Record, bool SettingsChanged, bool RulesChanged,
      IReadOnlyList<string> Notes, string? Error);                 // Error = message when folder/README creation stopped
  public static class WorkspaceSetup
  {
      public const string EbooksRuleName = "Ebooks";
      public static readonly IReadOnlyList<string> EbookExtensions; // { "epub", "mobi" }
      public static WorkspaceApplyResult Apply(WorkspacePlan plan, WorkspaceOptions options,
          AppSettings settings, RulesDocument? rules, DateTimeOffset now,
          Action<string>? beforeWriteFile = null);                  // called with the README path before writing it
  }
  ```
  Order: base folder (if missing), folders (parents first), README (`FileMode.CreateNew`, then hash the bytes read back), the `WatchFolders` entry (if `WatchInbox` and not already watched), then the rules (if `SuggestIntoWorkspace` and `rules` is not null). On an exception creating a folder or the README: stop, set `Error`, and return the record so far (skip settings and rules). `settings` and `rules` are edited in place. The caller passes copies.
  Notes (exact text): `"Installers rule not found, skipped."` · `"rules.json couldn't be read, rules left unchanged."`

- [ ] **Step 1: Write the failing tests**

```csharp
// helper: Apply(base, rules, options) => WorkspaceSetup.Apply(WorkspacePlanner.Plan(WorkspaceTemplate.Developer, base, settings, rules, 2026), options, settings, rules, DateTimeOffset.Now, announced.Add)

[Fact] public void Creates_all_folders_and_readme_and_records_them()           // 13 CreatedFolders incl. base; README exists; ReadmeSha256 set
[Fact] public void Creates_only_missing_folders_and_keeps_an_existing_readme()  // pre-create Projects + README "mine"; CreatedFolders lacks Projects; README text still "mine"; ReadmePath null
[Fact] public void Announces_the_readme_before_writing_it()                     // announced == [<base>\README.md]   (Review Focus 4)
[Fact] public void Adds_the_inbox_once_and_records_the_entry()                  // WatchFolders gains plan.InboxEntry; record.AddedWatchFolder == plan.InboxEntry; SettingsChanged
[Fact] public void Watch_inbox_unticked_leaves_settings_alone()                 // SettingsChanged false, AddedWatchFolder null
[Fact] public void Redirects_installers_and_records_old_and_new()               // default rules: Installers.Destination == Tokenize(<base>\Resources\Tools); RedirectedFrom == @"{Downloads}\Installers"
[Fact] public void Adds_ebooks_at_the_bottom_with_a_unique_name()               // rules already contain a rule named "Ebooks" with ext "azw": last rule is "Ebooks (2)" with epub, mobi; AddedRuleName == "Ebooks (2)"
[Fact] public void Unreadable_rules_are_skipped_with_a_note()                   // rules null: RulesChanged false; Notes contains "rules.json couldn't be read, rules left unchanged."
[Fact] public void A_file_in_the_way_stops_and_records_what_was_made()          // file named "Sandbox" in base: Error not null; CreatedFolders contains Inbox and Projects\Personal but not Resources; settings untouched  (Review Focus 3)
[Fact] public void Second_run_on_a_complete_workspace_records_nothing()         // apply twice (inbox already added, rules already changed): second Record.IsEmpty  (Review Focus 5)
```

Write each test with the assertions in its comment, using `TempFolder` and `DefaultRules.Create()` unless stated.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceSetupApplyTests`
Expected: build error.

- [ ] **Step 3: Implement `RuleFactory.UniqueName` and `WorkspaceSetup.Apply`**

Hash the README with `Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))`.

- [ ] **Step 4: Run the whole Core suite**

Run: `dotnet test tests/TidyFile.Core.Tests`
Expected: all passed, including the existing `RuleFactoryTests`.

---

### Task 5: Undo

**Files:**
- Modify: `src/TidyFile.Core/Workspace/WorkspaceSetup.cs` (add `Undo`, `WorkspaceUndoResult`)
- Test: `tests/TidyFile.Core.Tests/WorkspaceSetupUndoTests.cs`

**Interfaces:**
- Consumes: `WorkspaceRecord` (Task 3), `Apply` (Task 4, used by the tests to set up).
- Produces:
  ```csharp
  public sealed record WorkspaceUndoResult(bool SettingsChanged, bool RulesChanged, IReadOnlyList<string> Kept);
  public static WorkspaceUndoResult Undo(WorkspaceRecord record, AppSettings settings, RulesDocument? rules);
  ```
  `Kept` texts (exact): a kept folder → its path relative to the base (the base itself → its full path); `"README.md (edited)"`; `"Installers rule (destination changed)"`; `"Ebooks rule (edited)"`; `"rules (rules.json couldn't be read)"`.
  Steps follow the spec's Undo table. Folders are processed in reverse `CreatedFolders` order. A missing folder is skipped silently. A non-empty folder (the `IOException` from the non-recursive delete) is added to `Kept`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Undo_after_apply_restores_everything()            // base gone; Installers back to "{Downloads}\Installers"; no "Ebooks" rule; WatchFolders back to default; Kept empty
[Fact] public void Folders_with_files_are_kept()                     // file in Projects\Work: Projects\Work, Projects and base remain; Kept contains @"Projects\Work", "Projects" and the base path; other folders gone
[Fact] public void Existing_base_and_unrecorded_content_survive()    // base pre-exists with "notes.txt": base and notes.txt remain; base not in Kept   (Review Focus 1)
[Fact] public void Edited_readme_is_kept()                           // README text changed: file remains; Kept contains "README.md (edited)"
[Fact] public void Installers_changed_by_user_is_not_restored()      // user sets Installers.Destination to C:\Mine: stays C:\Mine; Kept contains "Installers rule (destination changed)"
[Fact] public void Edited_ebooks_rule_is_kept()                      // user adds "azw" to Ebooks: rule remains; Kept contains "Ebooks rule (edited)"
[Fact] public void Renamed_created_folder_is_skipped_silently()      // rename Sandbox to Play: no exception; Play remains; "Sandbox" not in Kept
[Fact] public void Unreadable_rules_skip_the_rule_steps()            // rules null: RulesChanged false; Kept contains "rules (rules.json couldn't be read)"; folders still removed
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.Core.Tests --filter FullyQualifiedName~WorkspaceSetupUndoTests`
Expected: build error, because `Undo` does not exist.

- [ ] **Step 3: Implement `WorkspaceSetup.Undo`**

The Ebooks rule is "unchanged" when it has no pattern and no sources, its extensions set equals `EbookExtensions`, and its destination equals `AddedRuleDestination` (`OrdinalIgnoreCase`).

- [ ] **Step 4: Run the whole solution's tests**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings, all passed.

- [ ] **Step 5: Checkpoint commit 1 (only with the owner's go-ahead)**

```bash
git add src/TidyFile.Core tests/TidyFile.Core.Tests
git commit -m "Create workspace: Core template, planner, setup and undo"
```

---

### Task 6: Window view model

**Files:**
- Create: `src/TidyFile.App/ViewModels/WorkspaceViewModel.cs`
- Test: `tests/TidyFile.App.Tests/WorkspaceViewModelTests.cs`

**Interfaces:**
- Consumes: `WorkspacePlanner`, `WorkspaceTemplate`, `WorkspaceRecord`, `WorkspaceOptions`; existing `ObservableObject`.
- Produces:
  ```csharp
  public sealed class WorkspaceViewModel : ObservableObject
  {
      public WorkspaceViewModel(AppSettings settings, RulesDocument? rules, WorkspaceRecord? lastSetup, int year, string? baseFolder = null);
      public string BaseFolder { get; set; }              // default WorkspacePlanner.DefaultBaseFolder(); setting it re-plans
      public string? BaseFolderError { get; }
      public WorkspacePlan? Plan { get; }                  // null while BaseFolderError is set
      public IReadOnlyList<string> PreviewLines { get; }   // "<relative>\" or "<relative>\  (already exists, kept)", then "README.md" likewise
      public bool WatchInbox { get; set; }                 // default true
      public bool SuggestIntoWorkspace { get; set; }       // default true
      public string SuggestDescription { get; }            // see below
      public bool CanCreate { get; }                       // BaseFolderError == null
      public bool HasUndo { get; }                         // lastSetup != null
      public string? UndoDescription { get; }              // $"Undo setup of {BaseFolder} ({CreatedAt.LocalDateTime:d MMM yyyy})"
      public WorkspaceOptions Options { get; }
  }
  ```
  `SuggestDescription`: when `RulesReadable` is false, `"rules.json couldn't be read; rules will be left unchanged."` Otherwise these parts joined with `", "`: `@"Installers → Resources\Tools"` or `"Installers rule not found, skipped"`, then `@"add rule Ebooks → Resources\Books"` or `"Ebooks already covered"`. Raise `PropertyChanged` for every derived property when `BaseFolder` changes.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Preview_lists_the_template_and_marks_existing_items()   // base with existing Inbox: PreviewLines has 13 lines; "Inbox\  (already exists, kept)"; "Sandbox\"; last line "README.md"
[Fact] public void Changing_the_base_folder_replans_and_notifies()          // set BaseFolder to another temp dir: PreviewLines changes; PropertyChanged raised for nameof(PreviewLines) and nameof(CanCreate)
[Fact] public void Relative_or_missing_drive_base_cannot_be_created()       // "Workspace" and @"Q:\Ws": CanCreate false; BaseFolderError not null; Plan null
[Fact] public void Both_options_are_ticked_by_default()                     // Options == new WorkspaceOptions(true, true)
[Fact] public void Suggest_description_reflects_the_rules()                 // default rules → contains @"Installers → Resources\Tools" and "add rule Ebooks"; rules null → "rules.json couldn't be read; rules will be left unchanged."
[Fact] public void Undo_is_offered_only_with_a_previous_setup()             // lastSetup null → HasUndo false; with record → HasUndo true and UndoDescription contains its BaseFolder
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/TidyFile.App.Tests --filter FullyQualifiedName~WorkspaceViewModelTests`
Expected: build error.

- [ ] **Step 3: Implement `WorkspaceViewModel`**

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TidyFile.App.Tests`
Expected: all passed.

---

### Task 7: Window, tray item and App wiring

**Files:**
- Create: `src/TidyFile.App/Views/WorkspaceWindow.xaml`, `src/TidyFile.App/Views/WorkspaceWindow.xaml.cs`
- Modify: `src/TidyFile.App/Services/TrayIcon.cs` (menu item + event), `src/TidyFile.App/App.xaml.cs` (open / create / undo)

**Interfaces:**
- Consumes: `WorkspaceViewModel` (Task 6), `WorkspaceSetup.Apply/Undo` (Tasks 4–5), `WorkspaceRecord` (Task 3), `AppPaths.WorkspaceSetupFile`.
- Produces:
  ```csharp
  // TrayIcon
  public event Action? WorkspaceRequested;   // menu item "Create workspace…", inserted right after "Settings…"
  // WorkspaceWindow
  public WorkspaceWindow(WorkspaceViewModel viewModel, Func<WorkspaceViewModel, string?> create, Func<string?> undo);
  //   create/undo return an error message or null; the window closes on null
  // App (private)
  void OpenWorkspace();
  string? CreateWorkspace(WorkspaceViewModel viewModel);
  string? UndoWorkspace();
  ```

- [ ] **Step 1: Build the window**

Layout per the spec's "User experience": base folder `TextBox` (`UpdateSourceTrigger=PropertyChanged`) + Browse… (`Microsoft.Win32.OpenFolderDialog`), red `BaseFolderError` text, a read-only `ListBox` of `PreviewLines`, the two `CheckBox`es (the second with `SuggestDescription` as a hint below it), and a footer with **Undo last setup** (visible when `HasUndo`, tooltip `UndoDescription`) on the left and **Create** (`IsEnabled=CanCreate`) / **Cancel** on the right. Reuse `SettingsWindow`'s style resources and icon code. On Create when `HasUndo`: ask "The previous setup can no longer be undone. Continue?" (Yes/No) first.

- [ ] **Step 2: Add the tray item and wire the App**

- `OpenWorkspace`: if `_settingsWindow` is open, show "Close the Settings window first, so its unsaved copy doesn't overwrite the workspace changes.", activate Settings and return. If a workspace window is open, activate it. Otherwise build the view model from `_settings`, `_rules.LoadDocument(out var w)` (pass `null` when `w` is not null), `WorkspaceRecord.Load(_paths.WorkspaceSetupFile)` and `DateTime.Now.Year`.
- `CreateWorkspace`:
  1. Re-plan from the view model's `BaseFolder`. Work on a settings copy (`new AppSettings { WatchFolders = new(_settings.WatchFolders), PopupSeconds = …, MaxSuggestions = … }`) and a freshly loaded rules document.
  2. Call `Apply` with `beforeWriteFile: p => _watcher?.IgnoreNext(p)`.
  3. If `SettingsChanged`: `JsonFile.Save(_paths.SettingsFile, copy)`, then `_settings.WatchFolders = copy.WatchFolders` and `StartWatcher()`. On failure, clear `record.AddedWatchFolder` and add a note.
  4. If `RulesChanged`: `_rules.Save(doc)`. On failure, clear the rule fields of the record and add a note.
  5. Save the record **only if** `!record.IsEmpty` (Review Focus 5).
  6. Notification "Workspace created in {base}. Click to open." plus the notes, with the click opening the base folder (`Process.Start(new ProcessStartInfo(base) { UseShellExecute = true })`).
  7. Return `result.Error`.
- `UndoWorkspace`: load the record (missing → "Nothing to undo."). Call `Undo` on copies, save what changed (as above), restart the watcher, delete the record, and show "Undone." plus `"Kept: " + string.Join(", ", Kept)` when `Kept` isn't empty.

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings, all passed.

- [ ] **Step 4: Manual check in the running app (owner)**

Exit the release TidyFile, run `dotnet run --project src/TidyFile.App`, then:
1. Tray → Create workspace…: the preview shows 12 folders + README.md, and the base is `D:\Workspace`.
2. Point the base folder at a temp folder → Create: the folders exist, README.md is readable, and the notification opens the folder.
3. Drop a file into `Inbox`: a suggestion popup appears. Settings shows Installers → `Resources\Tools` and an Ebooks rule at the bottom.
4. Put a file in `Projects\Work`, reopen the window → Undo last setup: only `Projects\Work`, `Projects` and the base remain, and the notification lists them. The rules and watched folders are back.
5. With Settings open, Create workspace… asks you to close Settings first.

- [ ] **Step 5: Checkpoint commit 2 (only with the owner's go-ahead)**

```bash
git add src/TidyFile.App tests/TidyFile.App.Tests
git commit -m "Create workspace: window, tray item and App wiring"
```

---

### Task 8: Documentation

**Files:**
- Modify: `README.md` (feature list + a short "Create workspace" section), `CLAUDE.md` (Layout: `Workspace/`; Status; replace the "Candidate — Starter layouts" roadmap entry with "✅ Create workspace" and a pointer to the spec)

- [ ] **Step 1: Update both files**

The README section has 3–4 lines: what it creates, the two options, and that Undo removes only what is still untouched.

- [ ] **Step 2: Final verification**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings, all passed.

- [ ] **Step 3: Checkpoint commit 3 (only with the owner's go-ahead)**

```bash
git add README.md CLAUDE.md docs/superpowers
git commit -m "Docs: Create workspace"
```

- [ ] **Step 4: Update the release copy (owner's choice)**

Use the publish-and-copy steps in CLAUDE.md "Build, test, run".
