namespace TidyFile.Core.Files;

/// <summary>Decides which new files are noise (partial downloads, temp files, shortcuts...).</summary>
public static class FileFilter
{
    private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", // Chrome / Edge
        ".part",       // Firefox
        ".partial",    // old Edge / IE
        ".download",   // Safari
        ".opdownload", // Opera
        ".tmp",
        ".temp",
        ".!ut",        // uTorrent
        ".aria2",
    };

    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", // Desktop shortcuts created by installers
        ".url",
    };

    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "thumbs.db",
        ".ds_store",
    };

    public static bool ShouldIgnore(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        // "~$Report.docx" = Office lock file, ".foo" = hidden/system style file.
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.'))
        {
            return true;
        }

        if (IgnoredNames.Contains(name))
        {
            return true;
        }

        var extension = Path.GetExtension(name);
        return TempExtensions.Contains(extension) || IgnoredExtensions.Contains(extension);
    }
}
