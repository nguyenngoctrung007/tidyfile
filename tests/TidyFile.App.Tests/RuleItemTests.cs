using TidyFile.App.ViewModels;
using TidyFile.Core.Rules;

namespace TidyFile.App.Tests;

public class RuleItemTests
{
    private static readonly string Docs = Path.Combine(Path.GetTempPath(), "Docs");

    [Fact]
    public void Extensions_are_cleaned_up_and_deduplicated()
    {
        var item = new RuleItem { Name = "Docs", ExtensionsText = "*.pdf, .DOCX;txt  pdf", Destination = Docs };

        Assert.Equal(new[] { "pdf", "docx", "txt" }, item.ToRule().Extensions);
    }

    [Fact]
    public void Sources_are_split_and_shown_in_the_summary()
    {
        var item = new RuleItem { Name = "GH", SourcesText = "github.com, nuget.org;GitHub.com", Destination = Docs };

        Assert.Equal(new[] { "github.com", "nuget.org" }, item.ToRule().Sources);
        Assert.Contains("from github.com, nuget.org", item.Summary);
        Assert.False(item.HasProblem); // A website alone is enough.
    }

    [Fact]
    public void Auto_move_is_off_by_default_and_shown_in_the_summary()
    {
        var item = new RuleItem { Name = "Docs", ExtensionsText = "pdf", Destination = Docs };
        Assert.False(item.ToRule().AutoMove);
        Assert.DoesNotContain("without asking", item.Summary);

        item.AutoMove = true;
        Assert.Contains("without asking", item.Summary);
    }

    [Fact]
    public void A_non_website_source_is_a_problem()
    {
        var item = new RuleItem { Name = "Bad", SourcesText = "about:blank", Destination = Docs };

        Assert.Contains("not a website", item.Problem);
    }

    [Fact]
    public void Round_trips_through_Rule()
    {
        var rule = new Rule
        {
            Name = "Invoices",
            Enabled = false,
            Extensions = { "pdf", "png" },
            NamePattern = "invoice|receipt",
            Sources = { "github.com" },
            AutoMove = true,
            Destination = Docs,
        };

        var back = RuleItem.FromRule(rule).ToRule();

        Assert.Equal(rule.Name, back.Name);
        Assert.Equal(rule.Enabled, back.Enabled);
        Assert.Equal(rule.Extensions, back.Extensions);
        Assert.Equal(rule.NamePattern, back.NamePattern);
        Assert.Equal(rule.Sources, back.Sources);
        Assert.True(back.AutoMove);
        Assert.Equal(rule.Destination, back.Destination);
    }

    [Fact]
    public void Blank_pattern_is_saved_as_null_and_text_is_trimmed()
    {
        var rule = new RuleItem { Name = "  Docs ", ExtensionsText = "pdf", NamePattern = "   ", Destination = $" {Docs} " }.ToRule();

        Assert.Equal("Docs", rule.Name);
        Assert.Null(rule.NamePattern);
        Assert.Equal(Docs, rule.Destination);
    }

    [Fact]
    public void Problem_is_reported_only_for_enabled_rules()
    {
        var item = new RuleItem { Name = "", ExtensionsText = "pdf", Destination = Docs };
        Assert.True(item.HasProblem);
        Assert.Contains("name", item.Problem);

        item.Enabled = false;
        Assert.False(item.HasProblem);
    }

    [Fact]
    public void Pattern_error_is_shown_for_invalid_regex()
    {
        var item = new RuleItem { NamePattern = "(unclosed" };

        Assert.NotNull(item.PatternError);

        item.NamePattern = "^ok";
        Assert.Null(item.PatternError);
    }

    [Fact]
    public void Unnamed_rule_has_a_display_name()
    {
        Assert.Equal("(unnamed rule)", new RuleItem { Name = " " }.DisplayName);
    }
}
