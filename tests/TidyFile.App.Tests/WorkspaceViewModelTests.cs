using TidyFile.App.ViewModels;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.App.Tests;

public class WorkspaceViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tidyfile-ws-vm", Guid.NewGuid().ToString("N"));

    public WorkspaceViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private WorkspaceViewModel Create(string baseFolder, RulesDocument? rules = null, WorkspaceRecord? lastSetup = null) =>
        new(new AppSettings(), rules ?? DefaultRules.Create(), lastSetup, 2026, baseFolder);

    [Fact]
    public void Preview_lists_the_template_and_marks_existing_items()
    {
        var baseFolder = Path.Combine(_root, "Ws");
        Directory.CreateDirectory(Path.Combine(baseFolder, "Inbox"));

        var vm = Create(baseFolder);

        Assert.Equal(13, vm.PreviewLines.Count);
        Assert.Contains(@"Inbox\  (already exists, kept)", vm.PreviewLines);
        Assert.Contains(@"Sandbox\", vm.PreviewLines);
        Assert.Equal("README.md", vm.PreviewLines[^1]);
    }

    [Fact]
    public void Preview_starts_with_the_base_folder_when_it_will_be_created()
    {
        var baseFolder = Path.Combine(_root, "New");

        var vm = Create(baseFolder);

        Assert.Equal(14, vm.PreviewLines.Count);
        Assert.Equal(baseFolder + @"\", vm.PreviewLines[0]);
    }

    [Fact]
    public void Changing_the_base_folder_replans_and_notifies()
    {
        var first = Path.Combine(_root, "A");
        Directory.CreateDirectory(Path.Combine(first, "Inbox"));
        var vm = Create(first);
        var before = vm.PreviewLines;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.BaseFolder = Path.Combine(_root, "B");

        Assert.NotEqual(before, vm.PreviewLines);
        Assert.Contains(nameof(WorkspaceViewModel.PreviewLines), changed);
        Assert.Contains(nameof(WorkspaceViewModel.CanCreate), changed);
    }

    [Theory]
    [InlineData("Workspace")]
    [InlineData(@"Q:\Ws")] // Assumes no Q: drive on the test machine.
    public void Relative_or_missing_drive_base_cannot_be_created(string baseFolder)
    {
        var vm = Create(Path.Combine(_root, "Ws"));

        vm.BaseFolder = baseFolder;

        Assert.False(vm.CanCreate);
        Assert.NotNull(vm.BaseFolderError);
        Assert.Null(vm.Plan);
    }

    [Fact]
    public void Both_options_are_ticked_by_default()
    {
        Assert.Equal(new WorkspaceOptions(true, true), Create(Path.Combine(_root, "Ws")).Options);
    }

    [Fact]
    public void Suggest_description_reflects_the_rules()
    {
        var vm = Create(Path.Combine(_root, "Ws"));
        Assert.Contains(@"Installers → Resources\Tools", vm.SuggestDescription);
        Assert.Contains("add rule Ebooks", vm.SuggestDescription);

        var unreadable = new WorkspaceViewModel(new AppSettings(), rules: null, lastSetup: null, 2026, Path.Combine(_root, "Ws"));
        Assert.Equal("rules.json couldn't be read; rules will be left unchanged.", unreadable.SuggestDescription);
    }

    [Fact]
    public void Undo_is_offered_only_with_a_previous_setup()
    {
        Assert.False(Create(Path.Combine(_root, "Ws")).HasUndo);

        var vm = Create(Path.Combine(_root, "Ws"),
            lastSetup: new WorkspaceRecord { BaseFolder = @"D:\Workspace", CreatedAt = DateTimeOffset.Now });

        Assert.True(vm.HasUndo);
        Assert.Contains(@"D:\Workspace", vm.UndoDescription);
    }
}
