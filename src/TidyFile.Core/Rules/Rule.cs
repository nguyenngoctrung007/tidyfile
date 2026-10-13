namespace TidyFile.Core.Rules;

/// <summary>
/// One user rule. A rule matches a file when:
///   - its extension is in <see cref="Extensions"/> (or the list is empty), and
///   - its file name matches <see cref="NamePattern"/> (or the pattern is empty).
/// A rule with neither extensions nor a pattern never matches.
/// </summary>
public sealed class Rule
{
    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Extensions such as "pdf" or ".pdf" (case-insensitive).</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Optional case-insensitive regular expression tested against the full file name.</summary>
    public string? NamePattern { get; set; }

    /// <summary>Destination folder. Supports tokens like {Documents} and %ENV% variables.</summary>
    public string Destination { get; set; } = "";
}

/// <summary>The JSON document stored in rules.json.</summary>
public sealed class RulesDocument
{
    public int Version { get; set; } = 1;

    public List<Rule> Rules { get; set; } = new();
}
