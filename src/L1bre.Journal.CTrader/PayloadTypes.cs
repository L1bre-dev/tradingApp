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
    public const int AccountAuthReq = 2102;
    public const int AccountAuthRes = 2103;
    public const int AssetListReq = 2112;
    public const int AssetListRes = 2113;
    public const int SymbolsListReq = 2114;
    public const int SymbolsListRes = 2115;
    public const int SymbolByIdReq = 2116;
    public const int SymbolByIdRes = 2117;
    public const int TraderReq = 2121;
    public const int TraderRes = 2122;
    public const int DealListReq = 2133;
    public const int DealListRes = 2134;
    public const int GetTrendbarsReq = 2137;
    public const int GetTrendbarsRes = 2138;
    public const int OaErrorRes = 2142;
    public const int GetTickDataReq = 2145;
    public const int GetTickDataRes = 2146;
    public const int AccountsTokenInvalidatedEvent = 2147;
    public const int ClientDisconnectEvent = 2148;
    public const int GetAccountsByAccessTokenReq = 2149;
    public const int GetAccountsByAccessTokenRes = 2150;
    public const int GetCtidProfileByTokenReq = 2151;
    public const int GetCtidProfileByTokenRes = 2152;
    public const int RefreshTokenReq = 2173;
    public const int RefreshTokenRes = 2174;
    public const int OrderDetailsReq = 2181;
    public const int OrderDetailsRes = 2182;

    /// <summary>Historical requests: cTrader allows 5 per second on a connection (the others 50).</summary>
    public static bool IsHistorical(int payloadType) => payloadType is DealListReq or GetTrendbarsReq or GetTickDataReq;
}
