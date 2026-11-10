using System.Security.Cryptography;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.Core.Workspace;

/// <summary>The two optional parts of "Create workspace".</summary>
public sealed record WorkspaceOptions(bool WatchInbox, bool SuggestIntoWorkspace);

/// <param name="Record">Everything that was actually created or changed (saved for Undo).</param>
/// <param name="SettingsChanged">The settings passed in were edited and need saving.</param>
/// <param name="RulesChanged">The rules passed in were edited and need saving.</param>
/// <param name="Notes">Things skipped on purpose, for the notification.</param>
/// <param name="Error">Why creating folders or the README stopped, or null.</param>
public sealed record WorkspaceApplyResult(
    WorkspaceRecord Record,
    bool SettingsChanged,
    bool RulesChanged,
    IReadOnlyList<string> Notes,
    string? Error)
{
    /// <summary>Notification title: "not finished" on an error, "already set up" when nothing was missing.</summary>
    public string Title =>
        Error is not null ? "Workspace not finished"
        : Record.IsEmpty ? "Workspace already set up"
        : "Workspace created";
}

/// <param name="SettingsChanged">The settings passed in were edited and need saving.</param>
/// <param name="RulesChanged">The rules passed in were edited and need saving.</param>
/// <param name="Kept">What was left in place because the user changed it, for the notification.</param>
public sealed record WorkspaceUndoResult(bool SettingsChanged, bool RulesChanged, IReadOnlyList<string> Kept);

/// <summary>
/// Creates a workspace from a <see cref="WorkspacePlan"/>, and undoes it.
/// Folders and the README are written to disk here; settings and rules are edited in the objects passed in
/// (copies, which the caller saves), so this class never touches settings.json or rules.json itself.
/// </summary>
public static class WorkspaceSetup
{
    public const string EbooksRuleName = "Ebooks";

    public const string NoteInstallersMissing = "Installers rule not found, skipped.";

    public const string NoteRulesUnreadable = "rules.json couldn't be read, rules left unchanged.";

    public const string KeptReadmeEdited = "README.md (edited)";

    public const string KeptInstallersChanged = "Installers rule (destination changed)";

    public const string KeptEbooksEdited = "Ebooks rule (edited)";

    public const string KeptRulesUnreadable = "rules (rules.json couldn't be read)";

    public static readonly IReadOnlyList<string> EbookExtensions = new[] { "epub", "mobi" };

