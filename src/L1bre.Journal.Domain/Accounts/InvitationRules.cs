namespace L1bre.Journal.Domain.Accounts;

public enum InvitationState
{
    Pending,
    Used,
    Revoked,
    Expired,
}

/// <summary>
/// When an invitation link can still be used to create an account.
/// </summary>
public static class InvitationRules
{
    /// <summary>An invitation link works for 7 days.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public static InvitationState StateAt(DateTimeOffset now, DateTimeOffset expiresAt, DateTimeOffset? usedAt, DateTimeOffset? revokedAt)
    {
        // Used and revoked win over expired: they describe what really happened to the link.
        if (usedAt is not null) return InvitationState.Used;
        if (revokedAt is not null) return InvitationState.Revoked;
        if (now >= expiresAt) return InvitationState.Expired;
        return InvitationState.Pending;
    }

    /// <summary>
    /// Roles given by an invitation. Every invited person is at least a Trader or a Coach;
    /// an invitation with no role at all makes them a Trader.
    /// </summary>
    public static IReadOnlyList<string> NormalizeRoles(IEnumerable<string> roles)
    {
        var known = roles
            .Select(r => Roles.All.FirstOrDefault(k => string.Equals(k, r?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct()
            .ToList();

        if (!known.Contains(Roles.Trader) && !known.Contains(Roles.Coach))
            known.Insert(0, Roles.Trader);

        return Roles.All.Where(known.Contains).ToList();
    }
}
