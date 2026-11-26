namespace AppSwitcher.Services;

/// <summary>
/// The PIN sign-in rules, without UI: who gets which access level and which stored hashes change.
/// The caller supplies the PIN prompt and applies the outcome (notification, audit, save).
/// </summary>
internal sealed class PinSignIn(SecurityService security)
{
    internal sealed record Outcome(AccessLevel Level, string? Message = null, NotificationType MessageType = NotificationType.Info,
        bool HashesChanged = false, bool Denied = false);

    private static readonly Outcome Cancelled = new(AccessLevel.None);

    /// <param name="promptPin">Called with true to set a new PIN, false to enter one; returns null when cancelled.</param>
    public Outcome SignIn(AppSettings settings, AccessLevel minimumLevel, Func<bool, string?> promptPin)
    {
        if (!security.IsPinConfigured(settings.AdminPasswordHash))
        {
            if (promptPin(true) is not { } newPin) return Cancelled;
            if (SharesPin(newPin, settings.OperatorPasswordHash))
                return new(AccessLevel.None, LocalizationService.T("Msg_PinsMustDiffer"), NotificationType.Error);
            settings.AdminPasswordHash = security.HashPassword(newPin);
            return new(AccessLevel.Admin, LocalizationService.T("Msg_PinSet"), HashesChanged: true);
        }

        if (security.RetryAfter > TimeSpan.Zero)
            return new(AccessLevel.None, security.AuthenticationFailureMessage, NotificationType.Error);
        if (promptPin(false) is not { } pin) return Cancelled;

        var level = security.GetAccessLevel(pin, settings.AdminPasswordHash, settings.OperatorPasswordHash);
        if (level == AccessLevel.None || level < minimumLevel)
            return new(AccessLevel.None, security.AuthenticationFailureMessage, NotificationType.Error, Denied: true);

        // Upgrade legacy hashes on the first correct entry; the caller must save at once (an unsaved upgrade marks the config dirty).
        var rehashed = false;
        if (level == AccessLevel.Admin && security.NeedsRehash(settings.AdminPasswordHash))
        {
            settings.AdminPasswordHash = security.HashPassword(pin);
            rehashed = true;
        }
        if (level == AccessLevel.Operator && security.NeedsRehash(settings.OperatorPasswordHash))
        {
            settings.OperatorPasswordHash = security.HashPassword(pin);
            rehashed = true;
        }

        return level == AccessLevel.Operator
            ? new(level, LocalizationService.T("Msg_OperatorAccess"), HashesChanged: rehashed)
            : new(level, HashesChanged: rehashed);
    }

    /// <summary>Sign-in checks the admin PIN first, so a PIN shared by both roles would give operators admin rights.</summary>
    public bool SharesPin(string newPin, string otherRoleHash) => security.VerifyPassword(newPin, otherRoleHash);
}
