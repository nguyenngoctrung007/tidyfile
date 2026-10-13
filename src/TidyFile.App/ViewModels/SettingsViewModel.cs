using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using TidyFile.Core.Files;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.App.ViewModels;

/// <summary>
/// Everything the Settings window edits. Works on copies: nothing is applied until the window is saved.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private bool _startWithWindows;
    private int _popupSeconds;
    private int _maxSuggestions;
    private FolderItem? _selectedFolder;
    private RuleItem? _selectedRule;
    private string _testFileName = "";
    private string _testSource = "";
    private string _testResult = "";

    /// <param name="rulesWarning">Set when rules.json could not be read and <paramref name="rules"/> are the defaults.</param>
    public SettingsViewModel(AppSettings settings, RulesDocument rules, bool startWithWindows, string? rulesWarning = null)
    {
        RulesWarning = rulesWarning is null
            ? null
            : rulesWarning + " Saving will replace rules.json with the rules shown here.";
        _startWithWindows = startWithWindows;
        _popupSeconds = Math.Clamp(settings.PopupSeconds, MinPopupSeconds, MaxPopupSeconds);
        _maxSuggestions = Math.Clamp(settings.MaxSuggestions, 1, 5);

        Folders = new ObservableCollection<FolderItem>(settings.WatchFolders.Select(f => new FolderItem(f)));
        Rules = new ObservableCollection<RuleItem>(rules.Rules.Select(RuleItem.FromRule));

        foreach (var rule in Rules)
        {
            rule.PropertyChanged += OnRuleChanged;
        }

        Rules.CollectionChanged += OnRulesCollectionChanged;
        Folders.CollectionChanged += (_, _) => IsDirty = true;
        _selectedRule = Rules.FirstOrDefault();
    }

    public const int MinPopupSeconds = 5;
    public const int MaxPopupSeconds = 120;

    /// <summary>True once anything that Save would write has been edited.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Shown as a banner in the Rules tab when rules.json could not be read.</summary>
    public string? RulesWarning { get; }

    // ----- General -----

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetField(ref _startWithWindows, value))
            {
                IsDirty = true;
            }
        }
    }

    public int PopupSeconds
    {
        get => _popupSeconds;
        set
        {
            if (SetField(ref _popupSeconds, Math.Clamp(value, MinPopupSeconds, MaxPopupSeconds)))
            {
                IsDirty = true;
            }
        }
    }

    public int MaxSuggestions
    {
        get => _maxSuggestions;
        set
        {
            if (SetField(ref _maxSuggestions, Math.Clamp(value, 1, 5)))
            {
                IsDirty = true;
                UpdateTestResult();
            }
        }
    }

    public IReadOnlyList<int> MaxSuggestionChoices { get; } = new[] { 1, 2, 3, 4, 5 };

    // ----- Folders -----

    public ObservableCollection<FolderItem> Folders { get; }

    public FolderItem? SelectedFolder
    {
        get => _selectedFolder;
        set => SetField(ref _selectedFolder, value);
    }

    /// <summary>Adds a folder (stored with a token like {Documents} when possible). Returns false if already listed.</summary>
    public bool AddFolder(string fullPath)
    {
        var item = new FolderItem(PathTokens.Tokenize(fullPath));
        if (Folders.Any(f => string.Equals(f.FullPath.TrimEnd('\\'), item.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Folders.Add(item);
        SelectedFolder = item;
        return true;
    }

    public void RemoveSelectedFolder()
    {
        if (SelectedFolder is not null)
        {
            Folders.Remove(SelectedFolder);
            SelectedFolder = Folders.FirstOrDefault();
        }
    }

    // ----- Rules -----

    public ObservableCollection<RuleItem> Rules { get; }

    public RuleItem? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetField(ref _selectedRule, value))
            {
                OnPropertyChanged(nameof(HasSelectedRule));
            }
        }
    }

    public bool HasSelectedRule => SelectedRule is not null;

    public void AddRule()
    {
        var rule = new RuleItem { Name = "New rule" };
        var index = SelectedRule is null ? Rules.Count : Rules.IndexOf(SelectedRule) + 1;
        Rules.Insert(index, rule);
        SelectedRule = rule;
    }

    public void DeleteSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = Rules.IndexOf(SelectedRule);
        Rules.Remove(SelectedRule);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
    }

    /// <summary>Rules higher in the list win ties, so order matters.</summary>
    public void MoveSelectedRule(int delta)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var from = Rules.IndexOf(SelectedRule);
        var to = from + delta;
        if (to < 0 || to >= Rules.Count)
        {
            return;
        }

        var keep = SelectedRule;
        Rules.Move(from, to);
        SelectedRule = keep;
    }

    // ----- Rule tester -----

    public string TestFileName
    {
        get => _testFileName;
        set
        {
            if (SetField(ref _testFileName, value))
            {
                UpdateTestResult();
            }
        }
    }

    /// <summary>Optional website for the tester, e.g. "github.com" (rules with websites only match when set).</summary>
    public string TestSource
    {
        get => _testSource;
        set
        {
            if (SetField(ref _testSource, value))
            {
                UpdateTestResult();
            }
        }
    }

    public string TestResult
    {
        get => _testResult;
        private set => SetField(ref _testResult, value);
    }

    private void UpdateTestResult()
    {
        var name = TestFileName.Trim();
        if (name.Length == 0)
        {
            TestResult = "";
            return;
        }

        var domain = DownloadSource.DomainOf(TestSource);
        var source = domain is null ? null : new DownloadSource(null, domain);

        var engine = new RuleEngine(Rules.Select(r => r.ToRule()));
        var matches = engine.Explain(name, source);
        if (matches.Count == 0)
        {
            TestResult = "No rule matches. The popup would offer Browse… and any folders learned from your past moves.";
            return;
        }

        // ✓ = shown in the popup. Use the same logic as the popup (it skips duplicate folders).
        var fakePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TidyFile rule test", System.IO.Path.GetFileName(name));
        var fromRules = engine.Suggest(fakePath, MaxSuggestions, source);
        var shown = fromRules.Select(s => s.RuleName).ToHashSet();

        var lines = matches
            .Take(8)
            .Select(m => $"{(shown.Contains(m.RuleName) ? "✓" : "·")} {m.RuleName}  →  {PathTokens.Shorten(m.Destination)}  ({m.Confidence:P0})");
        var header = RuleEngine.AutoMoveChoice(fromRules) is { } auto
            ? $"Moved without asking to {PathTokens.Shorten(auto.Destination)} (rule \"{auto.RuleName}\")."
            : "✓ = shown in the popup";
        TestResult = header + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
        UpdateTestResult();
    }

    private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;

        if (e.NewItems is not null)
        {
            foreach (RuleItem item in e.NewItems)
            {
                item.PropertyChanged += OnRuleChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (RuleItem item in e.OldItems)
            {
                item.PropertyChanged -= OnRuleChanged;
            }
        }

        UpdateTestResult();
    }

    // ----- Save -----

    /// <summary>Problems that must be fixed before saving.</summary>
    public IReadOnlyList<string> Validate() =>
        Rules
            .Where(r => r.Problem is not null)
            .Select(r => $"\"{r.DisplayName}\": {r.Problem}")
            .ToList();

    /// <summary>Names shared by several enabled rules. Allowed, but worth a warning before saving.</summary>
    public IReadOnlyList<string> DuplicateRuleNames() =>
        RuleEngine.FindDuplicateNames(Rules.Where(r => r.Enabled).Select(r => r.ToRule()));

    /// <summary>Copies the edited values into <paramref name="target"/> (the live settings object).</summary>
    public void ApplyTo(AppSettings target)
    {
        target.WatchFolders = Folders.Select(f => f.Value).ToList();
        target.PopupSeconds = PopupSeconds;
        target.MaxSuggestions = MaxSuggestions;
    }

    public RulesDocument ToRulesDocument() => new()
    {
        Rules = Rules.Select(r => r.ToRule()).ToList(),
    };
}
