using TidyFile.Core.Rules;

namespace TidyFile.Core.Tests;

public class RuleEngineTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "tidyfile-rules");
    private static readonly string Downloads = Path.Combine(Root, "Downloads");
    private static readonly string Finance = Path.Combine(Root, "Finance");
    private static readonly string Docs = Path.Combine(Root, "Docs");
    private static readonly string Pictures = Path.Combine(Root, "Pictures");

    private static RuleEngine CreateEngine() => new(new[]
    {
        new Rule { Name = "Invoices", Extensions = { "pdf" }, NamePattern = "invoice|receipt", Destination = Finance },
        new Rule { Name = "Documents", Extensions = { ".PDF", "docx" }, Destination = Docs },
        new Rule { Name = "Images", Extensions = { "png" }, Destination = Pictures },
    });

    [Fact]
    public void Specific_rule_ranks_above_generic_rule()
    {
        var suggestions = CreateEngine().Suggest(Path.Combine(Downloads, "Invoice-2026-10.pdf"));

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(Finance, suggestions[0].Destination);
        Assert.Equal("Invoices", suggestions[0].RuleName);
        Assert.Equal(Docs, suggestions[1].Destination);
        Assert.True(suggestions[0].Confidence > suggestions[1].Confidence);
    }

    [Fact]
    public void Extensions_are_case_insensitive_and_dot_optional()
    {
        var suggestions = CreateEngine().Suggest(Path.Combine(Downloads, "report.PdF"));

        var only = Assert.Single(suggestions);
        Assert.Equal(Docs, only.Destination);
    }

    [Fact]
    public void Unknown_extension_gives_no_suggestions()
    {
        Assert.Empty(CreateEngine().Suggest(Path.Combine(Downloads, "data.xyz")));
    }

    [Fact]
    public void Folder_the_file_is_already_in_is_not_suggested()
    {
        var suggestions = CreateEngine().Suggest(Path.Combine(Docs, "report.pdf"));

        Assert.DoesNotContain(suggestions, s => s.Destination == Docs);
    }

    [Fact]
    public void Same_destination_is_only_suggested_once()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "A", Extensions = { "pdf" }, Destination = Docs },
            new Rule { Name = "B", Extensions = { "pdf" }, Destination = Docs + Path.DirectorySeparatorChar },
        });

        var only = Assert.Single(engine.Suggest(Path.Combine(Downloads, "x.pdf")));
        Assert.Equal("A", only.RuleName);
    }

    [Fact]
    public void Max_limits_the_number_of_suggestions()
    {
        Assert.Single(CreateEngine().Suggest(Path.Combine(Downloads, "receipt.pdf"), max: 1));
    }

    [Fact]
    public void Broken_and_disabled_rules_are_skipped_and_reported()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Bad regex", NamePattern = "([", Destination = Docs },
            new Rule { Name = "No destination", Extensions = { "pdf" } },
            new Rule { Name = "Matches nothing", Destination = Docs },
            new Rule { Name = "Disabled", Enabled = false, Extensions = { "pdf" }, Destination = Docs },
        });

        Assert.Equal(0, engine.RuleCount);
        Assert.Equal(3, engine.Problems.Count); // Disabled rules are not a problem.
        Assert.Empty(engine.Suggest(Path.Combine(Downloads, "x.pdf")));
    }

    [Fact]
    public void Name_only_rule_matches_any_extension()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Project X", NamePattern = @"^projx[-_]", Destination = Docs },
        });

        Assert.Single(engine.Suggest(Path.Combine(Downloads, "projx-spec.docx")));
        Assert.Single(engine.Suggest(Path.Combine(Downloads, "PROJX_logo.png")));
        Assert.Empty(engine.Suggest(Path.Combine(Downloads, "other.docx")));
    }

    [Fact]
    public void Default_rules_are_all_valid()
    {
        var engine = new RuleEngine(DefaultRules.Create().Rules);

        Assert.Empty(engine.Problems);
        Assert.Equal(DefaultRules.Create().Rules.Count, engine.RuleCount);
    }
}
