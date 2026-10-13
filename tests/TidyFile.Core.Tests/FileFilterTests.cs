using TidyFile.Core.Files;

namespace TidyFile.Core.Tests;

public class FileFilterTests
{
    [Theory]
    [InlineData("Unconfirmed 123456.crdownload")]
    [InlineData("setup.exe.part")]
    [InlineData("big.zip.PARTIAL")]
    [InlineData("~WRL0001.tmp")]
    [InlineData("~$Report.docx")]
    [InlineData(".hidden")]
    [InlineData("desktop.ini")]
    [InlineData("Thumbs.db")]
    [InlineData("Visual Studio Code.lnk")]
    [InlineData("Some site.url")]
    public void Noise_files_are_ignored(string name)
    {
        Assert.True(FileFilter.ShouldIgnore(Path.Combine("C:", "Downloads", name)));
    }

    [Theory]
    [InlineData("Invoice-2026-10.pdf")]
    [InlineData("setup.exe")]
    [InlineData("photo.JPG")]
    [InlineData("README")]
    [InlineData("archive.tar.gz")]
    public void Real_files_are_not_ignored(string name)
    {
        Assert.False(FileFilter.ShouldIgnore(Path.Combine("C:", "Downloads", name)));
    }
}
