using System.Text.Json;
using TidyFile.Core.Storage;

namespace TidyFile.Core.History;

/// <summary>
/// Append-only log of every move and undo, one JSON object per line.
/// Plain text on purpose: easy to inspect, and it will feed the "learning" feature in Phase 2.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions LineOptions = new(JsonFile.Options) { WriteIndented = false };

    private readonly string _path;
    private readonly object _gate = new();

    public HistoryStore(string path)
    {
        _path = path;
    }

    public void Append(HistoryEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, LineOptions) + Environment.NewLine;
        lock (_gate)
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.AppendAllText(_path, line);
        }
    }

    public IReadOnlyList<HistoryEntry> ReadAll()
    {
        string[] lines;
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<HistoryEntry>();
            }

            lines = File.ReadAllLines(_path);
        }

        var entries = new List<HistoryEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<HistoryEntry>(line, LineOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A damaged line must not break the whole history; skip it.
            }
        }

        return entries;
    }

    /// <summary>Most recent moves that have not been undone yet, newest first.</summary>
    public IReadOnlyList<HistoryEntry> RecentUndoableMoves(int max = 10)
    {
        var all = ReadAll();
        var undone = all
            .Where(e => e.Action == HistoryAction.Undo && e.UndoOf is not null)
            .Select(e => e.UndoOf!.Value)
            .ToHashSet();

        return all
            .Where(e => e.Action == HistoryAction.Move && !undone.Contains(e.Id))
            .OrderByDescending(e => e.Timestamp)
            .Take(max)
            .ToList();
    }
}
