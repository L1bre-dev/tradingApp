using System.Text.Json.Nodes;
using L1bre.Journal.Domain.Trades;

namespace L1bre.Journal.CTrader;

/// <summary>The trading account's settings that matter to the journal (ProtoOATrader).</summary>
public record TraderInfo(int MoneyDigits, long DepositAssetId, DateTimeOffset? RegisteredAt, string? DepositCurrency);

/// <summary>A symbol's name and price format (ProtoOALightSymbol + ProtoOASymbol).</summary>
public record SymbolInfo(long SymbolId, string Name, int Digits, int PipPosition, long? LotSizeCents);

/// <summary>An execution, plus its original JSON kept in the database for later re-processing.</summary>
public record DealRecord(Fill Fill, string RawJson);

public record DealPage(IReadOnlyList<DealRecord> Deals, bool HasMore, DateTimeOffset? LatestExecution);

/// <summary>The order that opened a position: where its label, comment, stop loss and take profit live.</summary>
public record OrderInfo(
    long OrderId,
    string? OrderType,
    string? Label,
    string? Comment,
    decimal? StopLoss,
    decimal? TakeProfit,
    long? RelativeStopLoss,
    long? RelativeTakeProfit,
    decimal? LimitPrice,
    decimal? StopPrice);

/// <summary>Turns the account-level Open API answers into journal records. Pure functions, unit-tested.</summary>
public static class AccountParsing
{
    /// <summary>Prices in trendbars, ticks and relative SL/TP are integers in 1/100000 of the price.</summary>
    public const decimal RelativeScale = 100000m;

    public static readonly IReadOnlyDictionary<string, int> TradeSides = new Dictionary<string, int> { ["BUY"] = 1, ["SELL"] = 2 };

    public static readonly IReadOnlyDictionary<string, int> DealStatuses = new Dictionary<string, int>
    {
        ["FILLED"] = 2, ["PARTIALLY_FILLED"] = 3, ["REJECTED"] = 4, ["INTERNALLY_REJECTED"] = 5, ["ERROR"] = 6, ["MISSED"] = 7,
    };

    public static readonly IReadOnlyDictionary<string, int> OrderTypes = new Dictionary<string, int>
    {
        ["MARKET"] = 1, ["LIMIT"] = 2, ["STOP"] = 3, ["STOP_LOSS_TAKE_PROFIT"] = 4, ["MARKET_RANGE"] = 5, ["STOP_LIMIT"] = 6,
    };

    public static TraderInfo Trader(OpenApiMessage res)
    {
        var trader = res.Payload["trader"] as JsonObject ?? throw new FormatException("Trader response without trader.");
        var registered = OpenApiMessage.ReadInt64(trader["registrationTimestamp"]);
        return new TraderInfo(
            (int)(OpenApiMessage.ReadInt64(trader["moneyDigits"]) ?? 2),
            OpenApiMessage.ReadInt64(trader["depositAssetId"]) ?? 0,
            registered is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(registered.Value),
            null);
    }

    public static string? AssetName(OpenApiMessage res, long assetId) =>
        (res.Payload["asset"] as JsonArray)?.OfType<JsonObject>()
            .Where(a => OpenApiMessage.ReadInt64(a["assetId"]) == assetId)
            .Select(a => OpenApiMessage.ReadString(a["displayName"]) ?? OpenApiMessage.ReadString(a["name"]))
            .FirstOrDefault();

    public static IReadOnlyDictionary<long, string> SymbolNames(OpenApiMessage res) =>
        (res.Payload["symbol"] as JsonArray)?.OfType<JsonObject>()
            .Select(s => (Id: OpenApiMessage.ReadInt64(s["symbolId"]), Name: OpenApiMessage.ReadString(s["symbolName"])))
            .Where(s => s.Id is not null)
            .DistinctBy(s => s.Id)
            .ToDictionary(s => s.Id!.Value, s => s.Name ?? $"#{s.Id}")
        ?? new Dictionary<long, string>();

    public static IReadOnlyList<SymbolInfo> Symbols(OpenApiMessage res, IReadOnlyDictionary<long, string> names) =>
        (res.Payload["symbol"] as JsonArray)?.OfType<JsonObject>()
            .Select(s =>
            {
                var id = OpenApiMessage.ReadInt64(s["symbolId"]) ?? throw new FormatException("Symbol without symbolId.");
                return new SymbolInfo(
                    id,
                    names.TryGetValue(id, out var name) ? name : $"#{id}",
                    (int)(OpenApiMessage.ReadInt64(s["digits"]) ?? 5),
                    (int)(OpenApiMessage.ReadInt64(s["pipPosition"]) ?? 4),
                    OpenApiMessage.ReadInt64(s["lotSize"]));
            })
            .ToList()
        ?? [];

