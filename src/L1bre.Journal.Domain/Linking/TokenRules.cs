namespace L1bre.Journal.Domain.Linking;

/// <summary>
/// When to renew a cTrader access token. It lasts about 30 days and the refresh token never expires,
/// so the app renews it well before the end: a VPS that is down for a few days doesn't lose the link.
/// </summary>
public static class TokenRules
{
    /// <summary>Renew when less than this is left.</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromDays(7);

    /// <summary>How often the background job looks for tokens to renew.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    public static bool NeedsRefresh(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt - now < RefreshBefore;
}
