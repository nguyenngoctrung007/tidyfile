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
    public async Task MoveAsync_and_UndoAsync_round_trip()
    {
        using var temp = new TempFolder();
        var history = new HistoryStore(temp.Combine("history.jsonl"));
        var service = new MoveService(history);
        var source = temp.CreateFile(Path.Combine("Downloads", "photo.png"));

        var move = await service.MoveAsync(source, temp.Combine("Pictures"), "Images");
        Assert.True(File.Exists(move.To));

        var undo = await service.UndoAsync(move);
        Assert.Equal(source, undo.To);
        Assert.True(File.Exists(source));
        Assert.Empty(history.RecentUndoableMoves());
    }

    [Fact]
    public async Task Simultaneous_moves_of_same_named_files_get_different_names()
    {
        using var temp = new TempFolder();
        var service = new MoveService(new HistoryStore(temp.Combine("history.jsonl")));
        var destination = temp.Combine("Docs");
        var sources = Enumerable.Range(0, 8)
            .Select(i => temp.CreateFile(Path.Combine($"In{i}", "report.pdf"), $"copy {i}"))
            .ToList();

        var moves = await Task.WhenAll(sources.Select(s => service.MoveAsync(s, destination, null)));

        Assert.Equal(8, moves.Select(m => m.To).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(8, Directory.GetFiles(destination).Length);
        Assert.True(service.WaitUntilIdle(TimeSpan.Zero));
    }

    [Fact]
    public async Task MoveAsync_reports_failures_through_the_task()
    {
        using var temp = new TempFolder();
        var service = new MoveService(new HistoryStore(temp.Combine("history.jsonl")));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.MoveAsync(temp.Combine("nope.txt"), temp.Combine("Docs"), null));
        Assert.True(service.WaitUntilIdle(TimeSpan.Zero)); // A failed move must not keep the lock.
    }

    [Fact]
    public void Moving_a_missing_file_throws()
    {
        using var temp = new TempFolder();
        var service = new MoveService(new HistoryStore(temp.Combine("history.jsonl")));

        Assert.Throws<FileNotFoundException>(() => service.Move(temp.Combine("nope.txt"), temp.Combine("Docs"), null));
    }
}
