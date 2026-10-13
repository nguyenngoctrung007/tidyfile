using TidyFile.Core.Files;

namespace TidyFile.Core.Tests;

public class FileMoverTests
{
    [Fact]
    public void Unique_path_is_unchanged_when_free()
    {
        using var temp = new TempFolder();
        var desired = temp.Combine("report.pdf");

        Assert.Equal(desired, FileMover.GetUniquePath(desired));
    }

    [Fact]
    public void Unique_path_adds_a_counter_on_clash()
    {
        using var temp = new TempFolder();
        temp.CreateFile("report.pdf");
        temp.CreateFile("report (1).pdf");

        Assert.Equal(temp.Combine("report (2).pdf"), FileMover.GetUniquePath(temp.Combine("report.pdf")));
    }

    [Fact]
    public void Plan_and_move_never_overwrite()
    {
        using var temp = new TempFolder();
        var source = temp.CreateFile(Path.Combine("Downloads", "a.txt"), "new");
        temp.CreateFile(Path.Combine("Docs", "a.txt"), "old");

        var target = FileMover.PlanTarget(source, temp.Combine("Docs"));
        FileMover.MoveExact(source, target);

        Assert.Equal(temp.Combine("Docs", "a (1).txt"), target);
        Assert.Equal("old", File.ReadAllText(temp.Combine("Docs", "a.txt")));
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Move_creates_the_destination_folder()
    {
        using var temp = new TempFolder();
        var source = temp.CreateFile("a.txt");

        var target = FileMover.PlanTarget(source, temp.Combine("New", "Nested"));
        FileMover.MoveExact(source, target);

        Assert.True(File.Exists(target));
    }
}
