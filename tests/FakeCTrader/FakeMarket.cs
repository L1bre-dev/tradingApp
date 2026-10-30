using System.Text.Json.Nodes;

namespace FakeCTrader;

/// <summary>
/// The fake account history: one symbol (XAUUSD, 2 digits) with a made-up but deterministic price,
/// and a few positions covering the cases the journal must handle. Answers the account-level Open API
/// messages in the documented JSON shapes (enum values written as names, like "BUY").
/// </summary>
public sealed class FakeMarket
{
    public const long SymbolId = 41;
    public const string SymbolName = "XAUUSD";
    public const long UsdAssetId = 15;

    /// <summary>Executions per deal list answer before hasMore is set (small, to exercise paging).</summary>
    public int DealPageSize { get; set; } = 4;

    /// <summary>Ticks per tick data answer before hasMore is set.</summary>
    public int TickPageSize { get; set; } = 25;

    public DateTimeOffset RegisteredAt { get; }
    public List<JsonObject> Deals { get; } = [];
    public Dictionary<long, JsonObject> Orders { get; } = [];

    private long nextDealId = 9000;

    public FakeMarket(DateTimeOffset now)
    {
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-3);
        RegisteredAt = day.AddDays(-5);

        // 1. Frame M15, Buy limit filled at 09:07:20.250, closed in profit at 10:02.
        AddOrder(501, "LIMIT", "Frame", "M15", sl: 3995.00, tp: 4010.00);
        AddDeal(501, positionId: 1, "BUY", 100, day.AddHours(9).AddMinutes(7).AddSeconds(20).AddMilliseconds(250), 4001.20, commission: -35);
        AddOrder(502, "MARKET", null, null);
        AddDeal(502, positionId: 1, "SELL", 100, day.AddHours(10).AddMinutes(2), 4010.00, commission: -35, gross: 880, closeCommission: -70);

        // 2. Manual Sell, no label: to qualify, all six timeframes. Closed at a loss.
        AddOrder(511, "MARKET", null, null, relativeSl: 500000, relativeTp: 1000000);
        AddDeal(511, positionId: 2, "SELL", 200, day.AddHours(13).AddMinutes(33).AddSeconds(5), 4003.50, commission: -70);
        AddOrder(512, "MARKET", null, null);
        AddDeal(512, positionId: 2, "BUY", 200, day.AddHours(14), 4005.00, commission: -70, gross: -300, closeCommission: -140, swap: -12);

        // 3. Frame H1 closed in two parts the next day.
        AddOrder(521, "LIMIT", "Frame", "H1", sl: 3990.00, tp: 4020.00);
        AddDeal(521, positionId: 3, "BUY", 200, day.AddDays(1).AddHours(8).AddMinutes(41).AddSeconds(1), 3999.00, commission: -70);
        AddOrder(522, "MARKET", null, null);
        AddDeal(522, positionId: 3, "SELL", 100, day.AddDays(1).AddHours(9).AddMinutes(10), 4004.00, commission: -35, gross: 500, closeCommission: -70);
        AddOrder(523, "MARKET", null, null);
        AddDeal(523, positionId: 3, "SELL", 100, day.AddDays(1).AddHours(9).AddMinutes(50), 3997.00, commission: -35, gross: -200, closeCommission: -70);

        // 4. A rejected execution: must not become a trade.
        AddOrder(531, "MARKET", null, null);
        AddDeal(531, positionId: 4, "BUY", 100, day.AddDays(1).AddHours(11), 4000.00, commission: 0, status: "REJECTED", filled: 0);