    /// <param name="rules">The rules to edit, or null when rules.json couldn't be read.</param>
    /// <param name="beforeWriteFile">
    /// Called with the README path just before it is written, so a watcher on the base folder can ignore it.
    /// </param>
    public static WorkspaceApplyResult Apply(
        WorkspacePlan plan,
        WorkspaceOptions options,
        AppSettings settings,
        RulesDocument? rules,
        DateTimeOffset now,
        Action<string>? beforeWriteFile = null)
    {
        var record = new WorkspaceRecord { BaseFolder = plan.BaseFolder, CreatedAt = now };
        var notes = new List<string>();

        // 1–2. Folders and README. Existence is checked again here, not taken from the plan, which may be stale.
        try
        {
            CreateFolder(plan.BaseFolder, record);
            foreach (var folder in plan.Folders)
            {
                CreateFolder(folder.FullPath, record);
            }

            if (!File.Exists(plan.ReadmePath) && !Directory.Exists(plan.ReadmePath))
            {
                beforeWriteFile?.Invoke(plan.ReadmePath);

                // CreateNew: never overwrite, even if a README appeared since the check above.
                using (var stream = new FileStream(plan.ReadmePath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(plan.ReadmeContent);
                }

                record.ReadmePath = plan.ReadmePath;
                record.ReadmeSha256 = Sha256(plan.ReadmePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stop here; the record lists what was made so far, so Undo can still clean it up.
            return new WorkspaceApplyResult(record, SettingsChanged: false, RulesChanged: false, notes, ex.Message);
        }

        // 3. Watch the Inbox.
        var settingsChanged = false;
        if (options.WatchInbox && !settings.WatchFolders.Any(entry => WorkspacePlanner.SameFolder(entry, plan.InboxPath)))
        {
            settings.WatchFolders.Add(plan.InboxEntry);
            record.AddedWatchFolder = plan.InboxEntry;
            settingsChanged = true;
        }

        // 4. Point rules at the workspace.
        var rulesChanged = false;
        if (options.SuggestIntoWorkspace)
        {
            if (rules is null)
            {
                notes.Add(NoteRulesUnreadable);
            }
            else
            {
                rulesChanged |= RedirectInstallers(plan, rules, record, notes);
                rulesChanged |= AddEbooks(plan, rules, record);
            }
        }

        return new WorkspaceApplyResult(record, settingsChanged, rulesChanged, notes, Error: null);
    }

    /// <summary>
    /// Reverts what <paramref name="record"/> lists, but only what is still exactly as TidyFile left it.
    /// Folders are removed with a non-recursive delete, which Windows refuses for a non-empty folder,
    /// so the user's files can never be deleted.
    /// </summary>
    /// <param name="rules">The rules to edit, or null when rules.json couldn't be read (rule steps are skipped).</param>
    public static WorkspaceUndoResult Undo(WorkspaceRecord record, AppSettings settings, RulesDocument? rules)
    {
        var kept = new List<string>();

        var rulesChanged = false;
        if (record.AddedRuleName is not null || record.RedirectedRuleName is not null)
        {
            if (rules is null)
            {
                kept.Add(KeptRulesUnreadable);
            }
            else
            {
                rulesChanged |= RemoveEbooks(record, rules, kept);
                rulesChanged |= RestoreInstallers(record, rules, kept);
            }
        }

        var settingsChanged = false;
        if (record.AddedWatchFolder is not null)
        {
            var index = settings.WatchFolders.FindIndex(e => e.Equals(record.AddedWatchFolder, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                settings.WatchFolders.RemoveAt(index);
                settingsChanged = true;
            }
        }

        if (record.ReadmePath is not null && File.Exists(record.ReadmePath))
        {
            if (Sha256(record.ReadmePath).Equals(record.ReadmeSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(record.ReadmePath);
            }
            else
            {
                kept.Add(KeptReadmeEdited);
            }
        }

        // Deepest first: CreatedFolders is in creation order, parents before children.
        foreach (var folder in Enumerable.Reverse(record.CreatedFolders))
        {
            if (!Directory.Exists(folder))
            {
                continue; // Renamed or removed by the user.
            }

            try
            {
                Directory.Delete(folder, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept.Add(DisplayName(record.BaseFolder, folder));
            }
        }

        return new WorkspaceUndoResult(settingsChanged, rulesChanged, kept);
    }

    private static bool RemoveEbooks(WorkspaceRecord record, RulesDocument rules, List<string> kept)
    {
        var added = record.AddedRuleName is null ? null : FindByName(rules, record.AddedRuleName);
        if (added is null)
        {
            return false; // Already deleted by the user.
        }

        var unchanged =
            string.IsNullOrWhiteSpace(added.NamePattern) &&
            added.Sources.All(string.IsNullOrWhiteSpace) &&
            new HashSet<string>(added.Extensions.Select(e => e.Trim().TrimStart('.')), StringComparer.OrdinalIgnoreCase).SetEquals(EbookExtensions) &&
            record.AddedRuleDestination is not null && SameDestination(added.Destination, record.AddedRuleDestination);

        if (!unchanged)
        {
            kept.Add(KeptEbooksEdited);
            return false;
        }

        rules.Rules.Remove(added);
        return true;
    }

    private static bool RestoreInstallers(WorkspaceRecord record, RulesDocument rules, List<string> kept)
    {
        if (record.RedirectedRuleName is null || record.RedirectedFrom is null || record.RedirectedTo is null)
        {
            return false;
        }

        var rule = FindByName(rules, record.RedirectedRuleName);
        if (rule is null || !SameDestination(rule.Destination, record.RedirectedTo))
        {
            kept.Add(KeptInstallersChanged);
            return false;
        }

        rule.Destination = record.RedirectedFrom;
        return true;
    }

    private static Rule? FindByName(RulesDocument rules, string name) =>
        rules.Rules.FirstOrDefault(r => r.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A kept folder as the user would name it: relative to the base, or the base's full path.</summary>
    private static string DisplayName(string baseFolder, string folder) =>
        folder.Equals(baseFolder, StringComparison.OrdinalIgnoreCase) ? folder : Path.GetRelativePath(baseFolder, folder);

    private static void CreateFolder(string path, WorkspaceRecord record)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(path); // Throws IOException if a file has that name.
        record.CreatedFolders.Add(path);
    }

    private static bool RedirectInstallers(WorkspacePlan plan, RulesDocument rules, WorkspaceRecord record, List<string> notes)
    {
        var installers = WorkspacePlanner.FindInstallersRule(rules);
        if (installers is null)
        {
            notes.Add(NoteInstallersMissing);
            return false;
        }

        if (SameDestination(installers.Destination, plan.ToolsDestination))
        {
            return false; // Already points there (e.g. a second run); nothing to record.
        }

        record.RedirectedRuleName = installers.Name;
        record.RedirectedFrom = installers.Destination;
        record.RedirectedTo = plan.ToolsDestination;
        installers.Destination = plan.ToolsDestination;
        return true;
    }

    private static bool AddEbooks(WorkspacePlan plan, RulesDocument rules, WorkspaceRecord record)
    {
        if (rules.Rules.Any(r => r.Enabled && r.Extensions.Any(e => WorkspacePlanner.IsExtension(e, ".epub"))))
        {
            return false;
        }

        // At the bottom, so the user's own rules keep winning ties.
        var name = RuleFactory.UniqueName(rules, EbooksRuleName);
        rules.Rules.Add(new Rule { Name = name, Extensions = EbookExtensions.ToList(), Destination = plan.BooksDestination });
        record.AddedRuleName = name;
        record.AddedRuleDestination = plan.BooksDestination;
        return true;
    }

    /// <summary>True if two rule destinations (which may contain tokens) are the same folder.</summary>
    private static bool SameDestination(string a, string b) =>
        PathTokens.Expand(a.Trim()).TrimEnd('\\', '/')
            .Equals(PathTokens.Expand(b.Trim()).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
