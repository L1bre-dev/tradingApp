using System.Globalization;

namespace L1bre.Journal.Domain.Linking;

/// <summary>
/// Remembers, in a protected cookie, that this user just left for the cTrader login page.
/// The callback is only accepted for that same user, within 10 minutes, so another site can't
/// make someone's browser link a cTrader ID they didn't choose.
/// </summary>
public sealed record PendingLink(string UserId, string State, DateTimeOffset ExpiresAt)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static PendingLink Create(string userId, string state, DateTimeOffset now) => new(userId, state, now + Lifetime);

    public string Serialize() => string.Join('|', UserId, State, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    public static PendingLink? Deserialize(string? text)
    {
        var parts = text?.Split('|');
        if (parts is not { Length: 3 } || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires)) return null;
        return new PendingLink(parts[0], parts[1], DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    /// <summary>
    /// cTrader documents only <c>code</c> on the callback. If it also returns <c>state</c>, it must match.
    /// </summary>
    public bool Accepts(string userId, string? returnedState, DateTimeOffset now) =>
        userId == UserId
        && now < ExpiresAt
        && (string.IsNullOrEmpty(returnedState) || returnedState == State);
}
