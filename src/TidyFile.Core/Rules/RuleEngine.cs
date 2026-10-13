using System.Text.RegularExpressions;
using TidyFile.Core.Paths;

namespace TidyFile.Core.Rules;

/// <summary>
/// Turns a list of <see cref="Rule"/>s into ranked folder suggestions for a file.
/// Instances are immutable and safe to share between threads.
/// </summary>
public sealed class RuleEngine
{
    private const double ScoreExtensionAndName = 0.9;
    private const double ScoreNameOnly = 0.8;
    private const double ScoreExtensionOnly = 0.6;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    private readonly List<CompiledRule> _rules = new();
    private readonly List<string> _problems = new();

    public RuleEngine(IEnumerable<Rule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Destination))
            {
                _problems.Add($"Rule '{rule.Name}' has no destination and was skipped.");
                continue;
            }

            Regex? regex = null;
            if (!string.IsNullOrWhiteSpace(rule.NamePattern))
            {
                try
                {
                    regex = new Regex(rule.NamePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                }
                catch (ArgumentException ex)
                {
                    _problems.Add($"Rule '{rule.Name}' has an invalid name pattern and was skipped: {ex.Message}");
                    continue;
                }
            }

            var extensions = new HashSet<string>(
                rule.Extensions.Where(e => !string.IsNullOrWhiteSpace(e)).Select(NormalizeExtension),
                StringComparer.OrdinalIgnoreCase);

            if (extensions.Count == 0 && regex is null)
            {
                _problems.Add($"Rule '{rule.Name}' has neither extensions nor a name pattern and was skipped.");
                continue;
            }

            _rules.Add(new CompiledRule(rule, extensions, regex));
        }
    }

    /// <summary>Problems found while loading rules (invalid regex, missing destination, ...).</summary>
    public IReadOnlyList<string> Problems => _problems;

    public int RuleCount => _rules.Count;

    /// <summary>Returns up to <paramref name="max"/> distinct destinations, best first.</summary>
    public IReadOnlyList<Suggestion> Suggest(string filePath, int max = 3)
    {
        if (max <= 0)
        {
            return Array.Empty<Suggestion>();
        }

        var fileName = Path.GetFileName(filePath);
        var extension = NormalizeExtension(Path.GetExtension(fileName));
        var currentFolder = NormalizeFolder(Path.GetDirectoryName(filePath) ?? "");

        var candidates = new List<(Suggestion Suggestion, int Order)>();
        for (var i = 0; i < _rules.Count; i++)
        {
            var score = _rules[i].Score(fileName, extension);
            if (score <= 0)
            {
                continue;
            }

            var rule = _rules[i].Rule;
            var destination = NormalizeFolder(PathTokens.Expand(rule.Destination));
            if (destination.Length == 0 || destination.Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue; // Suggesting the folder the file is already in is pointless.
            }

            candidates.Add((new Suggestion(destination, $"Rule: {rule.Name}", score, rule.Name), i));
        }

        return candidates
            .OrderByDescending(c => c.Suggestion.Confidence)
            .ThenBy(c => c.Order)
            .Select(c => c.Suggestion)
            .DistinctBy(s => s.Destination, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    internal static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (extension.Length == 0)
        {
            return "";
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "";
        }

        try
        {
            folder = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }

        var root = Path.GetPathRoot(folder) ?? "";
        return folder.Length > root.Length
            ? folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : folder;
    }

    private sealed record CompiledRule(Rule Rule, HashSet<string> Extensions, Regex? NameRegex)
    {
        public double Score(string fileName, string extension)
        {
            var hasExtensions = Extensions.Count > 0;
            if (hasExtensions && !Extensions.Contains(extension))
            {
                return 0;
            }

            if (NameRegex is not null)
            {
                bool nameMatches;
                try
                {
                    nameMatches = NameRegex.IsMatch(fileName);
                }
                catch (RegexMatchTimeoutException)
                {
                    nameMatches = false;
                }

                if (!nameMatches)
                {
                    return 0;
                }

                return hasExtensions ? ScoreExtensionAndName : ScoreNameOnly;
            }

            return ScoreExtensionOnly;
        }
    }
}
