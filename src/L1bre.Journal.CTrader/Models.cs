namespace L1bre.Journal.CTrader;

/// <summary>Tokens for one cTrader ID. The access token lasts about 30 days; the refresh token never expires.</summary>
public record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>One trading account reachable with a token (ProtoOACtidTraderAccount).</summary>
public record CTraderAccountInfo(long CtidTraderAccountId, bool IsLive, long? TraderLogin, string? BrokerName);

/// <summary>Who the token belongs to and which accounts it opens.</summary>
public record CTraderIdentity(long CtidUserId, IReadOnlyList<CTraderAccountInfo> Accounts, string? PermissionScope);

/// <summary>An error answered by cTrader (OAuth endpoint or Open API ProtoOAErrorRes).</summary>
public class CTraderException(string errorCode, string? description)
    : Exception(string.IsNullOrWhiteSpace(description) ? errorCode : $"{errorCode}: {description}")
{
    public string ErrorCode { get; } = errorCode;
    public string? Description { get; } = description;

    /// <summary>The token was revoked or is no longer valid: the trader has to link again.</summary>
    public bool IsTokenProblem =>
        ErrorCode.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
        || ErrorCode.Contains("ACCESS_DENIED", StringComparison.OrdinalIgnoreCase)
        || ErrorCode.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);
}
