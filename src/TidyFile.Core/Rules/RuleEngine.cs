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

            var problem = Validate(rule);
            if (problem is not null)
            {
                _problems.Add($"Rule '{rule.Name}' was skipped: {problem}");
                continue;
            }

            var regex = string.IsNullOrWhiteSpace(rule.NamePattern) ? null : CreateRegex(rule.NamePattern);
            var extensions = new HashSet<string>(
                rule.Extensions.Where(e => !string.IsNullOrWhiteSpace(e)).Select(NormalizeExtension),
                StringComparer.OrdinalIgnoreCase);

            _rules.Add(new CompiledRule(rule, extensions, regex));
        }
    }

    /// <summary>Returns why a rule cannot be used, or null if it is valid. Used by the settings screen too.</summary>
    public static string? Validate(Rule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Destination))
        {
            return "it has no destination folder.";
        }

        var expanded = PathTokens.Expand(rule.Destination.Trim());
        if (!Path.IsPathFullyQualified(expanded))
        {
            return "its destination must be a full path such as D:\\Work or {Documents}\\Finance.";
        }

        var patternError = ValidatePattern(rule.NamePattern);
        if (patternError is not null)
        {
            return "its name pattern is invalid: " + patternError;
        }

        var hasExtensions = rule.Extensions.Any(e => !string.IsNullOrWhiteSpace(e));
        if (!hasExtensions && string.IsNullOrWhiteSpace(rule.NamePattern))
        {
            return "it needs at least one extension or a name pattern.";
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            return "it has no name.";
        }

        return null;
    }

    /// <summary>
    /// Names used by more than one rule (ignoring case and surrounding spaces), in first-seen order.
    /// Not fatal, but the popup shows "Rule: &lt;name&gt;", so duplicates are confusing.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateNames(IEnumerable<Rule> rules) =>
        rules
            .Select(r => r.Name.Trim())
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

    /// <summary>Returns the regex error message, or null if the pattern is empty or valid.</summary>
    public static string? ValidatePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        try
        {
            CreateRegex(pattern);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static Regex CreateRegex(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

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

    /// <summary>
    /// Every rule that matches <paramref name="fileName"/>, best first, without removing duplicates.
    /// Used by the "Test a file name" box in Settings.
    /// </summary>
    public IReadOnlyList<Suggestion> Explain(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        var extension = NormalizeExtension(Path.GetExtension(fileName));

        var matches = new List<(Suggestion Suggestion, int Order)>();
        for (var i = 0; i < _rules.Count; i++)
        {
            var score = _rules[i].Score(fileName, extension);
            if (score > 0)
            {
                var rule = _rules[i].Rule;
                matches.Add((new Suggestion(PathTokens.Expand(rule.Destination), $"Rule: {rule.Name}", score, rule.Name), i));
            }
        }

        return matches
            .OrderByDescending(m => m.Suggestion.Confidence)
            .ThenBy(m => m.Order)
            .Select(m => m.Suggestion)
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
