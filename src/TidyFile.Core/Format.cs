using System.Globalization;

namespace TidyFile.Core;

public static class Format
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>1536 => "1.5 KB".</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(value < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
