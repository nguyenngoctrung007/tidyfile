using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.Core.Workspace;

/// <summary>One template folder: where it goes and whether it is already there.</summary>
public sealed record PlannedFolder(string RelativePath, string FullPath, bool Exists);

/// <summary>What "Create workspace" would do in a base folder. Built by <see cref="WorkspacePlanner.Plan"/>.</summary>
/// <param name="InboxEntry">What gets added to the watched folders (tokenized when possible).</param>
/// <param name="RulesReadable">False when rules.json couldn't be read: no rule changes are planned.</param>
/// <param name="InstallersRuleName">The rule to redirect, or null if there is none.</param>
/// <param name="ToolsDestination">New destination for the Installers rule (tokenized when possible).</param>
/// <param name="EbooksNeeded">True when no enabled rule covers epub yet.</param>
/// <param name="BooksDestination">Destination of the Ebooks rule (tokenized when possible).</param>
public sealed record WorkspacePlan(
    string BaseFolder,
    bool BaseExists,
    IReadOnlyList<PlannedFolder> Folders,
    string ReadmePath,
    bool ReadmeExists,
    string ReadmeContent,
    string InboxPath,
    string InboxEntry,
    bool InboxAlreadyWatched,
    bool RulesReadable,
    string? InstallersRuleName,
    string ToolsDestination,
    bool EbooksNeeded,
    string BooksDestination);

/// <summary>Works out what "Create workspace" would do. Only reads the file system; drives the preview.</summary>
public static class WorkspacePlanner
{
    public const string ReadmeFileName = "README.md";

    /// <summary><c>D:\Workspace</c> if drive D: exists, else <c>%USERPROFILE%\Workspace</c>.</summary>
    public static string DefaultBaseFolder(Func<string, bool>? directoryExists = null) =>
        (directoryExists ?? Directory.Exists)(@"D:\")
            ? @"D:\Workspace"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Workspace");

    /// <summary>Why <paramref name="baseFolder"/> can't be used, or null if it can.</summary>
    public static string? ValidateBaseFolder(string baseFolder)
    {
        var text = baseFolder?.Trim() ?? "";
        if (text.Length == 0)
        {
            return "Choose a folder.";
        }

        if (!Path.IsPathFullyQualified(text))
        {
            return @"Use a full path such as D:\Workspace.";
        }

        string full;
        try
        {
            full = NormalizeBaseFolder(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That isn't a valid folder path.";
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return $"Drive {root} doesn't exist.";
        }

        return File.Exists(full) ? "A file with that name already exists." : null;
    }

    /// <summary>Full path with backslashes and no trailing separator (except for a drive root such as <c>D:\</c>).</summary>
    public static string NormalizeBaseFolder(string baseFolder)
    {
        var full = Path.GetFullPath(baseFolder.Trim());
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <param name="rules">The current rules, or null when rules.json couldn't be read.</param>
    public static WorkspacePlan Plan(WorkspaceTemplate template, string baseFolder, AppSettings settings, RulesDocument? rules, int year)
    {
        var root = NormalizeBaseFolder(baseFolder);
        var folders = template.FoldersFor(year)
            .Select(relative =>
            {
                var full = Path.Combine(root, relative);
                return new PlannedFolder(relative, full, Directory.Exists(full));
            })
            .ToList();

        var readmePath = Path.Combine(root, ReadmeFileName);
        var inboxPath = Path.Combine(root, template.InboxFolder);

        return new WorkspacePlan(
            BaseFolder: root,
            BaseExists: Directory.Exists(root),
            Folders: folders,
            ReadmePath: readmePath,
            ReadmeExists: File.Exists(readmePath) || Directory.Exists(readmePath),
            ReadmeContent: template.ReadmeFor(root),
            InboxPath: inboxPath,
            InboxEntry: PathTokens.Tokenize(inboxPath),
            InboxAlreadyWatched: settings.WatchFolders.Any(entry => SameFolder(entry, inboxPath)),
            RulesReadable: rules is not null,
            InstallersRuleName: rules is null ? null : FindInstallersRule(rules)?.Name,
            ToolsDestination: PathTokens.Tokenize(Path.Combine(root, template.ToolsFolder)),
            EbooksNeeded: rules is not null && !rules.Rules.Any(r => r.Enabled && r.Extensions.Any(e => IsExtension(e, ".epub"))),
            BooksDestination: PathTokens.Tokenize(Path.Combine(root, template.BooksFolder)));
    }

    /// <summary>
    /// The rule that sends installers somewhere: enabled, no name pattern, no websites, and "exe" among its extensions.
    /// Found by content, so it still works after the user renames it.
    /// </summary>
    public static Rule? FindInstallersRule(RulesDocument rules) =>
        rules.Rules.FirstOrDefault(r =>
            r.Enabled &&
            string.IsNullOrWhiteSpace(r.NamePattern) &&
            r.Sources.All(string.IsNullOrWhiteSpace) &&
            r.Extensions.Any(e => IsExtension(e, ".exe")));

    /// <summary>True if a watched-folder entry (which may contain tokens) is the same folder as <paramref name="fullPath"/>.</summary>
    internal static bool SameFolder(string entry, string fullPath)
    {
        try
        {
            return NormalizeBaseFolder(PathTokens.Expand(entry)).Equals(NormalizeBaseFolder(fullPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static bool IsExtension(string extension, string normalized) =>
        !string.IsNullOrWhiteSpace(extension) && RuleEngine.NormalizeExtension(extension) == normalized;
}
