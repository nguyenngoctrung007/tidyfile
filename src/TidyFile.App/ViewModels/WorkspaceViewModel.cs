using System;
using System.Collections.Generic;
using System.Linq;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;
using TidyFile.Core.Workspace;

namespace TidyFile.App.ViewModels;

/// <summary>The "Create workspace" window: base folder, preview, the two options and the last setup's undo.</summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private const string KeptSuffix = "  (already exists, kept)";

    private readonly AppSettings _settings;
    private readonly RulesDocument? _rules;
    private readonly WorkspaceRecord? _lastSetup;
    private readonly int _year;
    private string _baseFolder = "";
    private bool _watchInbox = true;
    private bool _suggestIntoWorkspace = true;

    /// <param name="rules">Current rules, or null when rules.json couldn't be read.</param>
    /// <param name="lastSetup">The saved record of the previous setup, if any (enables Undo).</param>
    /// <param name="baseFolder">Initial base folder; defaults to <see cref="WorkspacePlanner.DefaultBaseFolder"/>.</param>
    public WorkspaceViewModel(AppSettings settings, RulesDocument? rules, WorkspaceRecord? lastSetup, int year, string? baseFolder = null)
    {
        _settings = settings;
        _rules = rules;
        _lastSetup = lastSetup;
        _year = year;
        _baseFolder = baseFolder ?? WorkspacePlanner.DefaultBaseFolder();
        Replan();
    }

    public string BaseFolder
    {
        get => _baseFolder;
        set
        {
            if (SetField(ref _baseFolder, value ?? ""))
            {
                Replan();
            }
        }
    }

    public string? BaseFolderError { get; private set; }

    /// <summary>What Create would do, or null while <see cref="BaseFolderError"/> is set.</summary>
    public WorkspacePlan? Plan { get; private set; }

    public IReadOnlyList<string> PreviewLines { get; private set; } = Array.Empty<string>();

    public bool WatchInbox
    {
        get => _watchInbox;
        set => SetField(ref _watchInbox, value);
    }

    public bool SuggestIntoWorkspace
    {
        get => _suggestIntoWorkspace;
        set => SetField(ref _suggestIntoWorkspace, value);
    }

    /// <summary>What "Suggest into the workspace" will change, shown under its checkbox.</summary>
    public string SuggestDescription { get; private set; } = "";

    public bool CanCreate => BaseFolderError is null;

    public bool HasUndo => _lastSetup is not null;

    public string? UndoDescription =>
        _lastSetup is null ? null : $"Undo setup of {_lastSetup.BaseFolder} ({_lastSetup.CreatedAt.LocalDateTime:d MMM yyyy})";

    public WorkspaceOptions Options => new(WatchInbox, SuggestIntoWorkspace);

    private void Replan()
    {
        BaseFolderError = WorkspacePlanner.ValidateBaseFolder(_baseFolder);
        Plan = BaseFolderError is null
            ? WorkspacePlanner.Plan(WorkspaceTemplate.Developer, _baseFolder, _settings, _rules, _year)
            : null;

        if (Plan is null)
        {
            PreviewLines = Array.Empty<string>();
        }
        else
        {
            var lines = new List<string>();
            if (!Plan.BaseExists)
            {
                lines.Add(Plan.BaseFolder.TrimEnd('\\') + @"\"); // The base folder itself will be created too.
            }

            lines.AddRange(Plan.Folders.Select(f => Line(f.RelativePath + @"\", f.Exists)));
            lines.Add(Line(WorkspacePlanner.ReadmeFileName, Plan.ReadmeExists));
            PreviewLines = lines;
            SuggestDescription = Describe(Plan);
        }

        OnPropertyChanged(nameof(BaseFolderError));
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(PreviewLines));
        OnPropertyChanged(nameof(SuggestDescription));
        OnPropertyChanged(nameof(CanCreate));
    }

    private static string Line(string item, bool exists) => exists ? item + KeptSuffix : item;

    private static string Describe(WorkspacePlan plan)
    {
        if (!plan.RulesReadable)
        {
            return "rules.json couldn't be read; rules will be left unchanged.";
        }

        var installers = plan.InstallersRuleName is not null ? @"Installers → Resources\Tools" : "Installers rule not found, skipped";
        var ebooks = plan.EbooksNeeded ? @"add rule Ebooks → Resources\Books" : "Ebooks already covered";
        return installers + ", " + ebooks;
    }
}
