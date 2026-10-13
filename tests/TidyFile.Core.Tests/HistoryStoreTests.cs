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

        Assert.Equal(2, new HistoryStore(path).ReadAll().Count); // A fresh store reads the file from disk.
    }

    [Fact]
    public void Entries_are_kept_in_memory_after_the_first_read()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("history.jsonl");
        var store = new HistoryStore(path);
        store.Append(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, "a", "b"));
        Assert.Single(store.ReadAll());

        File.Delete(path); // Not re-read...
        store.Append(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, "c", "d", Source: "github.com"));

        var all = store.ReadAll(); // ...but appends still show up.
        Assert.Equal(2, all.Count);
        Assert.Equal("github.com", all[1].Source);
        Assert.Single(new HistoryStore(path).ReadAll()); // And they were written to disk.
    }

    [Fact]
    public void Old_lines_without_a_source_still_load()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("history.jsonl",
            "{\"id\":\"6f1c3a2e-0000-0000-0000-000000000001\",\"timestamp\":\"2026-10-01T10:00:00+02:00\",\"action\":\"Move\",\"from\":\"a\",\"to\":\"b\",\"ruleName\":\"Docs\"}\n");

        var entry = Assert.Single(new HistoryStore(path).ReadAll());
        Assert.Null(entry.Source);
        Assert.Equal("Docs", entry.RuleName);
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
