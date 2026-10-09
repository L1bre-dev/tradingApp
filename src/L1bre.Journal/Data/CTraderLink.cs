namespace L1bre.Journal.Data;

public enum LinkStatus
{
    /// <summary>Tokens work.</summary>
    Active,

    /// <summary>cTrader refused the tokens (revoked or invalid): the trader must link again.</summary>
    NeedsRelink,
}

/// <summary>
/// One cTrader ID linked by a user. A user can link several cTrader IDs,
/// and each cTrader ID can open several trading accounts.
/// Tokens are stored encrypted (see CTraderTokenProtector).
/// </summary>
public class CTraderLink
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    /// <summary>cTrader's own id for the cTrader ID (ProtoOACtidProfile.userId).</summary>
    public long CtidUserId { get; set; }

    public string AccessTokenEncrypted { get; set; } = "";
    public string RefreshTokenEncrypted { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    public LinkStatus Status { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? TokenRefreshedAt { get; set; }
    public DateTimeOffset? AccountsCheckedAt { get; set; }

    /// <summary>Last problem talking to cTrader for this link, shown on the accounts page.</summary>
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }

    public List<TradingAccount> Accounts { get; set; } = [];
}
