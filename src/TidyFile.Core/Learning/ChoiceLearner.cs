using System.Text.RegularExpressions;
using TidyFile.Core.Files;
using TidyFile.Core.History;
using TidyFile.Core.Rules;

namespace TidyFile.Core.Learning;

/// <summary>
/// Suggests folders from the user's past moves in history.jsonl.
/// A past move only counts for files with the same extension. Among those, moves of files from the same
/// website, or sharing a word in the name, are stronger evidence than the extension alone.
/// Rules (agreed for Phase 2):
///   - a folder needs at least <see cref="MinMoves"/> moves there (undone moves count against it);
///   - confidence never exceeds <see cref="MaxConfidence"/>, so an extension + name rule (0.9) always wins;
///   - the website is only ever a refinement of the extension, never a reason on its own.
/// </summary>
public static class ChoiceLearner
{
    public const int MinMoves = 2;
    public const double MaxConfidence = 0.85;

    // Base confidence per kind of evidence; up to ConsistencyWeight more when the moves all went to one folder.
    // Result ranges: same website 0.70–0.85, similar name 0.65–0.80, same extension 0.50–0.65.
    private const double BaseSameSource = 0.70;
    private const double BaseSimilarName = 0.65;
    private const double BaseSameExtension = 0.50;
    private const double ConsistencyWeight = 0.15;

    // Old habits fade: only the most recent moves are considered.
    private const int MaxMovesConsidered = 5000;

    private static readonly Regex WordSplit = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Words that appear in all kinds of file names and say nothing about where a file belongs.
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "from", "with", "copy", "new", "final", "draft", "file", "document", "untitled",
        "download", "img", "image", "scan", "screenshot", "version",
    };

    /// <summary>Up to <paramref name="max"/> learned suggestions for <paramref name="filePath"/>, best first.</summary>
    public static IReadOnlyList<Suggestion> Suggest(
        IReadOnlyList<HistoryEntry> history,
        string filePath,
        DownloadSource? source = null,
        int max = 3)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (max <= 0 || extension.Length < 2)
        {
            return Array.Empty<Suggestion>();
        }

        var undone = history
            .Where(e => e.Action == HistoryAction.Undo && e.UndoOf is not null)
            .Select(e => e.UndoOf!.Value)
            .ToHashSet();

        var past = history
            .Where(e => e.Action == HistoryAction.Move)
            .TakeLast(MaxMovesConsidered)
            .Where(e => Path.GetExtension(e.From).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Select(e => new PastMove(FolderOf(e.To), e.Source, Words(e.From), undone.Contains(e.Id)))
            .Where(p => p.Folder.Length > 0)
            .ToList();

        if (past.Count == 0)
        {
            return Array.Empty<Suggestion>();
        }

        var type = extension.TrimStart('.').ToUpperInvariant();
        var domain = source?.DisplayDomain;
        var words = Words(filePath);
        var best = new Dictionary<string, Suggestion>(StringComparer.OrdinalIgnoreCase);

        if (domain is not null)
        {
            Consider(best, past.Where(p => string.Equals(p.Source, domain, StringComparison.OrdinalIgnoreCase)),
                BaseSameSource, n => $"You moved {n} {type} files from {domain} here");
        }

        if (words.Count > 0)
        {
            Consider(best, past.Where(p => p.Words.Overlaps(words)),
                BaseSimilarName, n => $"You moved {n} similar {type} files here");
        }

        Consider(best, past, BaseSameExtension, n => $"You moved {n} {type} files here");

        var currentFolder = FolderOf(filePath);
        return best.Values
            .Where(s => !s.Destination.Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
            .Where(s => Directory.Exists(s.Destination)) // The user may have deleted or renamed it since.
            .OrderByDescending(s => s.Confidence)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Combines rule and learned suggestions into one ranked list without duplicate folders.
    /// When both suggest the same folder, the rule's entry is kept (so the rule name is still recorded)
    /// with the higher of the two confidences.
    /// </summary>
    public static IReadOnlyList<Suggestion> Merge(IReadOnlyList<Suggestion> fromRules, IReadOnlyList<Suggestion> learned, int max)
    {
        var merged = new List<Suggestion>(fromRules);
        foreach (var suggestion in learned)
        {
            var index = merged.FindIndex(s => SameFolder(s.Destination, suggestion.Destination));
            if (index < 0)
            {
                merged.Add(suggestion);
            }
            else if (suggestion.Confidence > merged[index].Confidence)
            {
                merged[index] = merged[index] with { Confidence = suggestion.Confidence };
            }
        }

        // Stable sort: on equal confidence rules (added first) stay ahead of learned suggestions.
        return merged.OrderByDescending(s => s.Confidence).Take(Math.Max(0, max)).ToList();
    }

    private static void Consider(
        Dictionary<string, Suggestion> best,
        IEnumerable<PastMove> evidence,
        double baseConfidence,
        Func<int, string> reason)
    {
        var moves = evidence.ToList();
        var keptTotal = moves.Count(m => !m.Undone);
        if (keptTotal == 0)
        {
            return;
        }

        foreach (var folder in moves.GroupBy(m => m.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var kept = folder.Count(m => !m.Undone);
            var net = kept - folder.Count(m => m.Undone);
            if (net < MinMoves)
            {
                continue;
            }

            var consistency = (double)kept / keptTotal;
            var confidence = Math.Min(MaxConfidence, baseConfidence + ConsistencyWeight * consistency);
            if (!best.TryGetValue(folder.Key, out var existing) || confidence > existing.Confidence)
            {
                best[folder.Key] = new Suggestion(folder.Key, reason(kept), confidence, RuleName: null);
            }
        }
    }

    /// <summary>Distinctive lower-case words of a file name ("Invoice_ACME-2026.pdf" → invoice, acme).</summary>
    internal static HashSet<string> Words(string path) =>
        WordSplit.Split(Path.GetFileNameWithoutExtension(path))
            .Where(w => w.Length >= 3 && !w.All(char.IsDigit) && !NoiseWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

    private static string FolderOf(string path)
    {
        var folder = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(folder) ? "" : folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool SameFolder(string a, string b) =>
        a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed record PastMove(string Folder, string? Source, HashSet<string> Words, bool Undone);
}
