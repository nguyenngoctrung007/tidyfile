using TidyFile.Core.Files;

namespace TidyFile.Core.History;

/// <summary>Moves files, records every move in history, and can undo them.</summary>
public sealed class MoveService
{
    private readonly HistoryStore _history;

    public MoveService(HistoryStore history)
    {
        _history = history;
    }

    /// <summary>
    /// Called with the final target path just before a file is written into a folder.
    /// The watcher uses it so files TidyFile moves itself (e.g. an undo back into Downloads) don't trigger a popup.
    /// </summary>
    public Action<string>? ExpectIncoming { get; set; }

    /// <summary>Called when a move announced through <see cref="ExpectIncoming"/> failed.</summary>
    public Action<string>? CancelIncoming { get; set; }

    public HistoryEntry Move(string sourcePath, string destinationFolder, string? ruleName)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The file no longer exists.", sourcePath);
        }

        var target = FileMover.PlanTarget(sourcePath, destinationFolder);
        MoveAnnounced(sourcePath, target);

        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, sourcePath, target, ruleName);
        _history.Append(entry);
        return entry;
    }

    /// <summary>Moves the file back to where it came from (with a new name if that spot is taken).</summary>
    public HistoryEntry Undo(HistoryEntry move)
    {
        if (move.Action != HistoryAction.Move)
        {
            throw new ArgumentException("Only Move entries can be undone.", nameof(move));
        }

        if (!File.Exists(move.To))
        {
            throw new FileNotFoundException("The moved file is no longer where TidyFile put it.", move.To);
        }

        var target = FileMover.GetUniquePath(move.From);
        MoveAnnounced(move.To, target);

        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Undo, move.To, target, move.RuleName, move.Id);
        _history.Append(entry);
        return entry;
    }

    private void MoveAnnounced(string source, string target)
    {
        ExpectIncoming?.Invoke(target);
        try
        {
            FileMover.MoveExact(source, target);
        }
        catch
        {
            CancelIncoming?.Invoke(target);
            throw;
        }
    }
}
