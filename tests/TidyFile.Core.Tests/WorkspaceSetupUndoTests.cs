using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.Core.Tests;

public class WorkspaceSetupUndoTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly AppSettings _settings = new();
    private readonly RulesDocument _rules = DefaultRules.Create();
    private readonly string _base;

    public WorkspaceSetupUndoTests()
    {
        _base = _temp.Combine("Ws");
    }

    public void Dispose() => _temp.Dispose();

    private WorkspaceRecord Apply()
    {
        var plan = WorkspacePlanner.Plan(WorkspaceTemplate.Developer, _base, _settings, _rules, 2026);
        var result = WorkspaceSetup.Apply(plan, new WorkspaceOptions(true, true), _settings, _rules, DateTimeOffset.Now);
        Assert.Null(result.Error);
        return result.Record;
    }

    private string In(params string[] parts) => Path.Combine(new[] { _base }.Concat(parts).ToArray());

    [Fact]
    public void Undo_after_apply_restores_everything()
    {
        var record = Apply();

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.False(Directory.Exists(_base));
        Assert.Equal(@"{Downloads}\Installers", _rules.Rules.Single(r => r.Name == "Installers").Destination);
        Assert.DoesNotContain(_rules.Rules, r => r.Name == WorkspaceSetup.EbooksRuleName);
        Assert.Equal(new AppSettings().WatchFolders, _settings.WatchFolders);
        Assert.True(result.SettingsChanged);
        Assert.True(result.RulesChanged);
        Assert.Empty(result.Kept);
    }

    [Fact]
    public void Folders_with_files_are_kept()
    {
        var record = Apply();
        File.WriteAllText(In("Projects", "Work", "app.cs"), "code");

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.True(File.Exists(In("Projects", "Work", "app.cs")));
        Assert.Contains(@"Projects\Work", result.Kept);
        Assert.Contains("Projects", result.Kept);
        Assert.Contains(_base, result.Kept);
        Assert.False(Directory.Exists(In("Inbox")));
        Assert.False(Directory.Exists(In("Resources")));
    }

    [Fact]
    public void Existing_base_and_unrecorded_content_survive()
    {
        Directory.CreateDirectory(_base);
        File.WriteAllText(In("notes.txt"), "mine");
        var record = Apply();

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.True(File.Exists(In("notes.txt")));
        Assert.DoesNotContain(_base, result.Kept);
        Assert.False(Directory.Exists(In("Inbox")));
        Assert.False(File.Exists(In("README.md")));
    }

    [Fact]
    public void Edited_readme_is_kept()
    {
        var record = Apply();
        File.AppendAllText(In("README.md"), "my notes");

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.True(File.Exists(In("README.md")));
        Assert.Contains(WorkspaceSetup.KeptReadmeEdited, result.Kept);
    }

    [Fact]
    public void Installers_changed_by_user_is_not_restored()
    {
        var record = Apply();
        _rules.Rules.Single(r => r.Name == "Installers").Destination = @"C:\Mine";

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.Equal(@"C:\Mine", _rules.Rules.Single(r => r.Name == "Installers").Destination);
        Assert.Contains(WorkspaceSetup.KeptInstallersChanged, result.Kept);
    }

    [Fact]
    public void Edited_ebooks_rule_is_kept()
    {
        var record = Apply();
        _rules.Rules.Single(r => r.Name == WorkspaceSetup.EbooksRuleName).Extensions.Add("azw");

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.Contains(_rules.Rules, r => r.Name == WorkspaceSetup.EbooksRuleName);
        Assert.Contains(WorkspaceSetup.KeptEbooksEdited, result.Kept);
    }

    [Fact]
    public void Renamed_created_folder_is_skipped_silently()
    {
        var record = Apply();
        Directory.Move(In("Sandbox"), In("Play"));

        var result = WorkspaceSetup.Undo(record, _settings, _rules);

        Assert.True(Directory.Exists(In("Play")));
        Assert.DoesNotContain("Sandbox", result.Kept);
    }

    [Fact]
    public void Unreadable_rules_skip_the_rule_steps()
    {
        var record = Apply();

        var result = WorkspaceSetup.Undo(record, _settings, rules: null);

        Assert.False(result.RulesChanged);
        Assert.Contains(WorkspaceSetup.KeptRulesUnreadable, result.Kept);
        Assert.False(Directory.Exists(_base));
    }
}
