namespace TidyFile.Core.Files;

/// <summary>Waits until a new file has finished being written (size stable and not locked).</summary>
public static class FileReadiness
{
    /// <summary>
    /// Returns <c>true</c> once the file's size is stable between two checks and it can be opened exclusively.
    /// Returns <c>false</c> if the file disappears, stays empty for <paramref name="emptyFileGrace"/>,
    /// or is still busy when <paramref name="timeout"/> expires.
    /// </summary>
    public static async Task<bool> WaitUntilReadyAsync(
        string path,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        TimeSpan? emptyFileGrace = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(750);
        var emptyGrace = emptyFileGrace ?? TimeSpan.FromSeconds(10);
        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        long lastSize = -1;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return false;
                }

                size = info.Length;
            }
            catch (IOException)
            {
                return false;
            }

            if (size == 0)
            {
                // Empty placeholders (Firefox, "New Text Document.txt") are not interesting.
                if (DateTime.UtcNow - started >= emptyGrace)
                {
                    return false;
                }
            }
            else if (size == lastSize && CanOpenExclusively(path))
            {
                return true;
            }

            lastSize = size;
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
