using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class AuditLogServiceTests
{
    [Fact]
    public void RotationRetainsOlderEntriesAndOnlyFiveArchives()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var service = new AuditLogService(NullLogger<AuditLogService>.Instance, path);
        try
        {
            for (var i = 0; i < 7; i++)
            {
                service.Record("ConfigChanged", new AuditActor("Admin", "Test"), "Success", "Config", $"event {i}");
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(AuditLogService.RotationBytes);
                service.Record("ConfigChanged", new AuditActor("Admin", "Test"), "Success", "Config", $"tail {i}");
            }
            Assert.True(File.Exists(path + ".5"));
            Assert.False(File.Exists(path + ".6"));
            Assert.Equal("tail 6", service.GetRecentEntries(1)[0].Details);
        }
        finally
        {
            File.Delete(path);
            for (var i = 1; i <= AuditLogService.RetainedArchives; i++) File.Delete(path + "." + i);
        }
    }
    [Fact]
    public void MultilineUnicodeTailIsExactAndAllocationDoesNotScaleWithHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var service = new AuditLogService(NullLogger<AuditLogService>.Instance, path);
        try
        {
            using (var writer = new StreamWriter(path))
                for (var i = 0; i < 100000; i++) writer.WriteLine("2026-11-03 10:00:00,ConfigChanged,Admin,Test,Success,Config,synthetic");
            var details = "ümlaut, \"quoted\"\r\nsecond line\nthird 🚀";
            service.Record("ConfigChanged", new AuditActor("Admin", "Test"), "Success", "Config", details);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var entries = service.GetRecentEntries(200);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(200, entries.Count);
            Assert.Equal(details, entries[0].Details);
            Assert.True(allocated < 2 * 1024 * 1024, $"Allocated {allocated} bytes");
        }
        finally { File.Delete(path); }
    }
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
