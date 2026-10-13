using TidyFile.Core.Paths;
using TidyFile.Core.Rules;
using TidyFile.Core.Storage;

namespace TidyFile.Core.Tests;

public class MiscTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(10L * 1024 * 1024, "10 MB")]
    public void Bytes_are_formatted(long bytes, string expected)
    {
        Assert.Equal(expected, Format.Bytes(bytes));
    }

    [Fact]
    public void Home_token_expands_and_shortens_back()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expanded = PathTokens.Expand(Path.Combine("{Home}", "Projects"));

        Assert.Equal(Path.Combine(home, "Projects"), expanded);
        Assert.EndsWith("Projects", PathTokens.Shorten(expanded));
        Assert.DoesNotContain(home, PathTokens.Shorten(expanded));
    }

    [Fact]
    public void Unknown_tokens_are_left_alone()
    {
        Assert.Equal("{Nope}", PathTokens.Expand("{Nope}"));
    }

    [Fact]
    public void Missing_json_file_is_created_with_defaults()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("rules.json");

        var doc = JsonFile.LoadOrCreate(path, DefaultRules.Create, out var warning);

        Assert.Null(warning);
        Assert.True(File.Exists(path));
        Assert.NotEmpty(doc.Rules);
    }

    [Fact]
    public void Corrupt_json_file_falls_back_to_defaults_and_keeps_a_copy()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("settings.json", "{ not json");

        var settings = JsonFile.LoadOrCreate(path, () => new AppSettings(), out var warning);

        Assert.NotNull(warning);
        Assert.Equal(2, settings.WatchFolders.Count);
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Rules_provider_reloads_after_the_file_changes()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("rules.json");
        var provider = new RulesProvider(path);
        var first = provider.GetEngine();

        var doc = new RulesDocument { Rules = { new Rule { Name = "Only", Extensions = { "x" }, Destination = temp.Path } } };
        JsonFile.Save(path, doc);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var second = provider.GetEngine();

        Assert.NotSame(first, second);
        Assert.Equal(1, second.RuleCount);
        Assert.Same(second, provider.GetEngine());
    }

    [Fact]
    public void Tokenize_keeps_braces_and_round_trips()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var full = Path.Combine(home, "SomeFolder");

        var tokenized = PathTokens.Tokenize(full);

        Assert.StartsWith("{", tokenized);
        Assert.Equal(full, PathTokens.Expand(tokenized));
        Assert.Equal(@"D:\Work", PathTokens.Tokenize(@"D:\Work"));
    }

    [Fact]
    public void Rules_provider_saves_and_uses_new_rules_immediately()
    {
        using var temp = new TempFolder();
        var provider = new RulesProvider(temp.Combine("rules.json"));
        var doc = provider.LoadDocument(out var warning);
        Assert.NotEmpty(doc.Rules); // Defaults on first run.
        Assert.Null(warning);
        provider.GetEngine();

        doc.Rules.Clear();
        doc.Rules.Add(new Rule { Name = "Only", Extensions = { "abc" }, Destination = temp.Path });
        provider.Save(doc);

        Assert.Equal(1, provider.GetEngine().RuleCount);
        Assert.Equal("Only", Assert.Single(provider.LoadDocument(out _).Rules).Name);
    }

    [Fact]
    public void Rules_provider_reports_a_corrupt_file_when_loading_for_editing()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("rules.json");
        File.WriteAllText(path, "{ \"rules\": [ oops");
        var provider = new RulesProvider(path);

        var doc = provider.LoadDocument(out var warning);

        Assert.NotNull(warning);
        Assert.NotEmpty(doc.Rules); // The defaults, which the Settings window must flag before they overwrite the file.
        Assert.True(File.Exists(path + ".bad"));
        Assert.Equal("{ \"rules\": [ oops", File.ReadAllText(path)); // Not replaced until the user saves.
    }
}
