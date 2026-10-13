using TidyFile.Core.History;

namespace TidyFile.Core.Tests;

public class MoveServiceTests
{
    [Fact]
    public void Move_then_undo_round_trips_and_is_recorded()
    {
        using var temp = new TempFolder();
        var history = new HistoryStore(temp.Combine("data", "history.jsonl"));
        var expected = new List<string>();
        var service = new MoveService(history) { ExpectIncoming = expected.Add };
        var source = temp.CreateFile(Path.Combine("Downloads", "invoice.pdf"));

        var move = service.Move(source, temp.Combine("Finance"), "Invoices");

        Assert.Equal(temp.Combine("Finance", "invoice.pdf"), move.To);
        Assert.True(File.Exists(move.To));
        Assert.Single(history.RecentUndoableMoves());

        var undo = service.Undo(move);

        Assert.Equal(source, undo.To);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(move.To));
        Assert.Empty(history.RecentUndoableMoves());
        Assert.Equal(new[] { move.To, source }, expected);
    }

    [Fact]
    public void Undo_does_not_overwrite_a_new_file_with_the_same_name()
    {
        using var temp = new TempFolder();
        var service = new MoveService(new HistoryStore(temp.Combine("history.jsonl")));
        var source = temp.CreateFile(Path.Combine("Downloads", "a.txt"), "first");
        var move = service.Move(source, temp.Combine("Docs"), null);
        temp.CreateFile(Path.Combine("Downloads", "a.txt"), "second");

        var undo = service.Undo(move);

        Assert.Equal(temp.Combine("Downloads", "a (1).txt"), undo.To);
        Assert.Equal("second", File.ReadAllText(source));
        Assert.Equal("first", File.ReadAllText(undo.To));
    }

    [Fact]
    public void Moving_a_missing_file_throws()
    {
        using var temp = new TempFolder();
        var service = new MoveService(new HistoryStore(temp.Combine("history.jsonl")));

        Assert.Throws<FileNotFoundException>(() => service.Move(temp.Combine("nope.txt"), temp.Combine("Docs"), null));
    }
}
