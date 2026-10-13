using System;
using System.Linq;
using TidyFile.Core.Rules;

namespace TidyFile.App.ViewModels;

/// <summary>One editable rule in the Settings window.</summary>
public sealed class RuleItem : ObservableObject
{
    private string _name = "";
    private bool _enabled = true;
    private string _extensionsText = "";
    private string _namePattern = "";
    private string _sourcesText = "";
    private string _destination = "";

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value))
            {
                OnPropertyChanged(nameof(Problem));
                OnPropertyChanged(nameof(HasProblem));
            }
        }
    }

    /// <summary>Comma or space separated, e.g. "pdf, docx".</summary>
    public string ExtensionsText
    {
        get => _extensionsText;
        set
        {
            if (SetField(ref _extensionsText, value))
            {
                RaiseDerived();
            }
        }
    }

    public string NamePattern
    {
        get => _namePattern;
        set
        {
            if (SetField(ref _namePattern, value))
            {
                OnPropertyChanged(nameof(PatternError));
                RaiseDerived();
            }
        }
    }

    /// <summary>Comma or space separated websites, e.g. "github.com, nuget.org".</summary>
    public string SourcesText
    {
        get => _sourcesText;
        set
        {
            if (SetField(ref _sourcesText, value))
            {
                RaiseDerived();
            }
        }
    }

    public string Destination
    {
        get => _destination;
        set
        {
            if (SetField(ref _destination, value))
            {
                RaiseDerived();
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed rule)" : Name;

    /// <summary>Short description shown under the name in the list.</summary>
    public string Summary
    {
        get
        {
            var what = ParseExtensions().Length > 0 ? string.Join(", ", ParseExtensions()) : "any type";
            if (!string.IsNullOrWhiteSpace(NamePattern))
            {
                what += $" · name ~ {NamePattern}";
            }

            var sources = ParseSources();
            if (sources.Length > 0)
            {
                what += " · from " + string.Join(", ", sources);
            }

            return $"{what}  →  {(string.IsNullOrWhiteSpace(Destination) ? "?" : Destination)}";
        }
    }

    public string? PatternError => RuleEngine.ValidatePattern(NamePattern);

    /// <summary>Why this rule can't be used (only for enabled rules), or null.</summary>
    public string? Problem =>
        Enabled && RuleEngine.Validate(ToRule()) is { } problem ? "This rule is skipped because " + problem : null;

    public bool HasProblem => Problem is not null;

    public static RuleItem FromRule(Rule rule) => new()
    {
        _name = rule.Name,
        _enabled = rule.Enabled,
        _extensionsText = string.Join(", ", rule.Extensions),
        _namePattern = rule.NamePattern ?? "",
        _sourcesText = string.Join(", ", rule.Sources),
        _destination = rule.Destination,
    };

    public Rule ToRule() => new()
    {
        Name = Name.Trim(),
        Enabled = Enabled,
        Extensions = ParseExtensions().ToList(),
        NamePattern = string.IsNullOrWhiteSpace(NamePattern) ? null : NamePattern.Trim(),
        Sources = ParseSources().ToList(),
        Destination = Destination.Trim(),
    };

    // Kept as typed (only trimmed): RuleEngine.Validate reports entries that aren't websites.
    private string[] ParseSources() =>
        SourcesText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] ParseExtensions() =>
        ExtensionsText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('*', '.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToArray();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
    }
}
