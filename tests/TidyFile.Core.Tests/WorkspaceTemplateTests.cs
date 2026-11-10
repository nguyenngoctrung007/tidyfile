using TidyFile.Core.Workspace;

namespace TidyFile.Core.Tests;

public class WorkspaceTemplateTests
{
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

    [Fact]
    public void Readme_only_promises_suggestions_for_files_in_the_inbox()
    {
        // The watcher sees files, not folders, so the README must not suggest dropping whole folders in.
        var readme = WorkspaceTemplate.Developer.ReadmeFor(@"D:\Workspace");

        Assert.DoesNotContain("Put old folders here", readme);
        Assert.Contains("files you put here get folder suggestions", readme);
    }
}
