using TidyFile.Core.Files;
using TidyFile.Core.Paths;
using TidyFile.Core.Rules;

namespace TidyFile.Core.Tests;

public class RuleFactoryTests
{
    private static readonly string Downloads = Path.Combine(Path.GetTempPath(), "tidyfile-factory", "Downloads");
    private static readonly string Finance = Path.Combine(Path.GetTempPath(), "tidyfile-factory", "Finance");
    private static readonly DownloadSource FromBank = new("cdn.bank.com", "bank.com");

    [Theory]
    [InlineData("statement.pdf", true, "PDF files from bank.com")]
    [InlineData("statement.pdf", false, "PDF files")]
    [InlineData("README", true, "files from bank.com")]
    [InlineData("README", false, null)]
    public void Describes_what_the_rule_would_match(string fileName, bool hasSource, string? expected)
    {
        Assert.Equal(expected, RuleFactory.Describe(Path.Combine(Downloads, fileName), hasSource ? FromBank : null));
    }

    [Fact]
    public void Builds_a_rule_from_extension_and_website()
    {
        var rule = RuleFactory.FromExample(Path.Combine(Downloads, "Statement-Oct.PDF"), Finance, FromBank)!;

        Assert.Equal("PDF files from bank.com", rule.Name);
        Assert.Equal(new[] { "pdf" }, rule.Extensions);
        Assert.Equal(new[] { "bank.com" }, rule.Sources);
        Assert.Null(rule.NamePattern);
        Assert.Equal(Finance, PathTokens.Expand(rule.Destination));
        Assert.Null(RuleEngine.Validate(rule));
    }

    [Fact]
    public void Website_only_rule_gets_a_capitalised_name()
    {
        var rule = RuleFactory.FromExample(Path.Combine(Downloads, "README"), Finance, FromBank)!;

        Assert.Equal("Files from bank.com", rule.Name);
        Assert.Empty(rule.Extensions);
        Assert.Null(RuleEngine.Validate(rule));
    }

    [Fact]
    public void Nothing_to_build_from_gives_null()
    {
        Assert.Null(RuleFactory.FromExample(Path.Combine(Downloads, "README"), Finance, null));
    }

    [Fact]
    public void Destination_under_a_known_folder_is_stored_as_a_token()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var rule = RuleFactory.FromExample(Path.Combine(Downloads, "a.pdf"), Path.Combine(documents, "Finance"), null)!;

        Assert.Equal(@"{Documents}\Finance", rule.Destination);
    }

    [Fact]
    public void New_rule_goes_to_the_top_and_wins_ties()
    {
        var doc = new RulesDocument { Rules = { new Rule { Name = "Documents", Extensions = { "pdf" }, Destination = Downloads + "-docs" } } };
        var rule = RuleFactory.FromExample(Path.Combine(Downloads, "a.pdf"), Finance, null)!;

        Assert.True(RuleFactory.AddToTop(doc, rule));

        Assert.Same(rule, doc.Rules[0]);
        var best = new RuleEngine(doc.Rules).Suggest(Path.Combine(Downloads, "b.pdf"))[0];
        Assert.Equal(Finance, best.Destination);
    }

    [Fact]
    public void Identical_rule_is_not_added_twice()
    {
        var doc = new RulesDocument();
        RuleFactory.AddToTop(doc, RuleFactory.FromExample(Path.Combine(Downloads, "a.pdf"), Finance, FromBank)!);

        var again = RuleFactory.FromExample(Path.Combine(Downloads, "b.PDF"), Finance + Path.DirectorySeparatorChar, FromBank)!;

        Assert.False(RuleFactory.AddToTop(doc, again));
        Assert.Single(doc.Rules);
    }

    [Fact]
    public void Same_conditions_to_another_folder_is_added_with_a_unique_name()
    {
        var doc = new RulesDocument();
        RuleFactory.AddToTop(doc, RuleFactory.FromExample(Path.Combine(Downloads, "a.pdf"), Finance, null)!);

        var other = RuleFactory.FromExample(Path.Combine(Downloads, "b.pdf"), Downloads + "-docs", null)!;

        Assert.True(RuleFactory.AddToTop(doc, other));
        Assert.Equal(new[] { "PDF files (2)", "PDF files" }, doc.Rules.Select(r => r.Name));
        Assert.Empty(RuleEngine.FindDuplicateNames(doc.Rules));
    }

    [Fact]
    public void A_disabled_identical_rule_does_not_block_a_new_one()
    {
        var doc = new RulesDocument();
        var old = RuleFactory.FromExample(Path.Combine(Downloads, "a.pdf"), Finance, null)!;
        old.Enabled = false;
        doc.Rules.Add(old);

        Assert.True(RuleFactory.AddToTop(doc, RuleFactory.FromExample(Path.Combine(Downloads, "b.pdf"), Finance, null)!));
    }

    // ----- RulesProvider.AddToTop -----

    [Fact]
    public void Provider_adds_to_rules_json_and_uses_it_right_away()
    {
        using var temp = new TempFolder();
        var provider = new RulesProvider(temp.Combine("rules.json"));
        provider.GetEngine(); // Creates the defaults.
        var rule = RuleFactory.FromExample(temp.Combine("x.weird"), temp.Path, null)!;

        Assert.Equal(AddRuleResult.Added, provider.AddToTop(rule));
        Assert.Equal(AddRuleResult.AlreadyExists, provider.AddToTop(RuleFactory.FromExample(temp.Combine("y.weird"), temp.Path, null)!));

        Assert.Equal("WEIRD files", provider.LoadDocument(out _).Rules[0].Name);
        Assert.Single(provider.GetEngine().Suggest(temp.Combine("Downloads", "z.weird")));
    }

    [Fact]
    public void Provider_leaves_a_damaged_rules_json_alone()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("rules.json", "{ broken");
        var provider = new RulesProvider(path);

        var result = provider.AddToTop(RuleFactory.FromExample(temp.Combine("a.pdf"), temp.Path, null)!);

        Assert.Equal(AddRuleResult.FileUnreadable, result);
        Assert.Equal("{ broken", File.ReadAllText(path));
    }
}
