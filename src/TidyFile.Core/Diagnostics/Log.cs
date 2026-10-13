namespace TidyFile.Core.Diagnostics;

/// <summary>Tiny file logger. Logging must never crash the app, so all errors are swallowed.</summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    /// <summary>Set once at startup. When null, logging is a no-op.</summary>
    public static string? FilePath { get; set; }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        var path = FilePath;
        if (path is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                // Keep the log small: start over (keeping one old copy) when it gets too big.
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ignore: logging is best effort.
        }
    }
}
