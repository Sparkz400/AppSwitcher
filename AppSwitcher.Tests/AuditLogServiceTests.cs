using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class AuditLogServiceTests
{
    [Fact]
    public void GetRecentEntries_Returns_Empty_When_File_Does_Not_Exist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var service = new AuditLogService(NullLogger<AuditLogService>.Instance, path);

        var entries = service.GetRecentEntries();

        Assert.Empty(entries);
    }

    [Fact]
    public void Record_And_Read_Back_Preserves_Escaped_Fields_And_Newest_First_Order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var service = new AuditLogService(NullLogger<AuditLogService>.Instance, path);

        try
        {
            service.Record("ConfigChanged", new AuditActor("Admin", @"TEST\alice"), "Success", "Configuration", "Theme: Dark, \"Blue\"");
            service.Record("AppStopped", new AuditActor("Operator", @"TEST\bob"), "Success", "Pump UI", "Group: Operations");

            var entries = service.GetRecentEntries();

            Assert.Equal(2, entries.Count);
            Assert.Equal("AppStopped", entries[0].Action);
            Assert.Equal("ConfigChanged", entries[1].Action);
            Assert.Equal("Theme: Dark, \"Blue\"", entries[1].Details);
            Assert.Equal(@"TEST\alice", entries[1].ActorUser);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
