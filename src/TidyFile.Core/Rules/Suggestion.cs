namespace TidyFile.Core.Rules;

/// <summary>A suggested destination folder for a file.</summary>
/// <param name="Destination">Absolute destination folder (tokens already expanded).</param>
/// <param name="Reason">Short human-readable explanation shown in the popup.</param>
/// <param name="Confidence">0..1, higher is better.</param>
/// <param name="RuleName">Rule that produced the suggestion, if any.</param>
/// <param name="AutoMove">The rule says to move without asking. Never true for learned suggestions.</param>
public sealed record Suggestion(string Destination, string Reason, double Confidence, string? RuleName, bool AutoMove = false);
