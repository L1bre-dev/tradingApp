using System.Text.Json.Nodes;

namespace L1bre.Journal.CTrader;

/// <summary>Turns Open API responses into our records. Kept separate so it can be unit-tested.</summary>
public static class OpenApiParsing
{
    public static IReadOnlyList<CTraderAccountInfo> Accounts(OpenApiMessage res)
    {
        if (res.Payload["ctidTraderAccount"] is not JsonArray array) return [];
        return array.OfType<JsonObject>()
            .Select(a => new CTraderAccountInfo(
                OpenApiMessage.ReadInt64(a["ctidTraderAccountId"]) ?? throw new FormatException("Account without ctidTraderAccountId."),
                OpenApiMessage.ReadBool(a["isLive"]) ?? false,
                OpenApiMessage.ReadInt64(a["traderLogin"]),
                a["brokerTitleShort"] is JsonValue b ? b.ToString() : null))
            .ToList();
    }

    public static long CtidUserId(OpenApiMessage res) =>
        OpenApiMessage.ReadInt64((res.Payload["profile"] as JsonObject)?["userId"])
        ?? throw new FormatException("Profile response without userId.");

    public static TokenSet RefreshedTokens(OpenApiMessage res, DateTimeOffset now) => new(
        res.GetString("accessToken") ?? throw new FormatException("Refresh response without accessToken."),
        res.GetString("refreshToken") ?? throw new FormatException("Refresh response without refreshToken."),
        now.AddSeconds(res.GetInt64("expiresIn") ?? 0));
}
