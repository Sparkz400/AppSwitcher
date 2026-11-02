using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class SecurityServiceTests
{
    [Fact]
    public void CurrentHashesUseRandomSaltsAndRejectWrongPins()
    {
        var service = new SecurityService();
        var hash = service.HashPassword("123456");
        Assert.NotEqual(hash, service.HashPassword("123456"));
        Assert.True(service.VerifyPassword("123456", hash));
        Assert.False(service.VerifyPassword("654321", hash));
        Assert.False(service.NeedsRehash(hash));
        Assert.Equal(AccessLevel.Admin, service.GetAccessLevel("123456", hash, ""));
    }

    [Fact]
    public void LegacyHashesRemainUsableAndRequireMigration()
    {
        var service = new SecurityService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("1234"), salt, 100_000, HashAlgorithmName.SHA256, 32);
        var legacyPbkdf2 = $"PBKDF2${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        var legacySha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AppSwitcher_SCADA_2024" + "1234")));
        foreach (var hash in new[] { legacyPbkdf2, legacySha })
        {
            Assert.True(service.VerifyPassword("1234", hash));
            Assert.False(service.VerifyPassword("9999", hash));
            Assert.True(service.NeedsRehash(hash));
            Assert.Equal(AccessLevel.Operator, service.GetAccessLevel("1234", "", hash));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("PBKDF2-SHA256$100000$$")]
    [InlineData("PBKDF2$AA==$AA==")]
    [InlineData("PBKDF2-SHA256$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("PBKDF2-SHA256$-1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("PBKDF2-SHA256$2147483647$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("PBKDF2-SHA256$100000$invalid!$invalid!")]
    public void MalformedHashesFailClosedWithoutDerivationOrExceptions(string hash)
    {
        Assert.False(new SecurityService().VerifyPassword("1234", hash));
        if (hash.Length == 0) return;
        var config = new ConfigRoot { Settings = new AppSettings { AdminPasswordHash = hash } };
        var result = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance).Validate(config);
        Assert.Contains(result.Errors, error => error.Message.Contains("Diag_InvalidPinHash"));
    }

    [Fact]
    public void OversizedHashIsRejectedBeforeParsing()
    {
        Assert.False(new SecurityService().VerifyPassword("1234", new string('A', 1_000_000)));
    }

    [Fact]
    public void RetryDelayGrowsIsBoundedAndSuccessfulAuthenticationResetsIt()
    {
        var clock = new ManualClock();
        var service = new SecurityService(clock);
        var hash = service.HashPassword("1234");
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(AccessLevel.None, service.GetAccessLevel("0000", hash, ""));
        Assert.Equal(TimeSpan.FromSeconds(2), service.RetryAfter);
        Assert.Equal(AccessLevel.None, service.GetAccessLevel("1234", hash, ""));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), service.RetryAfter);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AccessLevel.None, service.GetAccessLevel("0000", hash, ""));
        Assert.Equal(TimeSpan.FromSeconds(4), service.RetryAfter);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            clock.Advance(service.RetryAfter);
            Assert.Equal(AccessLevel.None, service.GetAccessLevel("0000", hash, ""));
            Assert.InRange(service.RetryAfter.TotalSeconds, 0, 30);
        }
        clock.Advance(service.RetryAfter);
        Assert.Equal(AccessLevel.Admin, service.GetAccessLevel("1234", hash, ""));
        Assert.Equal(TimeSpan.Zero, service.RetryAfter);
        Assert.Equal(AccessLevel.None, service.GetAccessLevel("0000", hash, ""));
        Assert.Equal(TimeSpan.Zero, service.RetryAfter);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
