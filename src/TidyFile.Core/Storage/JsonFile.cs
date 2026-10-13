using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidyFile.Core.Storage;

/// <summary>Small helpers to read and write the JSON config files.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep '&', '<' and non-ASCII readable in files people edit by hand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads <paramref name="path"/>. If it does not exist, writes and returns the default.
    /// If it is corrupt, keeps a ".bad" copy, returns the default and sets <paramref name="warning"/>.
    /// </summary>
    public static T LoadOrCreate<T>(string path, Func<T> createDefault, out string? warning)
        where T : class
    {
        warning = null;

        if (!File.Exists(path))
        {
            var value = createDefault();
            Save(path, value);
            return value;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<T>(json, Options);
            if (loaded is not null)
            {
                return loaded;
            }

            warning = $"{Path.GetFileName(path)} was empty; using defaults.";
        }
        catch (JsonException ex)
        {
            var backup = path + ".bad";
            File.Copy(path, backup, overwrite: true);
            warning = $"{Path.GetFileName(path)} has a JSON error (line {ex.LineNumber + 1}); " +
                      $"using defaults. Your file was kept as {Path.GetFileName(backup)}.";
        }

        return createDefault();
    }

    public static void Save<T>(string path, T value)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Write to a temp file first so a crash never leaves a half-written config.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
