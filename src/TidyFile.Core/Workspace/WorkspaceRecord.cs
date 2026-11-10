using System.Text.Json;
using System.Text.Json.Serialization;
using TidyFile.Core.Diagnostics;
using TidyFile.Core.Storage;

namespace TidyFile.Core.Workspace;

/// <summary>
/// What the last "Create workspace" actually created or changed, so it can be undone (workspace-setup.json).
/// Only things TidyFile did itself are listed; anything that already existed is never recorded.
/// </summary>
public sealed class WorkspaceRecord
{
    public string BaseFolder { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Full paths in creation order (parents first; the base folder first if it was created).</summary>
    public List<string> CreatedFolders { get; set; } = new();

    public string? ReadmePath { get; set; }

    /// <summary>Hex SHA-256 of the README's bytes as written; Undo deletes it only if they still match.</summary>
    public string? ReadmeSha256 { get; set; }

    /// <summary>The exact entry added to the watched folders.</summary>
    public string? AddedWatchFolder { get; set; }

    public string? RedirectedRuleName { get; set; }

    public string? RedirectedFrom { get; set; }

    public string? RedirectedTo { get; set; }

    public string? AddedRuleName { get; set; }

    public string? AddedRuleDestination { get; set; }

    /// <summary>True when nothing was created or changed (e.g. Create on a complete workspace).</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        CreatedFolders.Count == 0 && ReadmePath is null && AddedWatchFolder is null &&
        RedirectedRuleName is null && AddedRuleName is null;

    /// <summary>The saved record, or null if there is none or it can't be read.</summary>
    public static WorkspaceRecord? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WorkspaceRecord>(File.ReadAllText(path), JsonFile.Options) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read '{path}', so the last workspace setup can't be undone: {ex.Message}");
            return null;
        }
    }

    public void Save(string path) => JsonFile.Save(path, this);

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
