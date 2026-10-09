namespace L1bre.Journal.CTrader;

/// <summary>
/// Numeric message types of the Open API, from the official enums
/// (ProtoPayloadType and ProtoOAPayloadType on help.ctrader.com). Only the ones we use.
/// </summary>
public static class PayloadTypes
{
    public const int ErrorRes = 50;
    public const int HeartbeatEvent = 51;

    public const int ApplicationAuthReq = 2100;
    public const int ApplicationAuthRes = 2101;
    public const int OaErrorRes = 2142;
    public const int AccountsTokenInvalidatedEvent = 2147;
    public const int ClientDisconnectEvent = 2148;
    public const int GetAccountsByAccessTokenReq = 2149;
    public const int GetAccountsByAccessTokenRes = 2150;
    public const int GetCtidProfileByTokenReq = 2151;
    public const int GetCtidProfileByTokenRes = 2152;
    public const int RefreshTokenReq = 2173;
    public const int RefreshTokenRes = 2174;
}
