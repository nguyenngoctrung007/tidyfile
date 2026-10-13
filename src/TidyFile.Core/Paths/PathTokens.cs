using System.Text.RegularExpressions;

namespace TidyFile.Core.Paths;

/// <summary>
/// Expands folder tokens such as <c>{Downloads}</c> or <c>{Documents}</c> used in settings and rules,
/// plus normal environment variables such as <c>%USERPROFILE%</c>.
/// </summary>
public static class PathTokens
{
    private static readonly Regex TokenRegex = new(@"\{(\w+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly Dictionary<string, Func<string>> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = () => Home,
        ["Desktop"] = () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        ["Documents"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["Pictures"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["Music"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ["Videos"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        // .NET has no SpecialFolder for Downloads; the default location is good enough for the MVP.
        ["Downloads"] = () => Path.Combine(Home, "Downloads"),
    };

    /// <summary>Token names in the order used when shortening paths for display.</summary>
    private static readonly string[] DisplayOrder = { "Downloads", "Desktop", "Documents", "Pictures", "Music", "Videos", "Home" };

    public static string Expand(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var result = TokenRegex.Replace(path, m =>
            Tokens.TryGetValue(m.Groups[1].Value, out var resolve) ? resolve() : m.Value);

        return Environment.ExpandEnvironmentVariables(result);
    }

    /// <summary>
    /// Turns a full path back into a short, friendly form for the UI,
    /// e.g. <c>C:\Users\trung\Documents\Finance</c> becomes <c>Documents\Finance</c>.
    /// </summary>
    public static string Shorten(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return fullPath;
        }

        foreach (var token in DisplayOrder)
        {
            var root = Tokens[token]();
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }

            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return token + fullPath[root.Length..];
            }
        }

        return fullPath;
    }
}
