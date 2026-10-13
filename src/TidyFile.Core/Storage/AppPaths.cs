namespace TidyFile.Core.Storage;

/// <summary>Where TidyFile keeps its own files. Default: %LOCALAPPDATA%\TidyFile.</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidyFile");
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string RulesFile => Path.Combine(Root, "rules.json");

    public string HistoryFile => Path.Combine(Root, "history.jsonl");

    public string LogFile => Path.Combine(Root, "tidyfile.log");
}
