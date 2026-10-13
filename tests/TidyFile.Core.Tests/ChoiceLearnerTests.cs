using TidyFile.Core.Files;
using TidyFile.Core.History;
using TidyFile.Core.Learning;
using TidyFile.Core.Rules;

namespace TidyFile.Core.Tests;

public class ChoiceLearnerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly List<HistoryEntry> _history = new();
    private readonly string _downloads;
    private readonly string _finance;
    private readonly string _docs;
    private readonly string _work;

    public ChoiceLearnerTests()
    {
        _downloads = Folder("Downloads");
        _finance = Folder("Finance");
        _docs = Folder("Docs");
        _work = Folder("Work");
    }

    public void Dispose() => _temp.Dispose();

    private string Folder(string name)
    {
        var path = _temp.Combine(name);
        Directory.CreateDirectory(path);
        return path;
    }

    private HistoryEntry Moved(string fileName, string folder, string? source = null)
    {
        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Move,
            Path.Combine(_downloads, fileName), Path.Combine(folder, fileName), Source: source);
        _history.Add(entry);
        return entry;
    }

    private void Undid(HistoryEntry move) =>
        _history.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryAction.Undo, move.To, move.From, UndoOf: move.Id));

    private IReadOnlyList<Suggestion> Suggest(string fileName, string? source = null) =>
        ChoiceLearner.Suggest(_history, Path.Combine(_downloads, fileName),
            source is null ? null : new DownloadSource(null, source));

    [Fact]
    public void Nothing_is_learned_from_a_single_move()
    {
        Moved("a.pdf", _docs);

        Assert.Empty(Suggest("b.pdf"));
    }

    [Fact]
    public void Two_moves_of_the_same_type_are_enough()
    {
        Moved("a.pdf", _docs);
        Moved("b.pdf", _docs);

        var suggestion = Assert.Single(Suggest("c.pdf"));
        Assert.Equal(_docs, suggestion.Destination);
        Assert.Null(suggestion.RuleName);
        Assert.Equal("You moved 2 PDF files here", suggestion.Reason);
        Assert.Equal(0.65, suggestion.Confidence, 3); // All PDFs went there.
    }

    [Fact]
    public void Other_file_types_are_ignored()
    {
        Moved("a.pdf", _docs);
        Moved("b.pdf", _docs);

        Assert.Empty(Suggest("c.docx"));
        Assert.Empty(Suggest("no-extension"));
    }

    [Fact]
    public void Same_website_is_stronger_than_same_type()
    {
        Moved("statement-jan.pdf", _finance, "bank.com");
        Moved("statement-feb.pdf", _finance, "bank.com");
        Moved("paper1.pdf", _docs);
        Moved("paper2.pdf", _docs);
        Moved("paper3.pdf", _docs);

        var suggestions = Suggest("q1.pdf", "bank.com");

        Assert.Equal(_finance, suggestions[0].Destination);
        Assert.Equal("You moved 2 PDF files from bank.com here", suggestions[0].Reason);
        Assert.Equal(0.85, suggestions[0].Confidence, 3);
        Assert.Equal(_docs, suggestions[1].Destination);
    }

    [Fact]
    public void Similar_names_are_stronger_than_same_type()
    {
        Moved("invoice-acme-01.pdf", _finance);
        Moved("Invoice_Globex_02.pdf", _finance);
        Moved("paper1.pdf", _docs);
        Moved("paper2.pdf", _docs);
        Moved("paper3.pdf", _docs);

        var suggestions = Suggest("INVOICE 2026-10.pdf");

        Assert.Equal(_finance, suggestions[0].Destination);
        Assert.Equal("You moved 2 similar PDF files here", suggestions[0].Reason);
        Assert.Equal(_docs, suggestions[1].Destination); // Still offered, from the type alone.
    }

    [Fact]
    public void Website_alone_never_suggests_a_different_type()
    {
        Moved("a.zip", _work, "github.com");
        Moved("b.zip", _work, "github.com");

        Assert.Empty(Suggest("c.pdf", "github.com"));
    }

    [Fact]
    public void Undone_moves_count_against_a_folder()
    {
        Moved("a.pdf", _docs);
        Moved("b.pdf", _docs);
        Undid(Moved("c.pdf", _docs));

        Assert.Empty(Suggest("d.pdf")); // 2 kept - 1 undone = 1 < 2.

        Moved("e.pdf", _docs);
        Assert.Single(Suggest("f.pdf"));
    }

    [Fact]
    public void Inconsistent_choices_give_lower_confidence()
    {
        Moved("a.pdf", _docs);
        Moved("b.pdf", _docs);
        Moved("c.pdf", _finance);
        Moved("d.pdf", _finance);

        var suggestions = Suggest("e.pdf");

        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions, s => Assert.Equal(0.575, s.Confidence, 3)); // Half the PDFs each.
    }

    [Fact]
    public void Confidence_never_reaches_a_specific_rule()
    {
        for (var i = 0; i < 50; i++)
        {
            Moved($"invoice-{i}.pdf", _finance, "bank.com");
        }

        var suggestion = Suggest("invoice-new.pdf", "bank.com")[0];
        Assert.Equal(ChoiceLearner.MaxConfidence, suggestion.Confidence, 3);
        Assert.True(suggestion.Confidence < 0.9);
    }

    [Fact]
    public void Deleted_folders_and_the_current_folder_are_not_suggested()
    {
        var gone = Folder("Gone");
        Moved("a.pdf", gone);
        Moved("b.pdf", gone);
        Directory.Delete(gone);

        Moved("c.pdf", _downloads);
        Moved("d.pdf", _downloads);

        Assert.Empty(Suggest("e.pdf"));
    }

    [Fact]
    public void Words_skip_numbers_short_and_common_words()
    {
        Assert.Equal(
            new[] { "invoice", "acme" },
            ChoiceLearner.Words(@"C:\x\Invoice_ACME-2026 (copy) - final v2.pdf").OrderByDescending(w => w.Length).ToArray());
    }

    // ----- Merge -----

    private static Suggestion Rule(string folder, double confidence, string name) => new(folder, $"Rule: {name}", confidence, name);

    private static Suggestion Learned(string folder, double confidence) => new(folder, "You moved…", confidence, null);

    [Fact]
    public void Merge_ranks_by_confidence_and_respects_max()
    {
        var merged = ChoiceLearner.Merge(
            new[] { Rule(@"C:\Docs", 0.9, "Specific"), Rule(@"C:\All", 0.6, "Generic") },
            new[] { Learned(@"C:\Finance", 0.8) },
            max: 2);

        Assert.Equal(new[] { @"C:\Docs", @"C:\Finance" }, merged.Select(s => s.Destination));
    }

    [Fact]
    public void Merge_keeps_the_rule_for_a_shared_folder_with_the_higher_confidence()
    {
        var merged = ChoiceLearner.Merge(
            new[] { Rule(@"C:\Docs", 0.6, "Docs") },
            new[] { Learned(@"c:\docs\", 0.8) },
            max: 3);

        var only = Assert.Single(merged);
        Assert.Equal("Docs", only.RuleName);
        Assert.Equal(0.8, only.Confidence, 3);
    }

    [Fact]
    public void Merge_puts_rules_first_on_a_tie()
    {
        var merged = ChoiceLearner.Merge(
            new[] { Rule(@"C:\A", 0.65, "A") },
            new[] { Learned(@"C:\B", 0.65) },
            max: 3);

        Assert.Equal("A", merged[0].RuleName);
    }
}