        // 5. Frame M5 still open.
        AddOrder(541, "LIMIT", "Frame", "M5", sl: 3998.00, tp: 4004.00);
        AddDeal(541, positionId: 5, "BUY", 100, day.AddDays(2).AddHours(15).AddMinutes(0).AddSeconds(12), 4000.40, commission: -35);
    }

    /// <summary>Adds an execution (tests use it to simulate a new trade between two syncs).</summary>
    public void AddDeal(long orderId, long positionId, string side, long volume, DateTimeOffset executed, double price, long commission,
        long? gross = null, long closeCommission = 0, long swap = 0, string status = "FILLED", long? filled = null)
    {
        var deal = new JsonObject
        {
            ["dealId"] = nextDealId++,
            ["orderId"] = orderId,
            ["positionId"] = positionId,
            ["volume"] = volume,
            ["filledVolume"] = filled ?? volume,
            ["symbolId"] = SymbolId,
            ["createTimestamp"] = executed.AddMilliseconds(-150).ToUnixTimeMilliseconds(),
            ["executionTimestamp"] = executed.ToUnixTimeMilliseconds(),
            ["executionPrice"] = price,
            ["tradeSide"] = side,
            ["dealStatus"] = status,
            ["commission"] = commission,
            ["moneyDigits"] = 2,
        };
        if (gross is not null)
        {
            deal["closePositionDetail"] = new JsonObject
            {
                ["entryPrice"] = 0,
                ["grossProfit"] = gross,
                ["swap"] = swap,
                ["commission"] = closeCommission,
                ["balance"] = 1_000_000,
                ["closedVolume"] = volume,
                ["moneyDigits"] = 2,
            };
        }
        Deals.Add(deal);
    }

    public void AddOrder(long orderId, string type, string? label, string? comment, double? sl = null, double? tp = null, long? relativeSl = null, long? relativeTp = null)
    {
        var tradeData = new JsonObject { ["symbolId"] = SymbolId, ["volume"] = 100, ["tradeSide"] = "BUY" };
        if (label is not null) tradeData["label"] = label;
        if (comment is not null) tradeData["comment"] = comment;
        var order = new JsonObject { ["orderId"] = orderId, ["tradeData"] = tradeData, ["orderType"] = type, ["orderStatus"] = "ORDER_STATUS_FILLED" };
        if (sl is not null) order["stopLoss"] = sl;
        if (tp is not null) order["takeProfit"] = tp;
        if (relativeSl is not null) order["relativeStopLoss"] = relativeSl;
        if (relativeTp is not null) order["relativeTakeProfit"] = relativeTp;
        Orders[orderId] = order;
    }

    // ---- Price model: one price per second, in 1/100000 of a dollar. ----

    /// <summary>Bid at a given second: around 4000.00, moving by cents, never the same twice in a row.</summary>
    public static long PriceAt(long unixSecond) => 400_000_000 + (unixSecond / 60 % 97) * 10_000 + (unixSecond % 60) * 100 - 3_000;

    public static JsonObject Trendbar(long openMinute, int minutes)
    {
        long open = PriceAt(openMinute * 60), close = PriceAt((openMinute + minutes) * 60 - 1);
        long high = long.MinValue, low = long.MaxValue;
        for (var s = openMinute * 60; s < (openMinute + minutes) * 60; s++)
        {
            var p = PriceAt(s);
            if (p > high) high = p;
            if (p < low) low = p;
        }
        return new JsonObject
        {
            ["volume"] = minutes * 60,
            ["low"] = low,
            ["deltaOpen"] = open - low,
            ["deltaClose"] = close - low,
            ["deltaHigh"] = high - low,
            ["utcTimestampInMinutes"] = openMinute,
        };
    }

    public static readonly Dictionary<string, int> PeriodMinutes = new() { ["M1"] = 1, ["M5"] = 5, ["M15"] = 15, ["M30"] = 30, ["H1"] = 60, ["H4"] = 240 };
    private static readonly Dictionary<int, string> PeriodNumbers = new() { [1] = "M1", [5] = "M5", [7] = "M15", [8] = "M30", [9] = "H1", [10] = "H4" };

    public JsonObject? Answer(int type, JsonObject payload, long accountId) => type switch
    {
        2121 => Ok(2122, new JsonObject
        {
            ["trader"] = new JsonObject
            {
                ["ctidTraderAccountId"] = accountId, ["balance"] = 1_000_000, ["depositAssetId"] = UsdAssetId, ["moneyDigits"] = 2,
                ["registrationTimestamp"] = RegisteredAt.ToUnixTimeMilliseconds(),
            },
        }),
        2112 => Ok(2113, new JsonObject
        {
            ["asset"] = new JsonArray(
                new JsonObject { ["assetId"] = UsdAssetId, ["name"] = "USD", ["displayName"] = "USD" },
                new JsonObject { ["assetId"] = 16, ["name"] = "XAU" }),
        }),
        2114 => Ok(2115, new JsonObject
        {
            ["symbol"] = new JsonArray(
                new JsonObject { ["symbolId"] = SymbolId, ["symbolName"] = SymbolName, ["enabled"] = true },
                new JsonObject { ["symbolId"] = 1, ["symbolName"] = "EURUSD", ["enabled"] = true }),
        }),
        2116 => Ok(2117, new JsonObject
        {
            ["symbol"] = new JsonArray((payload["symbolId"] as JsonArray ?? []).Select(id => (JsonNode)new JsonObject
            {
                ["symbolId"] = id!.GetValue<long>(), ["digits"] = id.GetValue<long>() == SymbolId ? 2 : 5,
                ["pipPosition"] = id.GetValue<long>() == SymbolId ? 1 : 4, ["lotSize"] = 10000,
            }).ToArray()),
        }),
        2133 => DealList(payload),
        2181 => payload["orderId"]?.GetValue<long>() is { } orderId && Orders.TryGetValue(orderId, out var order)
            ? Ok(2182, new JsonObject { ["order"] = order.DeepClone() })
            : Error("ORDER_NOT_FOUND", "Order not found"),
        2137 => Trendbars(payload),
        2145 => TickData(payload),
        _ => null,
    };

    private JsonObject DealList(JsonObject payload)
    {
        var from = payload["fromTimestamp"]?.GetValue<long>() ?? 0;
        var to = payload["toTimestamp"]?.GetValue<long>() ?? long.MaxValue;
        if (to - from > 604_800_000) return Error("INVALID_REQUEST", "Deal list range is limited to one week in this fake");
        var matching = Deals.Where(d => d["executionTimestamp"]!.GetValue<long>() is var t && t >= from && t <= to)
            .OrderBy(d => d["executionTimestamp"]!.GetValue<long>()).ToList();
        return Ok(2134, new JsonObject
        {
            ["deal"] = new JsonArray(matching.Take(DealPageSize).Select(d => (JsonNode)d.DeepClone()).ToArray()),
            ["hasMore"] = matching.Count > DealPageSize,
        });
    }

    private static JsonObject Trendbars(JsonObject payload)
    {
        var periodNode = payload["period"]!.AsValue();
        var period = periodNode.TryGetValue<string>(out var name) ? name : PeriodNumbers[periodNode.GetValue<int>()];
        var minutes = PeriodMinutes[period];
        var fromMinute = payload["fromTimestamp"]!.GetValue<long>() / 60_000;
        var toMinute = payload["toTimestamp"]!.GetValue<long>() / 60_000;
        var count = payload["count"]?.GetValue<int>() ?? int.MaxValue;

        // Bars open on multiples of their length since 1970 (true for M1-H4 in UTC).
        var lastOpen = toMinute / minutes * minutes;
        var bars = new List<JsonNode>();
        for (var open = lastOpen; open >= fromMinute && bars.Count < count; open -= minutes)
            bars.Add(Trendbar(open, minutes));
        bars.Reverse();
        return Ok(2138, new JsonObject { ["period"] = period, ["trendbar"] = new JsonArray(bars.ToArray()), ["symbolId"] = SymbolId });
    }

    /// <summary>One Bid tick per second, newest first, delta-encoded after the first one, paged with hasMore.</summary>
    private JsonObject TickData(JsonObject payload)
    {
        var from = payload["fromTimestamp"]!.GetValue<long>();
        var to = payload["toTimestamp"]!.GetValue<long>();
        if (to - from > 604_800_000) return Error("INVALID_REQUEST", "Tick data range is limited to one week");
        var ticks = new JsonArray();
        long prevTime = 0, prevPrice = 0;
        var count = 0;
        var hasMore = false;
        for (var second = to / 1000; second * 1000 >= from; second--)
        {
            if (second * 1000 > to) continue;
            if (count == TickPageSize) { hasMore = true; break; }
            long time = second * 1000, price = PriceAt(second);
            ticks.Add(count == 0
                ? new JsonObject { ["timestamp"] = time, ["tick"] = price }
                : new JsonObject { ["timestamp"] = time - prevTime, ["tick"] = price - prevPrice });
            prevTime = time;
            prevPrice = price;
            count++;
        }
        return Ok(2146, new JsonObject { ["tickData"] = ticks, ["hasMore"] = hasMore });
    }

    private static JsonObject Ok(int type, JsonObject payload) => new() { ["payloadType"] = type, ["payload"] = payload };

    private static JsonObject Error(string code, string description) =>
        Ok(2142, new JsonObject { ["errorCode"] = code, ["description"] = description });
}
