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
}
