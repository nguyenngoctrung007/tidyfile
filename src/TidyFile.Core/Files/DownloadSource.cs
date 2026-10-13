namespace TidyFile.Core.Files;

/// <summary>
/// Where a downloaded file came from, read from the "Zone.Identifier" alternate data stream
/// that browsers attach to downloads on NTFS drives. Read locally only; it is never sent anywhere.
/// </summary>
/// <param name="HostDomain">Domain of the file's own URL (often a CDN, e.g. objects.githubusercontent.com).</param>
/// <param name="ReferrerDomain">Domain of the page the download started from (e.g. github.com).</param>
public sealed record DownloadSource(string? HostDomain, string? ReferrerDomain)
{
    /// <summary>The page's domain if known (what the user recognises), else the file's.</summary>
    public string? DisplayDomain => ReferrerDomain ?? HostDomain;

    /// <summary>Both domains, for rule matching: a rule for github.com should match either.</summary>
    public IEnumerable<string> Domains =>
        new[] { ReferrerDomain, HostDomain }.OfType<string>().Distinct();

    /// <summary>Reads the source of <paramref name="filePath"/>, or null if there is none (not downloaded, FAT drive, ...).</summary>
    public static DownloadSource? Read(string filePath)
    {
        try
        {
            var streamPath = filePath + ":Zone.Identifier";
            return File.Exists(streamPath) ? Parse(File.ReadAllText(streamPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Parses the stream's INI-style text. Returns null if it has no usable URL.</summary>
    public static DownloadSource? Parse(string zoneIdentifier)
    {
        string? host = null;
        string? referrer = null;

        foreach (var rawLine in zoneIdentifier.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase))
            {
                host = DomainOf(line["HostUrl=".Length..]);
            }
            else if (line.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
            {
                referrer = DomainOf(line["ReferrerUrl=".Length..]);
            }
        }

        return host is null && referrer is null ? null : new DownloadSource(host, referrer);
    }

    /// <summary>
    /// "https://www.GitHub.com/x" → "github.com". Also accepts a bare domain ("github.com"), as typed in a rule.
    /// Returns null for things that aren't web addresses (about:internet, data:, file paths...).
    /// </summary>
    public static string? DomainOf(string? urlOrDomain)
    {
        var text = urlOrDomain?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // blob:https://site/uuid — the real origin is inside.
        if (text.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["blob:".Length..];
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            if (text.Contains(':') || text.Contains('\\'))
            {
                return null; // about:internet, C:\..., data:...
            }

            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "ftp") ||
            string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var domain = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        return domain.StartsWith("www.", StringComparison.Ordinal) ? domain[4..] : domain;
    }

    /// <summary>True if <paramref name="domain"/> is <paramref name="ruleDomain"/> or one of its subdomains.</summary>
    public static bool Matches(string domain, string ruleDomain) =>
        domain.Equals(ruleDomain, StringComparison.OrdinalIgnoreCase) ||
        domain.EndsWith("." + ruleDomain, StringComparison.OrdinalIgnoreCase);
}
