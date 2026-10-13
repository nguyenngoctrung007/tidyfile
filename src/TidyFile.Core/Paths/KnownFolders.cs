using System.Runtime.InteropServices;

namespace TidyFile.Core.Paths;

/// <summary>Folders .NET has no <see cref="Environment.SpecialFolder"/> for.</summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads

    // Looked up once: asking the shell is cheap, but Expand runs for every rule on every new file.
    private static readonly Lazy<string> DownloadsPath = new(ResolveDownloads);

    /// <summary>
    /// The user's real Downloads folder, which they may have moved (e.g. to D:\Downloads).
    /// Falls back to %USERPROFILE%\Downloads if Windows can't tell us.
    /// </summary>
    public static string Downloads => DownloadsPath.Value;

    private static string ResolveDownloads()
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            var id = DownloadsId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pathPtr) != 0)
            {
                return fallback;
            }

            try
            {
                var path = Marshal.PtrToStringUni(pathPtr);
                return string.IsNullOrWhiteSpace(path) ? fallback : path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return fallback;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
