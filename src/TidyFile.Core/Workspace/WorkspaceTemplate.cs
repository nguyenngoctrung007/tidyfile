namespace TidyFile.Core.Workspace;

/// <summary>
/// A folder structure that "Create workspace" builds, plus the README it writes. Data only.
/// See docs/superpowers/specs/2026-11-10-create-workspace-design.md.
/// </summary>
public sealed class WorkspaceTemplate
{
    private const string YearPlaceholder = "{Year}";
    private const string BasePlaceholder = "<base>";

    private WorkspaceTemplate(string name, IReadOnlyList<string> folderPatterns, string readme)
    {
        Name = name;
        FolderPatterns = folderPatterns;
        _readme = readme;
    }

    private readonly string _readme;

    /// <summary>The standard developer workspace.</summary>
    public static WorkspaceTemplate Developer { get; } = new(
        "Developer",
        new[]
        {
            "Inbox", "Projects", @"Projects\Work", @"Projects\Personal", "Sandbox",
            "Resources", @"Resources\Books", @"Resources\Courses", @"Resources\Tools", @"Resources\Assets",
            "Archive", @"Archive\" + YearPlaceholder,
        },
        DeveloperReadme);

    public string Name { get; }

    /// <summary>Folders relative to the base folder, parents before children. May contain "{Year}".</summary>
    public IReadOnlyList<string> FolderPatterns { get; }

    public string InboxFolder => "Inbox";

    public string ToolsFolder => @"Resources\Tools";

    public string BooksFolder => @"Resources\Books";

    /// <summary><see cref="FolderPatterns"/> with "{Year}" replaced by <paramref name="year"/>.</summary>
    public IReadOnlyList<string> FoldersFor(int year) =>
        FolderPatterns.Select(f => f.Replace(YearPlaceholder, year.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();

    /// <summary>The README text for a workspace in <paramref name="baseFolder"/> (git paths use forward slashes).</summary>
    public string ReadmeFor(string baseFolder) =>
        _readme.Replace(BasePlaceholder, baseFolder.TrimEnd('\\', '/').Replace('\\', '/'));

    private const string DeveloperReadme = """
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

        """;
}
