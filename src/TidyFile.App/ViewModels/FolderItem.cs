using System.IO;
using TidyFile.Core.Paths;

namespace TidyFile.App.ViewModels;

/// <summary>One watched folder. <see cref="Value"/> is what gets saved (may contain tokens like {Downloads}).</summary>
public sealed class FolderItem
{
    public FolderItem(string value)
    {
        Value = value;
        FullPath = PathTokens.Expand(value);
        Exists = Directory.Exists(FullPath);
    }

    public string Value { get; }

    public string FullPath { get; }

    public bool Exists { get; }

    public string Display => PathTokens.Shorten(FullPath);

    public string Status => Exists ? FullPath : $"Not found: {FullPath}";
}