    /// <summary>
    /// Executions of a deal list page. Rejected or failed deals are skipped: only filled volume makes a trade.
    /// Money fields are integers scaled by 10^moneyDigits (per deal, else the account's).
    /// </summary>
    public static DealPage Deals(OpenApiMessage res, int accountMoneyDigits)
    {
        var deals = new List<DealRecord>();
        DateTimeOffset? latest = null;
        foreach (var d in (res.Payload["deal"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var executed = DateTimeOffset.FromUnixTimeMilliseconds(OpenApiMessage.ReadInt64(d["executionTimestamp"]) ?? 0);
            if (latest is null || executed > latest) latest = executed;

            var status = OpenApiMessage.ReadEnum(d["dealStatus"], DealStatuses);
            var filled = OpenApiMessage.ReadInt64(d["filledVolume"]) ?? 0;
            if (status is not (2 or 3) || filled <= 0) continue;

            var moneyDigits = (int)(OpenApiMessage.ReadInt64(d["moneyDigits"]) ?? accountMoneyDigits);
            CloseDetail? close = null;
            if (d["closePositionDetail"] is JsonObject c)
            {
                var closeDigits = (int)(OpenApiMessage.ReadInt64(c["moneyDigits"]) ?? moneyDigits);
                close = new CloseDetail(
                    ToDecimal(OpenApiMessage.ReadDouble(c["entryPrice"]) ?? 0),
                    OpenApiMessage.ReadInt64(c["closedVolume"]) ?? 0,
                    Money(c["grossProfit"], closeDigits),
                    Money(c["swap"], closeDigits),
                    Money(c["commission"], closeDigits),
                    Money(c["pnlConversionFee"], closeDigits));
            }

            var fill = new Fill(
                OpenApiMessage.ReadInt64(d["dealId"]) ?? throw new FormatException("Deal without dealId."),
                OpenApiMessage.ReadInt64(d["positionId"]) ?? 0,
                OpenApiMessage.ReadInt64(d["orderId"]) ?? 0,
                OpenApiMessage.ReadInt64(d["symbolId"]) ?? 0,
                OpenApiMessage.ReadEnum(d["tradeSide"], TradeSides) == 2 ? Direction.Sell : Direction.Buy,
                filled,
                executed,
                ToDecimal(OpenApiMessage.ReadDouble(d["executionPrice"]) ?? 0),
                Money(d["commission"], moneyDigits),
                close);
            deals.Add(new DealRecord(fill, d.ToJsonString()));
        }
        return new DealPage(deals, OpenApiMessage.ReadBool(res.Payload["hasMore"]) ?? false, latest);
    }

    public static OrderInfo Order(OpenApiMessage res)
    {
        var o = res.Payload["order"] as JsonObject ?? throw new FormatException("Order details without order.");
        var trade = o["tradeData"] as JsonObject;
        var type = OpenApiMessage.ReadEnum(o["orderType"], OrderTypes);
        return new OrderInfo(
            OpenApiMessage.ReadInt64(o["orderId"]) ?? 0,
            type is null ? null : OrderTypes.FirstOrDefault(t => t.Value == type).Key,
            Text(trade?["label"]),
            Text(trade?["comment"]),
            Price(o["stopLoss"]),
            Price(o["takeProfit"]),
            OpenApiMessage.ReadInt64(o["relativeStopLoss"]),
            OpenApiMessage.ReadInt64(o["relativeTakeProfit"]),
            Price(o["limitPrice"]),
            Price(o["stopPrice"]));
    }

    /// <summary>
    /// Trendbars: low is an integer in 1/100000, open/high/close are deltas from low; the time is in minutes
    /// (help.ctrader.com, "Symbol data"). Returned oldest first.
    /// </summary>
    public static IReadOnlyList<Bar> Bars(OpenApiMessage res, int digits) =>
        ((res.Payload["trendbar"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Select(b =>
            {
                var low = OpenApiMessage.ReadInt64(b["low"]) ?? 0;
                return new Bar(
                    DateTimeOffset.FromUnixTimeSeconds((OpenApiMessage.ReadInt64(b["utcTimestampInMinutes"]) ?? 0) * 60),
                    Relative(low + (OpenApiMessage.ReadInt64(b["deltaOpen"]) ?? 0), digits),
                    Relative(low + (OpenApiMessage.ReadInt64(b["deltaHigh"]) ?? 0), digits),
                    Relative(low, digits),
                    Relative(low + (OpenApiMessage.ReadInt64(b["deltaClose"]) ?? 0), digits),
                    OpenApiMessage.ReadInt64(b["volume"]) ?? 0);
            })
            .OrderBy(b => b.OpenTime)
            .ToList();

    /// <summary>
    /// Ticks come newest first. The first one holds the real time (ms) and price (1/100000); each next one holds
    /// the difference from the previous one, to be added (Spotware on community.ctrader.com, topic 22598).
    /// </summary>
    public static (IReadOnlyList<Tick> Ticks, bool HasMore) Ticks(OpenApiMessage res, int digits)
    {
        var ticks = new List<Tick>();
        long time = 0, price = 0;
        var first = true;
        foreach (var t in (res.Payload["tickData"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var dt = OpenApiMessage.ReadInt64(t["timestamp"]) ?? 0;
            var dp = OpenApiMessage.ReadInt64(t["tick"]) ?? 0;
            time = first ? dt : time + dt;
            price = first ? dp : price + dp;
            first = false;
            ticks.Add(new Tick(DateTimeOffset.FromUnixTimeMilliseconds(time), Relative(price, digits)));
        }
        ticks.Reverse();
        return (ticks, OpenApiMessage.ReadBool(res.Payload["hasMore"]) ?? false);
    }

    public static decimal Relative(long value, int digits) => Math.Round(value / RelativeScale, digits);

    private static decimal Money(JsonNode? node, int moneyDigits)
    {
        var value = OpenApiMessage.ReadInt64(node) ?? 0;
        var scale = 1m;
        for (var i = 0; i < moneyDigits; i++) scale *= 10;
        return value / scale;
    }

    private static decimal? Price(JsonNode? node) => OpenApiMessage.ReadDouble(node) is { } d && d != 0 ? ToDecimal(d) : null;

    private static decimal ToDecimal(double d) => Math.Round((decimal)d, 8);

    private static string? Text(JsonNode? node) => OpenApiMessage.ReadString(node) is { Length: > 0 } s ? s : null;
}
