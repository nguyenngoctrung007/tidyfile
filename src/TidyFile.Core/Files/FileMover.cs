namespace TidyFile.Core.Files;

/// <summary>Low-level, safe file moves. Never overwrites: picks "name (1).ext" on a clash.</summary>
public static class FileMover
{
    /// <summary>Returns the path the file would get inside <paramref name="destinationFolder"/>.</summary>
    public static string PlanTarget(string sourcePath, string destinationFolder) =>
        GetUniquePath(Path.Combine(destinationFolder, Path.GetFileName(sourcePath)));

    /// <summary>Moves <paramref name="sourcePath"/> to exactly <paramref name="targetPath"/> (which must not exist).</summary>
    public static void MoveExact(string sourcePath, string targetPath)
    {
        var folder = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // overwrite: false => throws IOException instead of destroying an existing file.
        File.Move(sourcePath, targetPath, overwrite: false);
    }

    public static string GetUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
        {
            return desiredPath;
        }

        var folder = Path.GetDirectoryName(desiredPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name for '{desiredPath}'.");
    }
}
