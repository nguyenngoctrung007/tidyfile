using TidyFile.Core.Workspace;

namespace TidyFile.Core.Tests;

public class WorkspaceRecordTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("workspace-setup.json");
        var record = new WorkspaceRecord
        {
            BaseFolder = @"D:\Workspace",
            CreatedAt = DateTimeOffset.Now,
            CreatedFolders = { @"D:\Workspace", @"D:\Workspace\Inbox" },
            ReadmeSha256 = "ab12",
            RedirectedRuleName = "Installers",
            RedirectedFrom = @"{Downloads}\Installers",
            RedirectedTo = @"D:\Workspace\Resources\Tools",
        };

        record.Save(path);
        var back = WorkspaceRecord.Load(path)!;

        Assert.Equal(record.CreatedFolders, back.CreatedFolders);
        Assert.Equal(record.RedirectedFrom, back.RedirectedFrom);
        Assert.False(back.IsEmpty);
    }

    [Fact]
    public void Missing_or_unreadable_record_is_none()
    {
        using var temp = new TempFolder();

        Assert.Null(WorkspaceRecord.Load(temp.Combine("nope.json")));
        Assert.Null(WorkspaceRecord.Load(temp.CreateFile("bad.json", "{ broken")));
    }

    [Fact]
    public void New_record_is_empty()
    {
        Assert.True(new WorkspaceRecord { BaseFolder = @"D:\Workspace" }.IsEmpty);
    }
}
