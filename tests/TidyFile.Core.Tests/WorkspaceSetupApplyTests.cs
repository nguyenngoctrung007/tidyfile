using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.Core.Tests;

public class WorkspaceSetupApplyTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly AppSettings _settings = new();
    private readonly List<(string Path, bool ExistedAlready)> _announced = new();
    private readonly string _base;

    public WorkspaceSetupApplyTests()
    {
        _base = _temp.Combine("Ws");
    }

    public void Dispose() => _temp.Dispose();

    private WorkspacePlan _plan = null!;

    private WorkspaceApplyResult Apply(RulesDocument? rules, WorkspaceOptions? options = null)
    {
        _plan = WorkspacePlanner.Plan(WorkspaceTemplate.Developer, _base, _settings, rules, 2026);
        return WorkspaceSetup.Apply(_plan, options ?? new WorkspaceOptions(true, true), _settings, rules,
            DateTimeOffset.Now, p => _announced.Add((p, File.Exists(p))));
    }

    [Fact]
    public void Creates_all_folders_and_readme_and_records_them()
    {
        var result = Apply(DefaultRules.Create());

        Assert.Null(result.Error);
        Assert.Equal(_base, result.Record.BaseFolder);
        Assert.Equal(13, result.Record.CreatedFolders.Count);
        Assert.Equal(_base, result.Record.CreatedFolders[0]);
        Assert.All(result.Record.CreatedFolders, f => Assert.True(Directory.Exists(f)));
        Assert.True(File.Exists(Path.Combine(_base, "README.md")));
        Assert.Equal(Path.Combine(_base, "README.md"), result.Record.ReadmePath);
        Assert.NotNull(result.Record.ReadmeSha256);
    }

    [Fact]
    public void Creates_only_missing_folders_and_keeps_an_existing_readme()
    {
        Directory.CreateDirectory(Path.Combine(_base, "Projects"));
        File.WriteAllText(Path.Combine(_base, "README.md"), "mine");

        var result = Apply(DefaultRules.Create());

        Assert.DoesNotContain(_base, result.Record.CreatedFolders);
        Assert.DoesNotContain(Path.Combine(_base, "Projects"), result.Record.CreatedFolders);
        Assert.Contains(Path.Combine(_base, "Projects", "Work"), result.Record.CreatedFolders);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_base, "README.md")));
        Assert.Null(result.Record.ReadmePath);
    }

    [Fact]
    public void Announces_the_readme_before_writing_it()
    {
        Apply(DefaultRules.Create());

        Assert.Equal(new[] { (Path.Combine(_base, "README.md"), false) }, _announced);
    }

    [Fact]
    public void Adds_the_inbox_once_and_records_the_entry()
    {
        var before = _settings.WatchFolders.Count;

        var result = Apply(DefaultRules.Create());

        Assert.True(result.SettingsChanged);
        Assert.Equal(before + 1, _settings.WatchFolders.Count);
        Assert.Contains(_plan.InboxEntry, _settings.WatchFolders);
        Assert.Equal(_plan.InboxEntry, result.Record.AddedWatchFolder);
    }

    [Fact]
    public void Watch_inbox_unticked_leaves_settings_alone()
    {
        var result = Apply(DefaultRules.Create(), new WorkspaceOptions(WatchInbox: false, SuggestIntoWorkspace: true));

        Assert.False(result.SettingsChanged);
        Assert.Null(result.Record.AddedWatchFolder);
        Assert.Equal(new AppSettings().WatchFolders, _settings.WatchFolders);
    }

    [Fact]
    public void Redirects_installers_and_records_old_and_new()
    {
        var rules = DefaultRules.Create();

        var result = Apply(rules);

        var installers = rules.Rules.Single(r => r.Name == "Installers");
        Assert.True(result.RulesChanged);
        Assert.Equal(PathTokens.Tokenize(Path.Combine(_base, "Resources", "Tools")), installers.Destination);
        Assert.Equal("Installers", result.Record.RedirectedRuleName);
        Assert.Equal(@"{Downloads}\Installers", result.Record.RedirectedFrom);
        Assert.Equal(installers.Destination, result.Record.RedirectedTo);
    }

    [Fact]
    public void Adds_ebooks_at_the_bottom_with_a_unique_name()
    {
        var rules = DefaultRules.Create();
        rules.Rules.Add(new Rule { Name = "Ebooks", Extensions = { "azw" }, Destination = @"C:\Kindle" });

        var result = Apply(rules);

        var last = rules.Rules[^1];
        Assert.Equal("Ebooks (2)", last.Name);
        Assert.Equal(new[] { "epub", "mobi" }, last.Extensions);
        Assert.Equal(_plan.BooksDestination, last.Destination);
        Assert.Equal("Ebooks (2)", result.Record.AddedRuleName);
        Assert.Equal(_plan.BooksDestination, result.Record.AddedRuleDestination);
    }

    [Fact]
    public void Missing_installers_rule_is_noted()
    {
        var result = Apply(new RulesDocument());

        Assert.Contains("Installers rule not found, skipped.", result.Notes);
        Assert.Null(result.Record.RedirectedRuleName);
    }

    [Fact]
    public void Unreadable_rules_are_skipped_with_a_note()
    {
        var result = Apply(rules: null);

        Assert.False(result.RulesChanged);
        Assert.Contains("rules.json couldn't be read, rules left unchanged.", result.Notes);
    }

    [Fact]
    public void A_file_in_the_way_stops_and_records_what_was_made()
    {
        Directory.CreateDirectory(_base);
        File.WriteAllText(Path.Combine(_base, "Sandbox"), "not a folder");

        var result = Apply(DefaultRules.Create());

        Assert.NotNull(result.Error);
        Assert.Contains(Path.Combine(_base, "Inbox"), result.Record.CreatedFolders);
        Assert.Contains(Path.Combine(_base, "Projects", "Personal"), result.Record.CreatedFolders);
        Assert.DoesNotContain(Path.Combine(_base, "Resources"), result.Record.CreatedFolders);
        Assert.False(result.SettingsChanged);
        Assert.False(result.RulesChanged);
        Assert.Equal(new AppSettings().WatchFolders, _settings.WatchFolders);
    }

    [Fact]
    public void Title_says_created_already_set_up_or_not_finished()
    {
        var rules = DefaultRules.Create();
        Assert.Equal("Workspace created", Apply(rules).Title);
        Assert.Equal("Workspace already set up", Apply(rules).Title);

        File.WriteAllText(_temp.Combine("Blocked"), "a file, not a folder");
        var blocked = WorkspaceSetup.Apply(
            WorkspacePlanner.Plan(WorkspaceTemplate.Developer, _temp.Combine("Blocked"), _settings, rules, 2026),
            new WorkspaceOptions(true, true), _settings, rules, DateTimeOffset.Now);
        Assert.Equal("Workspace not finished", blocked.Title);
    }

    [Fact]
    public void Second_run_on_a_complete_workspace_records_nothing()
    {
        var rules = DefaultRules.Create();
        Apply(rules);

        var second = Apply(rules);

        Assert.Null(second.Error);
        Assert.True(second.Record.IsEmpty);
        Assert.False(second.SettingsChanged);
        Assert.False(second.RulesChanged);
    }
}
