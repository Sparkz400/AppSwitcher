using System.Security.Cryptography;
using System.Text;

namespace AppSwitcher.Services;

public class SecurityService
{
    private const string CurrentHashPrefix = "PBKDF2-SHA256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string LegacySalt = "AppSwitcher_SCADA_2024";
    private readonly TimeProvider _clock;
    private int _failedAttempts;
    private long _lastFailure;
    private TimeSpan _retryDelay;

    public SecurityService() : this(TimeProvider.System) { }
    internal SecurityService(TimeProvider clock) => _clock = clock;

    public TimeSpan RetryAfter => TimeSpan.FromTicks(Math.Max(0,
        (_retryDelay - _clock.GetElapsedTime(_lastFailure)).Ticks));

    public string AuthenticationFailureMessage => RetryAfter > TimeSpan.Zero
        ? LocalizationService.T("Msg_PinRetryAfter", (int)Math.Ceiling(RetryAfter.TotalSeconds))
        : LocalizationService.T("Msg_WrongPassword");

    /// <summary>
    /// Hashes a password using PBKDF2 with a random salt.
    /// Format: PBKDF2-SHA256$iterations$base64salt$base64hash
    /// </summary>
    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{CurrentHashPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a stored hash. Supports both new PBKDF2 and legacy SHA256 format.
    /// Returns false if no hash is stored (forces PIN setup).
    /// </summary>
    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash) || !IsSupportedHash(storedHash)) return false;

        if (storedHash.StartsWith($"{CurrentHashPrefix}$", StringComparison.Ordinal))
            return VerifyCurrentPbkdf2(password, storedHash);

        if (storedHash.StartsWith("PBKDF2$", StringComparison.Ordinal))
            return VerifyLegacyPbkdf2(password, storedHash);

        // Legacy SHA256 format — verify with constant-time comparison
        return VerifyLegacy(password, storedHash);
    }

    /// <summary>
    /// Returns true if a legacy (SHA256) hash format is detected, indicating re-hashing is needed.
    /// </summary>
    public bool NeedsRehash(string storedHash)
    {
        return !string.IsNullOrEmpty(storedHash)
            && !storedHash.StartsWith($"{CurrentHashPrefix}$", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns true if no admin PIN has been configured yet.
    /// </summary>
    public bool IsPinConfigured(string storedHash) => !string.IsNullOrEmpty(storedHash);

    /// <summary>
    /// Determines the access level for the given PIN against stored hashes.
    /// Checks admin first, then operator.
    /// </summary>
    public AccessLevel GetAccessLevel(string password, string adminHash, string operatorHash)
    {
        if (RetryAfter > TimeSpan.Zero) return AccessLevel.None;
        var access = AccessLevel.None;
        if (!string.IsNullOrEmpty(adminHash) && VerifyPassword(password, adminHash))
            access = AccessLevel.Admin;
        else if (!string.IsNullOrEmpty(operatorHash) && VerifyPassword(password, operatorHash))
            access = AccessLevel.Operator;
        if (access != AccessLevel.None)
        {
            _failedAttempts = 0;
            _retryDelay = TimeSpan.Zero;
        }
        else
        {
            _failedAttempts = Math.Min(_failedAttempts + 1, 7);
            _lastFailure = _clock.GetTimestamp();
            _retryDelay = _failedAttempts < 3 ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Min(30, 2 << (_failedAttempts - 3)));
        }
        return access;
    }

    internal static bool IsSupportedHash(string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return true; // An unset PIN is a supported configuration.
        if (storedHash.Length > 256) return false;
        if (!storedHash.StartsWith("PBKDF2", StringComparison.Ordinal))
            return storedHash.Length == 64 && storedHash.All(char.IsAsciiHexDigit);
        var parts = storedHash.Split('$');
        var current = parts[0] == CurrentHashPrefix;
        if (current ? parts.Length != 4 : parts[0] != "PBKDF2" || parts.Length != 3) return false;
        // Bound work before deriving a key from imported or damaged configuration.
        if (current && (!int.TryParse(parts[1], out var iterations) || iterations is < 1 or > 1_000_000)) return false;
        try
        {
            return Convert.FromBase64String(parts[^2]).Length == SaltSize
                && Convert.FromBase64String(parts[^1]).Length == HashSize;
        }
        catch (FormatException) { return false; }
    }

    private static bool VerifyCurrentPbkdf2(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                iterations, HashAlgorithmName.SHA256, expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyLegacyPbkdf2(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 3) return false;

            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                Iterations, HashAlgorithmName.SHA256, expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyLegacy(string password, string storedHash)
    {
        var bytes = Encoding.UTF8.GetBytes(LegacySalt + password);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Use constant-time comparison even for legacy
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
