using TidyFile.Core.Files;
using TidyFile.Core.Paths;

namespace TidyFile.Core.Rules;

/// <summary>Builds rules from an example file, for the popup's "Always do this".</summary>
public static class RuleFactory
{
    /// <summary>
    /// What a rule made from this file would match, e.g. "PDF files from bank.com",
    /// or null if there is nothing to match on (no extension and no known website).
    /// </summary>
    public static string? Describe(string filePath, DownloadSource? source)
    {
        var type = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        var domain = source?.DisplayDomain;

        return (type.Length > 0, domain is not null) switch
        {
            (true, true) => $"{type} files from {domain}",
            (true, false) => $"{type} files",
            (false, true) => $"files from {domain}",
            _ => null,
        };
    }

    /// <summary>
    /// A rule that sends files like <paramref name="filePath"/> to <paramref name="destinationFolder"/>:
    /// same extension, and same website when known. No name pattern: one example is too little to guess it.
    /// Returns null when <see cref="Describe"/> does.
    /// </summary>
    public static Rule? FromExample(string filePath, string destinationFolder, DownloadSource? source)
    {
        var description = Describe(filePath, source);
        if (description is null)
        {
            return null;
        }

        var rule = new Rule
        {
            Name = char.ToUpperInvariant(description[0]) + description[1..],
            Destination = PathTokens.Tokenize(destinationFolder),
        };

        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (extension.Length > 0)
        {
            rule.Extensions.Add(extension);
        }

        if (source?.DisplayDomain is { } domain)
        {
            rule.Sources.Add(domain);
        }

        return rule;
    }

    /// <summary>
    /// Inserts <paramref name="rule"/> at the top of <paramref name="document"/> (so it wins ties with older rules),
    /// renaming it if the name is taken. Returns false, changing nothing, if an identical rule already exists.
    /// </summary>
    public static bool AddToTop(RulesDocument document, Rule rule)
    {
        if (document.Rules.Any(r => r.Enabled && SameConditions(r, rule) && SameFolder(r.Destination, rule.Destination)))
        {
            return false;
        }

        var baseName = rule.Name;
        for (var i = 2; document.Rules.Any(r => r.Name.Trim().Equals(rule.Name, StringComparison.OrdinalIgnoreCase)); i++)
        {
            rule.Name = $"{baseName} ({i})";
        }

        document.Rules.Insert(0, rule);
        return true;
    }

    private static bool SameConditions(Rule a, Rule b) =>
        string.IsNullOrWhiteSpace(a.NamePattern) && string.IsNullOrWhiteSpace(b.NamePattern) &&
        SameSet(a.Extensions.Select(e => RuleEngine.NormalizeExtension(e)), b.Extensions.Select(e => RuleEngine.NormalizeExtension(e))) &&
        SameSet(a.Sources.Select(DownloadSource.DomainOf).OfType<string>(), b.Sources.Select(DownloadSource.DomainOf).OfType<string>());

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a.Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase)
            .SetEquals(b.Where(x => x.Length > 0));

    private static bool SameFolder(string a, string b) =>
        Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string folder) =>
        PathTokens.Expand(folder.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
