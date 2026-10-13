using TidyFile.Core.Storage;

namespace TidyFile.Core.Rules;

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
}
