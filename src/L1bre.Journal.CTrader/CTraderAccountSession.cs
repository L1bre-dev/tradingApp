using System.Text.Json.Nodes;
using L1bre.Journal.Domain.Trades;
using Microsoft.Extensions.Logging;

namespace L1bre.Journal.CTrader;

/// <summary>
/// An open, authorised connection to one trading account, used by the sync to read its history:
/// executions, the orders behind them, symbols, candles and ticks. Read-only calls only.
/// </summary>
public sealed class CTraderAccountSession : IAsyncDisposable
{
    /// <summary>
    /// Longest range cTrader accepts in one trendbars request, by period (from Spotware's own
    /// OpenAPI.Net library, TrendbarsMaximumTime.cs).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TimeSpan> MaxTrendbarRange = new Dictionary<string, TimeSpan>
    {
        ["M1"] = TimeSpan.FromMilliseconds(302400000),
        ["M5"] = TimeSpan.FromMilliseconds(302400000),
        ["M15"] = TimeSpan.FromMilliseconds(21168000000),
        ["M30"] = TimeSpan.FromMilliseconds(21168000000),
        ["H1"] = TimeSpan.FromMilliseconds(21168000000),
        ["H4"] = TimeSpan.FromMilliseconds(31622400000),
    };

    /// <summary>Tick data requests may not span more than one week.</summary>
    private static readonly TimeSpan MaxTickRange = TimeSpan.FromMilliseconds(604800000);

    private readonly OpenApiConnection connection;
    private readonly ILogger logger;

    public long AccountId { get; }

    private CTraderAccountSession(OpenApiConnection connection, long accountId, ILogger logger)
    {
        this.connection = connection;
        AccountId = accountId;
        this.logger = logger;
    }

    internal static async Task<CTraderAccountSession> OpenAsync(OpenApiConnection connection, long accountId, string accessToken, ILogger logger, CancellationToken ct)
    {
        var session = new CTraderAccountSession(connection, accountId, logger);
        try
        {
            await session.RequestAsync(PayloadTypes.AccountAuthReq, new JsonObject { ["accessToken"] = accessToken }, ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task<TraderInfo> GetTraderAsync(CancellationToken ct)
    {
        var trader = AccountParsing.Trader(await RequestAsync(PayloadTypes.TraderReq, [], ct));
        var assets = await RequestAsync(PayloadTypes.AssetListReq, [], ct);
        return trader with { DepositCurrency = AccountParsing.AssetName(assets, trader.DepositAssetId) };
    }

    public async Task<IReadOnlyList<SymbolInfo>> GetSymbolsAsync(IReadOnlyCollection<long> symbolIds, CancellationToken ct)
    {
        if (symbolIds.Count == 0) return [];
        var names = AccountParsing.SymbolNames(await RequestAsync(PayloadTypes.SymbolsListReq, new JsonObject { ["includeArchivedSymbols"] = true }, ct));
        var details = await RequestAsync(PayloadTypes.SymbolByIdReq,
            new JsonObject { ["symbolId"] = new JsonArray(symbolIds.Select(id => (JsonNode)id).ToArray()) }, ct);
        return AccountParsing.Symbols(details, names);
    }

    /// <summary>Executions between two times (cTrader caps the rows per answer; <see cref="DealPage.HasMore"/> tells when to ask again).</summary>
    public async Task<DealPage> GetDealsAsync(DateTimeOffset from, DateTimeOffset to, int moneyDigits, CancellationToken ct) =>
        AccountParsing.Deals(await RequestAsync(PayloadTypes.DealListReq, new JsonObject
        {
            ["fromTimestamp"] = from.ToUnixTimeMilliseconds(),
            ["toTimestamp"] = to.ToUnixTimeMilliseconds(),
        }, ct), moneyDigits);

    public async Task<OrderInfo> GetOrderAsync(long orderId, CancellationToken ct) =>
        AccountParsing.Order(await RequestAsync(PayloadTypes.OrderDetailsReq, new JsonObject { ["orderId"] = orderId }, ct));

    /// <summary>The last <paramref name="count"/> candles of a timeframe opened at or before <paramref name="to"/>, oldest first.</summary>
    public async Task<IReadOnlyList<Bar>> GetLastBarsAsync(long symbolId, int digits, Timeframe timeframe, DateTimeOffset to, int count, CancellationToken ct) =>
        AccountParsing.Bars(await RequestAsync(PayloadTypes.GetTrendbarsReq, new JsonObject
        {
            ["symbolId"] = symbolId,
            ["period"] = timeframe.Label,
            ["fromTimestamp"] = (to - MaxTrendbarRange[timeframe.Label]).ToUnixTimeMilliseconds(),
            ["toTimestamp"] = to.ToUnixTimeMilliseconds(),
            ["count"] = count,
        }, ct), digits);

    /// <summary>All the candles of a timeframe between two times, oldest first.</summary>
    public async Task<IReadOnlyList<Bar>> GetBarsAsync(long symbolId, int digits, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to - from > MaxTrendbarRange[timeframe.Label]) from = to - MaxTrendbarRange[timeframe.Label];
        return AccountParsing.Bars(await RequestAsync(PayloadTypes.GetTrendbarsReq, new JsonObject
        {
            ["symbolId"] = symbolId,
            ["period"] = timeframe.Label,
            ["fromTimestamp"] = from.ToUnixTimeMilliseconds(),
            ["toTimestamp"] = to.ToUnixTimeMilliseconds(),
        }, ct), digits);
    }

    /// <summary>Bid ticks between two times (inclusive), oldest first. Follows hasMore until the whole range is read.</summary>
    public async Task<IReadOnlyList<Tick>> GetBidTicksAsync(long symbolId, int digits, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to - from > MaxTickRange) from = to - MaxTickRange;
        var all = new List<Tick>();
        var until = to;
        for (var page = 0; page < 50; page++)
        {
            var (ticks, hasMore) = AccountParsing.Ticks(await RequestAsync(PayloadTypes.GetTickDataReq, new JsonObject
            {
                ["symbolId"] = symbolId,
                ["type"] = "BID",
                ["fromTimestamp"] = from.ToUnixTimeMilliseconds(),
                ["toTimestamp"] = until.ToUnixTimeMilliseconds(),
            }, ct), digits);
            all.AddRange(ticks);
            // The answer holds the newest ticks of the range; older ones are asked for next.
            if (!hasMore || ticks.Count == 0) break;
            until = ticks[0].Time.AddMilliseconds(-1);
            if (until < from) break;
        }
        return all.Where(t => t.Time >= from && t.Time <= to).DistinctBy(t => (t.Time, t.Price)).OrderBy(t => t.Time).ToList();
    }

    /// <summary>
    /// Every account request carries the account id. If cTrader says the request rate was exceeded,
    /// wait a little and try again (at most 3 times).
    /// </summary>
    private async Task<OpenApiMessage> RequestAsync(int payloadType, JsonObject payload, CancellationToken ct)
    {
        payload["ctidTraderAccountId"] = AccountId;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await connection.RequestAsync(payloadType, (JsonObject)payload.DeepClone(), ct);
            }
            catch (CTraderException ex) when (attempt < 3 && ex.ErrorCode is "REQUEST_FREQUENCY_EXCEEDED" or "BLOCKED_PAYLOAD_TYPE")
            {
                logger.LogInformation("cTrader asked to slow down ({Code}); retrying message {PayloadType}", ex.ErrorCode, payloadType);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
