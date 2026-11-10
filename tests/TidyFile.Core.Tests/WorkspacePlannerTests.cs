using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.Core.Tests;

public class WorkspacePlannerTests
{
    [Fact]
    public void Default_base_is_D_Workspace_when_D_exists()
    {
        Assert.Equal(@"D:\Workspace", WorkspacePlanner.DefaultBaseFolder(p => p == @"D:\"));
    }

    [Fact]
    public void Default_base_falls_back_to_the_user_profile()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Workspace"),
            WorkspacePlanner.DefaultBaseFolder(_ => false));
    }

    [Theory]
    [InlineData("Workspace")]       // Relative.
    [InlineData("")]
    [InlineData(@"Q:\Workspace")]   // Assumes no Q: drive on the test machine.
    public void Invalid_base_folders_are_rejected(string path)
    {
        Assert.NotNull(WorkspacePlanner.ValidateBaseFolder(path));
    }

    [Fact]
    public void Base_folder_is_normalized()
    {
        using var temp = new TempFolder();

        Assert.Equal(temp.Path, WorkspacePlanner.NormalizeBaseFolder(temp.Path + @"\"));
        Assert.Equal(temp.Path, WorkspacePlanner.NormalizeBaseFolder(temp.Path.Replace('\\', '/')));
        Assert.Null(WorkspacePlanner.ValidateBaseFolder(temp.Path + @"\"));
    }

    [Fact]
    public void Plan_on_an_empty_base_creates_everything()
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

    [Fact]
    public void Plan_marks_existing_folders_and_readme()
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

    [Fact]
    public void Installers_rule_is_found_by_content_not_name()
    {
        var rules = new RulesDocument
        {
            Rules =
            {
                new Rule { Name = "Exe with pattern", Extensions = { "exe" }, NamePattern = "setup", Destination = @"C:\A" },
                new Rule { Name = "Off", Enabled = false, Extensions = { "exe" }, Destination = @"C:\B" },
                new Rule { Name = "My setups", Extensions = { ".EXE", "msi" }, Destination = @"C:\C" },
            },
        };

        Assert.Equal("My setups", WorkspacePlanner.FindInstallersRule(rules)?.Name);
        Assert.Null(WorkspacePlanner.FindInstallersRule(new RulesDocument()));
    }

    [Fact]
    public void Ebooks_not_needed_when_an_enabled_rule_covers_epub()
    {
        using var temp = new TempFolder();
        var rules = new RulesDocument { Rules = { new Rule { Name = "Books", Extensions = { "epub" }, Destination = @"C:\Books" } } };

        Assert.False(Plan(temp.Combine("Ws"), rules).EbooksNeeded);
    }

    [Fact]
    public void Inbox_already_watched_ignores_case_and_trailing_slash()
    {
        using var temp = new TempFolder();
        var settings = new AppSettings { WatchFolders = { temp.Combine("WS", "INBOX") + @"\" } };

        var plan = WorkspacePlanner.Plan(WorkspaceTemplate.Developer, temp.Combine("Ws"), settings, new RulesDocument(), 2026);

        Assert.True(plan.InboxAlreadyWatched);
    }

    [Fact]
    public void Unreadable_rules_plan_no_rule_changes()
    {
        using var temp = new TempFolder();

        var plan = Plan(temp.Combine("Ws"), rules: null);

        Assert.False(plan.RulesReadable);
        Assert.Null(plan.InstallersRuleName);
        Assert.False(plan.EbooksNeeded);
    }

    private static WorkspacePlan Plan(string baseFolder, RulesDocument? rules) =>
        WorkspacePlanner.Plan(WorkspaceTemplate.Developer, baseFolder, new AppSettings(), rules, 2026);
}
