using TidyFile.Core.Files;

namespace TidyFile.Core.Tests;

public class FileReadinessTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Complete_file_is_ready()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("done.txt", "content");

        Assert.True(await FileReadiness.WaitUntilReadyAsync(path, TimeSpan.FromSeconds(5), Poll));
    }

    [Fact]
    public async Task Missing_file_is_not_ready()
    {
        using var temp = new TempFolder();

        Assert.False(await FileReadiness.WaitUntilReadyAsync(temp.Combine("gone.txt"), TimeSpan.FromSeconds(5), Poll));
    }

    [Fact]
    public async Task Empty_file_is_given_up_on_after_the_grace_period()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("empty.txt", "");

        var ready = await FileReadiness.WaitUntilReadyAsync(
            path, TimeSpan.FromSeconds(5), Poll, emptyFileGrace: TimeSpan.FromMilliseconds(100));

        Assert.False(ready);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var temp = new TempFolder();
        var path = temp.CreateFile("empty.txt", "");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileReadiness.WaitUntilReadyAsync(path, TimeSpan.FromSeconds(5), Poll, TimeSpan.FromSeconds(5), cts.Token));
    }
}
