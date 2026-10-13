namespace TidyFile.Core.Storage;

/// <summary>Contents of settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Folders to watch. Supports tokens like {Downloads} and %ENV% variables.</summary>
    public List<string> WatchFolders { get; set; } = new() { "{Downloads}", "{Desktop}" };

    /// <summary>How long the popup stays before it closes by itself (it pauses while the mouse is over it).</summary>
    public int PopupSeconds { get; set; } = 20;

    /// <summary>How many destination suggestions to show.</summary>
    public int MaxSuggestions { get; set; } = 3;
}
