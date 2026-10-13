using TidyFile.App.ViewModels;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.App.Tests;

public class SettingsViewModelTests
{
    private static readonly string Docs = Path.Combine(Path.GetTempPath(), "Docs");
    private static readonly string Finance = Path.Combine(Path.GetTempPath(), "Finance");

    private static SettingsViewModel Create(params Rule[] rules) =>
        new(new AppSettings(), new RulesDocument { Rules = rules.ToList() }, startWithWindows: false);

    private static Rule PdfRule(string name, string destination) =>
        new() { Name = name, Extensions = { "pdf" }, Destination = destination };

    // ----- Dirty tracking -----

    [Fact]
    public void Is_not_dirty_when_just_opened()
    {
        Assert.False(Create(PdfRule("Docs", Docs)).IsDirty);
    }

    [Fact]
    public void Selecting_and_testing_do_not_count_as_changes()
    {
        var vm = Create(PdfRule("Docs", Docs), PdfRule("Finance", Finance));

        vm.SelectedRule = vm.Rules[1];
        vm.SelectedFolder = vm.Folders[0];
        vm.TestFileName = "report.pdf";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Setting_the_same_value_does_not_count_as_a_change()
    {
        var vm = Create();

        vm.PopupSeconds = vm.PopupSeconds;
        vm.MaxSuggestions = vm.MaxSuggestions;
        vm.StartWithWindows = vm.StartWithWindows;

        Assert.False(vm.IsDirty);
    }

    public static TheoryData<string, Action<SettingsViewModel>> Edits => new()
    {
        { "start with Windows", vm => vm.StartWithWindows = true },
        { "popup seconds", vm => vm.PopupSeconds = 60 },
        { "max suggestions", vm => vm.MaxSuggestions = 1 },
        { "add folder", vm => vm.AddFolder(Path.GetTempPath()) },
        { "remove folder", vm => { vm.SelectedFolder = vm.Folders[0]; vm.RemoveSelectedFolder(); } },
        { "add rule", vm => vm.AddRule() },
        { "delete rule", vm => vm.DeleteSelectedRule() },
        { "move rule", vm => { vm.SelectedRule = vm.Rules[0]; vm.MoveSelectedRule(+1); } },
        { "rename rule", vm => vm.Rules[0].Name = "Renamed" },
        { "disable rule", vm => vm.Rules[1].Enabled = false },
        { "trust rule", vm => vm.Rules[0].AutoMove = true },
        { "edit existing rule", vm => vm.Rules[1].ExtensionsText = "docx" },
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void Edits_make_it_dirty(string what, Action<SettingsViewModel> edit)
    {
        var vm = Create(PdfRule("Docs", Docs), PdfRule("Finance", Finance));

        edit(vm);

        Assert.True(vm.IsDirty, what);
    }

    [Fact]
    public void Added_rules_are_tracked_too()
    {
        var vm = Create(PdfRule("Docs", Docs));
        vm.AddRule();
        var added = vm.SelectedRule!;

        // The add itself is a change; check that later edits to the new rule are also seen (handler attached).
        vm.TestFileName = "x.pdf";
        added.ExtensionsText = "pdf";
        added.Destination = Finance;

        Assert.Contains("New rule", vm.TestResult);
    }

    [Fact]
    public void Tester_uses_the_website_when_given()
    {
        var vm = Create(new Rule { Name = "GitHub zips", Extensions = { "zip" }, Sources = { "github.com" }, Destination = Docs });

        vm.TestFileName = "release.zip";
        Assert.StartsWith("No rule matches", vm.TestResult);

        vm.TestSource = "https://github.com/x/y";
        Assert.Contains("✓ GitHub zips", vm.TestResult);
        Assert.False(vm.IsDirty); // Testing is not an edit.
    }

    [Fact]
    public void Tester_says_when_a_file_would_be_moved_without_asking()
    {
        var vm = Create(PdfRule("Docs", Docs));
        vm.TestFileName = "a.pdf";
        Assert.StartsWith("✓ = shown in the popup", vm.TestResult);

        vm.Rules[0].AutoMove = true;
        Assert.StartsWith("Moved without asking to", vm.TestResult);
        Assert.Contains("\"Docs\"", vm.TestResult);
    }

    // ----- Folders -----

    [Fact]
    public void Adding_a_folder_twice_is_refused()
    {
        var vm = Create();
        var folder = Path.Combine(Path.GetTempPath(), "TidyFile watched");
        var count = vm.Folders.Count;

        Assert.True(vm.AddFolder(folder));
        Assert.False(vm.AddFolder(folder + Path.DirectorySeparatorChar));
        Assert.False(vm.AddFolder(folder.ToUpperInvariant()));
        Assert.Equal(count + 1, vm.Folders.Count);
    }

    [Fact]
    public void Adding_a_folder_already_listed_as_a_token_is_refused()
    {
        var vm = Create(); // Defaults watch {Downloads} and {Desktop}.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.False(vm.AddFolder(desktop));
    }

    // ----- Rule list -----

    [Fact]
    public void Rules_move_within_bounds_and_keep_the_selection()
    {
        var vm = Create(PdfRule("A", Docs), PdfRule("B", Docs), PdfRule("C", Docs));
        vm.SelectedRule = vm.Rules[0];

        vm.MoveSelectedRule(-1); // Already at the top: nothing happens.
        Assert.Equal("ABC", string.Concat(vm.Rules.Select(r => r.Name)));

        vm.MoveSelectedRule(+1);
        Assert.Equal("BAC", string.Concat(vm.Rules.Select(r => r.Name)));
        Assert.Equal("A", vm.SelectedRule!.Name);
    }

    [Fact]
    public void New_rule_is_inserted_after_the_selection()
    {
        var vm = Create(PdfRule("A", Docs), PdfRule("B", Docs));
        vm.SelectedRule = vm.Rules[0];

        vm.AddRule();

        Assert.Equal(new[] { "A", "New rule", "B" }, vm.Rules.Select(r => r.Name));
        Assert.Same(vm.Rules[1], vm.SelectedRule);
    }

    [Fact]
    public void Deleting_selects_the_next_rule()
    {
        var vm = Create(PdfRule("A", Docs), PdfRule("B", Docs), PdfRule("C", Docs));
        vm.SelectedRule = vm.Rules[1];

        vm.DeleteSelectedRule();
        Assert.Equal("C", vm.SelectedRule!.Name);

        vm.DeleteSelectedRule();
        Assert.Equal("A", vm.SelectedRule!.Name);

        vm.DeleteSelectedRule();
        Assert.Null(vm.SelectedRule);
    }

    // ----- Validation -----

    [Fact]
    public void Validate_lists_only_enabled_rules_with_problems()
    {
        var vm = Create(
            PdfRule("Good", Docs),
            PdfRule("Relative", "Finance"),
            new Rule { Name = "Off", Enabled = false, Extensions = { "pdf" } });

        var problem = Assert.Single(vm.Validate());
        Assert.Contains("Relative", problem);
    }

    [Fact]
    public void Duplicate_names_among_enabled_rules_are_reported()
    {
        var vm = Create(PdfRule("Docs", Docs), PdfRule("docs ", Finance), PdfRule("Other", Docs));
        Assert.Equal(new[] { "Docs" }, vm.DuplicateRuleNames());

        vm.Rules[1].Enabled = false;
        Assert.Empty(vm.DuplicateRuleNames());
    }

    // ----- Rules warning -----

    [Fact]
    public void Rules_warning_says_saving_will_replace_the_file()
    {
        var vm = new SettingsViewModel(new AppSettings(), new RulesDocument(), false, "rules.json has a JSON error.");

        Assert.StartsWith("rules.json has a JSON error.", vm.RulesWarning);
        Assert.Contains("replace", vm.RulesWarning);
        Assert.Null(Create().RulesWarning);
    }

    // ----- Save -----

    [Fact]
    public void ApplyTo_copies_the_edits_and_leaves_the_original_untouched()
    {
        var original = new AppSettings();
        var vm = new SettingsViewModel(original, new RulesDocument(), false) { PopupSeconds = 45, MaxSuggestions = 2 };
        vm.AddFolder(Path.GetTempPath());

        var target = new AppSettings();
        vm.ApplyTo(target);

        Assert.Equal(45, target.PopupSeconds);
        Assert.Equal(2, target.MaxSuggestions);
        Assert.Equal(3, target.WatchFolders.Count);
        Assert.Equal(20, original.PopupSeconds);
        Assert.Equal(2, original.WatchFolders.Count);
    }

    [Fact]
    public void Out_of_range_numbers_are_clamped()
    {
        var vm = new SettingsViewModel(new AppSettings { PopupSeconds = 1, MaxSuggestions = 99 }, new RulesDocument(), false);

        Assert.Equal(SettingsViewModel.MinPopupSeconds, vm.PopupSeconds);
        Assert.Equal(5, vm.MaxSuggestions);
    }

    [Fact]
    public void ToRulesDocument_keeps_the_order()
    {
        var vm = Create(PdfRule("A", Docs), PdfRule("B", Docs));
        vm.SelectedRule = vm.Rules[1];
        vm.MoveSelectedRule(-1);

        Assert.Equal(new[] { "B", "A" }, vm.ToRulesDocument().Rules.Select(r => r.Name));
    }
}
