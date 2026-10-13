namespace TidyFile.Core.History;

public enum HistoryAction
{
    Move,
    Undo,
}

/// <summary>One line in history.jsonl.</summary>
/// <param name="Id">Unique id of this entry.</param>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Action">Move or Undo.</param>
/// <param name="From">Full path before the action.</param>
/// <param name="To">Full path after the action.</param>
/// <param name="RuleName">Rule that suggested the destination, or null if the user browsed.</param>
/// <param name="UndoOf">For Undo entries: the Id of the Move that was reverted.</param>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    HistoryAction Action,
    string From,
    string To,
    string? RuleName = null,
    Guid? UndoOf = null);
