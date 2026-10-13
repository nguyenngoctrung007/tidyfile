using TidyFile.Core.Files;

namespace TidyFile.Core.History;

/// <summary>
/// Moves files, records every move in history, and can undo them.
/// Moves run one at a time: two moves into the same folder could otherwise pick the same free name.
/// </summary>
public sealed class MoveService
{
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

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

    /// <summary>
    /// <see cref="Move"/> on a background thread. A move to another drive copies the whole file,
    /// which would otherwise freeze the popup and the tray menu.
    /// </summary>
    public Task<HistoryEntry> MoveAsync(string sourcePath, string destinationFolder, string? ruleName, string? sourceDomain = null) =>
        Task.Run(() => Move(sourcePath, destinationFolder, ruleName, sourceDomain));

    /// <summary><see cref="Undo"/> on a background thread.</summary>
    public Task<HistoryEntry> UndoAsync(HistoryEntry move) => Task.Run(() => Undo(move));

    /// <summary>
    /// Waits until no move is running (used on exit, so a cross-drive copy is not cut off half-way).
    /// Returns false if <paramref name="timeout"/> passed first.
    /// </summary>
    public bool WaitUntilIdle(TimeSpan timeout)
    {
        if (!_oneAtATime.Wait(timeout))
        {
            return false;
        }

        _oneAtATime.Release();
        return true;
    }

    /// <param name="sourceDomain">Website the file was downloaded from, if known; recorded for learning.</param>
    public HistoryEntry Move(string sourcePath, string destinationFolder, string? ruleName, string? sourceDomain = null)
    {
        _oneAtATime.Wait();
        try
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The file no longer exists.", sourcePath);
            }

            var target = FileMover.PlanTarget(sourcePath, destinationFolder);
            MoveAnnounced(sourcePath, target);

            var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move, sourcePath, target, ruleName,
                Source: sourceDomain);
            _history.Append(entry);
            return entry;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Moves the file back to where it came from (with a new name if that spot is taken).</summary>
    public HistoryEntry Undo(HistoryEntry move)
    {
        if (move.Action != HistoryAction.Move)
        {
            throw new ArgumentException("Only Move entries can be undone.", nameof(move));
        }

        _oneAtATime.Wait();
        try
        {
            if (!File.Exists(move.To))
            {
                throw new FileNotFoundException("The moved file is no longer where TidyFile put it.", move.To);
            }

            var target = FileMover.GetUniquePath(move.From);
            MoveAnnounced(move.To, target);

            var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Undo, move.To, target, move.RuleName, move.Id, move.Source);
            _history.Append(entry);
            return entry;
        }
        finally
        {
            _oneAtATime.Release();
        }
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
