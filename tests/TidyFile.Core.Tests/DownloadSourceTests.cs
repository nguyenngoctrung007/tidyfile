using TidyFile.Core.Files;

namespace TidyFile.Core.Tests;

public class DownloadSourceTests
{
    // What Chrome / Edge write after a download (CRLF line endings).
    private const string ChromeZone =
        "[ZoneTransfer]\r\nZoneId=3\r\n" +
        "ReferrerUrl=https://github.com/dotnet/runtime/releases\r\n" +
        "HostUrl=https://objects.githubusercontent.com/github-production-release-asset/abc\r\n";

    [Fact]
    public void Parses_page_and_file_domains()
    {
        var source = DownloadSource.Parse(ChromeZone)!;

        Assert.Equal("github.com", source.ReferrerDomain);
        Assert.Equal("objects.githubusercontent.com", source.HostDomain);
        Assert.Equal("github.com", source.DisplayDomain);
        Assert.Equal(new[] { "github.com", "objects.githubusercontent.com" }, source.Domains);
    }

    [Fact]
    public void Falls_back_to_the_file_domain_when_there_is_no_page()
    {
        var source = DownloadSource.Parse("[ZoneTransfer]\nZoneId=3\nHostUrl=https://www.example.org/a.zip\n")!;

        Assert.Null(source.ReferrerDomain);
        Assert.Equal("example.org", source.DisplayDomain);
    }

    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\n")]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=about:internet\r\n")] // Chrome in some private modes
    [InlineData("")]
    public void Returns_null_without_a_usable_url(string zone)
    {
        Assert.Null(DownloadSource.Parse(zone));
    }

    [Theory]
    [InlineData("https://www.GitHub.com/x?y=1", "github.com")]
    [InlineData("http://example.org:8080/a", "example.org")]
    [InlineData("ftp://files.example.org/a", "files.example.org")]
    [InlineData("blob:https://web.whatsapp.com/1234", "web.whatsapp.com")]
    [InlineData("github.com", "github.com")]
    [InlineData("  WWW.nuget.org  ", "nuget.org")]
    [InlineData("about:internet", null)]
    [InlineData("data:text/plain,hi", null)]
    [InlineData(@"C:\Users\me\file.txt", null)]
    [InlineData("file:///C:/x.txt", null)]
    [InlineData("not a website", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DomainOf_normalizes_urls_and_bare_domains(string? input, string? expected)
    {
        Assert.Equal(expected, DownloadSource.DomainOf(input));
    }

    [Theory]
    [InlineData("github.com", "github.com", true)]
    [InlineData("objects.github.com", "github.com", true)]
    [InlineData("GitHub.com", "github.com", true)]
    [InlineData("notgithub.com", "github.com", false)]
    [InlineData("github.com.evil.io", "github.com", false)]
    public void Subdomains_match_but_lookalikes_do_not(string domain, string ruleDomain, bool expected)
    {
        Assert.Equal(expected, DownloadSource.Matches(domain, ruleDomain));
    }

    [Fact]
    public void Reads_the_alternate_data_stream_of_a_real_file()
    {
        using var temp = new TempFolder();
        var file = temp.CreateFile("setup.exe");
        File.WriteAllText(file + ":Zone.Identifier", ChromeZone);

        var source = DownloadSource.Read(file);

        Assert.Equal("github.com", source?.DisplayDomain);
        Assert.Equal("hello", File.ReadAllText(file)); // The file's own content is untouched.
    }

    [Fact]
    public void Read_returns_null_for_files_without_a_source()
    {
        using var temp = new TempFolder();

        Assert.Null(DownloadSource.Read(temp.CreateFile("local.txt")));
        Assert.Null(DownloadSource.Read(temp.Combine("missing.txt")));
    }
}
