using TidyFile.Core.Files;
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

    // ----- Auto-move -----

    [Fact]
    public void Auto_move_when_the_best_rule_is_trusted()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Invoices", Extensions = { "pdf" }, NamePattern = "invoice", Destination = Finance, AutoMove = true },
            new Rule { Name = "Documents", Extensions = { "pdf" }, Destination = Docs },
        });

        var choice = RuleEngine.AutoMoveChoice(engine.Suggest(Path.Combine(Downloads, "invoice-1.pdf")));

        Assert.Equal(Finance, choice?.Destination);
        Assert.Equal("Invoices", choice?.RuleName);
    }

    [Fact]
    public void No_auto_move_when_a_better_rule_is_not_trusted()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Documents", Extensions = { "pdf" }, Destination = Docs, AutoMove = true },
            new Rule { Name = "Invoices", Extensions = { "pdf" }, NamePattern = "invoice", Destination = Finance },
        });

        Assert.Null(RuleEngine.AutoMoveChoice(engine.Suggest(Path.Combine(Downloads, "invoice-1.pdf"))));
        Assert.NotNull(RuleEngine.AutoMoveChoice(engine.Suggest(Path.Combine(Downloads, "notes.pdf"))));
    }

    [Fact]
    public void Learned_suggestions_never_auto_move()
    {
        var learned = new Suggestion(Docs, "You moved…", 0.85, RuleName: null, AutoMove: true); // Even if mislabelled.

        Assert.Null(RuleEngine.AutoMoveChoice(new[] { learned }));
        Assert.Null(RuleEngine.AutoMoveChoice(Array.Empty<Suggestion>()));
    }

    [Fact]
    public void Disabled_trusted_rule_does_nothing()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Docs", Enabled = false, Extensions = { "pdf" }, Destination = Docs, AutoMove = true },
        });

        Assert.Null(RuleEngine.AutoMoveChoice(engine.Suggest(Path.Combine(Downloads, "a.pdf"))));
    }

    [Fact]
    public void Auto_move_is_off_for_rules_saved_before_it_existed()
    {
        var rule = System.Text.Json.JsonSerializer.Deserialize<Rule>(
            "{ \"name\": \"Docs\", \"extensions\": [\"pdf\"], \"destination\": \"C:\\\\Docs\" }",
            TidyFile.Core.Storage.JsonFile.Options)!;

        Assert.False(rule.AutoMove);
    }

    // ----- Download source -----

    private static readonly DownloadSource FromGitHub = new("objects.githubusercontent.com", "github.com");

    [Fact]
    public void Source_rule_only_matches_files_from_that_website()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "GitHub zips", Extensions = { "zip" }, Sources = { "github.com" }, Destination = Docs },
        });
        var file = Path.Combine(Downloads, "release.zip");

        Assert.Single(engine.Suggest(file, source: FromGitHub));
        Assert.Empty(engine.Suggest(file)); // Unknown source.
        Assert.Empty(engine.Suggest(file, source: new DownloadSource("example.org", null)));
    }

    [Fact]
    public void Source_matches_either_domain_and_subdomains()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "CDN", Sources = { "githubusercontent.com" }, Destination = Docs },
        });

        Assert.Single(engine.Suggest(Path.Combine(Downloads, "a.bin"), source: FromGitHub));
    }

    [Fact]
    public void Rule_sources_accept_full_urls()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "GitHub", Sources = { "https://www.github.com/" }, Destination = Docs },
        });

        Assert.Single(engine.Suggest(Path.Combine(Downloads, "a.bin"), source: FromGitHub));
    }

    [Fact]
    public void Source_makes_a_rule_more_specific()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Zips", Extensions = { "zip" }, Destination = Docs },
            new Rule { Name = "GitHub zips", Extensions = { "zip" }, Sources = { "github.com" }, Destination = Finance },
            new Rule { Name = "Releases", Extensions = { "zip" }, NamePattern = "release", Destination = Pictures },
        });

        var suggestions = engine.Suggest(Path.Combine(Downloads, "release.zip"), source: FromGitHub);

        Assert.Equal(new[] { "Releases", "GitHub zips", "Zips" }, suggestions.Select(s => s.RuleName));
        Assert.Equal(0.9, suggestions[0].Confidence, 3);
        Assert.Equal(0.65, suggestions[1].Confidence, 3);
        Assert.Equal(0.6, suggestions[2].Confidence, 3);
    }

    [Fact]
    public void Source_only_and_name_plus_source_scores()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "From GitHub", Sources = { "github.com" }, Destination = Docs },
            new Rule { Name = "Named", NamePattern = "^release", Sources = { "github.com" }, Destination = Finance },
            new Rule { Name = "All", Extensions = { "zip" }, NamePattern = "^release", Sources = { "github.com" }, Destination = Pictures },
        });

        var matches = engine.Explain("release.zip", FromGitHub);

        Assert.Equal(0.95, matches.Single(m => m.RuleName == "All").Confidence, 3);
        Assert.Equal(0.85, matches.Single(m => m.RuleName == "Named").Confidence, 3);
        Assert.Equal(0.6, matches.Single(m => m.RuleName == "From GitHub").Confidence, 3);
    }

    [Fact]
    public void Validate_accepts_a_website_as_the_only_condition_and_rejects_non_websites()
    {
        Assert.Null(RuleEngine.Validate(new Rule { Name = "GH", Sources = { "github.com" }, Destination = Docs }));
        Assert.Contains("not a website", RuleEngine.Validate(new Rule { Name = "Bad", Sources = { "about:blank" }, Destination = Docs }));
        Assert.Contains("website", RuleEngine.Validate(new Rule { Name = "Empty", Sources = { " " }, Destination = Docs }));
    }

    [Fact]
    public void Validate_explains_what_is_wrong()
    {
        Assert.Null(RuleEngine.Validate(new Rule { Name = "Ok", Extensions = { "pdf" }, Destination = Docs }));
        Assert.Contains("destination", RuleEngine.Validate(new Rule { Extensions = { "pdf" } }));
        Assert.Contains("pattern", RuleEngine.Validate(new Rule { NamePattern = "([", Destination = Docs }));
        Assert.Contains("extension", RuleEngine.Validate(new Rule { Extensions = { " " }, Destination = Docs }));
        Assert.Contains("full path", RuleEngine.Validate(new Rule { Extensions = { "pdf" }, Destination = "Finance" }));
        Assert.Contains("name", RuleEngine.Validate(new Rule { Name = "  ", Extensions = { "pdf" }, Destination = Docs }));
    }

    [Fact]
    public void Rules_without_a_name_are_skipped()
    {
        var engine = new RuleEngine(new[] { new Rule { Extensions = { "pdf" }, Destination = Docs } });

        Assert.Equal(0, engine.RuleCount);
        Assert.Contains("name", Assert.Single(engine.Problems));
    }

    [Fact]
    public void FindDuplicateNames_ignores_case_spaces_and_empty_names()
    {
        var rules = new[]
        {
            new Rule { Name = "Docs" },
            new Rule { Name = " docs " },
            new Rule { Name = "Images" },
            new Rule { Name = "" },
            new Rule { Name = " " },
        };

        Assert.Equal(new[] { "Docs" }, RuleEngine.FindDuplicateNames(rules));
    }

    [Fact]
    public void ValidatePattern_accepts_empty_and_valid_patterns()
    {
        Assert.Null(RuleEngine.ValidatePattern(null));
        Assert.Null(RuleEngine.ValidatePattern(""));
        Assert.Null(RuleEngine.ValidatePattern("^invoice"));
        Assert.NotNull(RuleEngine.ValidatePattern("(unclosed"));
    }

    [Fact]
    public void Explain_lists_every_matching_rule_best_first()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule { Name = "Docs", Extensions = { "pdf" }, Destination = Docs },
            new Rule { Name = "Also docs", Extensions = { "pdf" }, Destination = Docs },
            new Rule { Name = "Invoices", Extensions = { "pdf" }, NamePattern = "invoice", Destination = Finance },
        });

        var matches = engine.Explain("invoice-01.pdf");

        Assert.Equal(new[] { "Invoices", "Docs", "Also docs" }, matches.Select(m => m.RuleName));
        Assert.Empty(engine.Explain("photo.png"));
    }
}
