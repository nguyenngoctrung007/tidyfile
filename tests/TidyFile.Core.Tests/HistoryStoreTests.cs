using TidyFile.Core.History;

namespace TidyFile.Core.Tests;

public class HistoryStoreTests
{
    [Fact]
    public void Reading_a_missing_file_returns_nothing()
    {
        using var temp = new TempFolder();

        Assert.Empty(new HistoryStore(temp.Combine("history.jsonl")).ReadAll());
    }

    [Fact]
    public void Entries_survive_a_round_trip()
    {
        using var temp = new TempFolder();
        var store = new HistoryStore(temp.Combine("history.jsonl"));
        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, @"C:\a.pdf", @"C:\Docs\a.pdf", "Docs");

        store.Append(entry);

        Assert.Equal(entry, Assert.Single(store.ReadAll()));
    }

    [Fact]
    public void Damaged_lines_are_skipped()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("history.jsonl");
        var store = new HistoryStore(path);
        store.Append(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, "a", "b"));
        File.AppendAllText(path, "{ this is not json" + Environment.NewLine);
        store.Append(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, "c", "d"));

        Assert.Equal(2, store.ReadAll().Count);
    }

    [Fact]
    public void Recent_moves_are_newest_first_and_exclude_undone()
    {
        using var temp = new TempFolder();
        var store = new HistoryStore(temp.Combine("history.jsonl"));
        var now = DateTimeOffset.Now;
        var older = new HistoryEntry(Guid.NewGuid(), now.AddMinutes(-2), HistoryAction.Move, "a", "b");
        var undone = new HistoryEntry(Guid.NewGuid(), now.AddMinutes(-1), HistoryAction.Move, "c", "d");
        var newest = new HistoryEntry(Guid.NewGuid(), now, HistoryAction.Move, "e", "f");
        store.Append(older);
        store.Append(undone);
        store.Append(newest);
        store.Append(new HistoryEntry(Guid.NewGuid(), now, HistoryAction.Undo, "d", "c", UndoOf: undone.Id));

        var recent = store.RecentUndoableMoves();

        Assert.Equal(new[] { newest.Id, older.Id }, recent.Select(e => e.Id));
    }
}
