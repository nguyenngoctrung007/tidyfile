using TidyFile.Core.Storage;

namespace TidyFile.Core.Rules;

public enum AddRuleResult
{
    Added,
    AlreadyExists,
    FileUnreadable,
}

/// <summary>
/// Loads rules.json and reloads it automatically when the file changes on disk,
/// so edits take effect without restarting the app.
/// </summary>
public sealed class RulesProvider
{
    private readonly string _path;
    private readonly object _gate = new();
    private RuleEngine _engine = new(Array.Empty<Rule>());
    private DateTime _loadedWriteTimeUtc = DateTime.MinValue;

    public RulesProvider(string path)
    {
        _path = path;
    }

    /// <summary>Raised (on the calling thread) when loading produced warnings or rule problems.</summary>
    public event Action<IReadOnlyList<string>>? ProblemsFound;

    public RuleEngine GetEngine()
    {
        lock (_gate)
        {
            var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (writeTime == _loadedWriteTimeUtc && writeTime != DateTime.MinValue)
            {
                return _engine;
            }

            RulesDocument document;
            string? warning;
            try
            {
                document = JsonFile.LoadOrCreate(_path, DefaultRules.Create, out warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Probably an editor is saving the file right now. Keep the old rules and retry next time.
                return _engine;
            }

            _engine = new RuleEngine(document.Rules);
            _loadedWriteTimeUtc = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;

            var problems = new List<string>();
            if (warning is not null)
            {
                problems.Add(warning);
            }

            problems.AddRange(_engine.Problems);
            if (problems.Count > 0)
            {
                ProblemsFound?.Invoke(problems);
            }

            return _engine;
        }
    }

    /// <summary>
    /// Reads rules.json for editing (creates it with the defaults if missing).
    /// <paramref name="warning"/> is set when the file could not be read and the defaults were returned instead,
    /// so the caller can tell the user that saving will replace their file.
    /// </summary>
    public RulesDocument LoadDocument(out string? warning)
    {
        lock (_gate)
        {
            return JsonFile.LoadOrCreate(_path, DefaultRules.Create, out warning);
        }
    }

    /// <summary>
    /// Adds <paramref name="rule"/> at the top of rules.json (see <see cref="RuleFactory.AddToTop"/>).
    /// Refuses to touch a file it couldn't read, so a damaged rules.json is never replaced by defaults behind the user's back.
    /// </summary>
    public AddRuleResult AddToTop(Rule rule)
    {
        lock (_gate)
        {
            var document = JsonFile.LoadOrCreate(_path, DefaultRules.Create, out var warning);
            if (warning is not null)
            {
                return AddRuleResult.FileUnreadable;
            }

            if (!RuleFactory.AddToTop(document, rule))
            {
                return AddRuleResult.AlreadyExists;
            }

            Save(document);
            return AddRuleResult.Added;
        }
    }

    /// <summary>Writes rules.json; the next <see cref="GetEngine"/> call uses the new rules.</summary>
    public void Save(RulesDocument document)
    {
        lock (_gate)
        {
            JsonFile.Save(_path, document);
            _loadedWriteTimeUtc = DateTime.MinValue; // Force a reload even if the timestamp didn't move.
        }
    }
}
