using L1bre.Journal.CTrader;
using L1bre.Journal.Data;
using L1bre.Journal.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

/// <summary>
/// Imports the trades of one selected trading account: new executions since the last sync (the whole history
/// the first time), rebuilt into trades, qualified from their opening order, then their 54-candle charts.
/// </summary>
public class TradeSyncService(
    ApplicationDbContext db,
    CTraderApi api,
    CTraderLinkService links,
    TimeProvider clock,
    ILogger<TradeSyncService> logger)
{
    /// <summary>cTrader examples read deal history one week at a time.</summary>
    private static readonly TimeSpan DealWindow = TimeSpan.FromDays(7);

    /// <summary>Each sync re-reads the last hour, in case an execution was recorded late. Duplicates are ignored.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromHours(1);

    /// <summary>When cTrader gives no registration date, the first import starts this far back.</summary>
    private static readonly TimeSpan DefaultHistory = TimeSpan.FromDays(365);

    /// <summary>Charts built per account and per run, so a long first import doesn't hold the job for hours.</summary>
    public const int ChartsPerRun = 40;

    public const int MaxChartAttempts = 3;

    public async Task SyncAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.TradingAccounts.Include(a => a.CTraderLink).SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account?.CTraderLink is null || !account.IsSelected || !account.IsAvailable) return;
        var link = account.CTraderLink;

        try
        {
            var token = await links.GetAccessTokenAsync(link, ct);
            await using var session = await api.OpenAccountAsync(token, account.CtidTraderAccountId, account.IsLive, ct);

            if (account.MoneyDigits is null)
            {
                var trader = await session.GetTraderAsync(ct);
                account.MoneyDigits = trader.MoneyDigits;
                account.DepositCurrency = trader.DepositCurrency;
                account.HistoryStart = trader.RegisteredAt ?? clock.GetUtcNow() - DefaultHistory;
            }

            var syncedUntil = clock.GetUtcNow();
            var newDeals = await ImportDealsAsync(account, session, syncedUntil, ct);
            await UpdateTradesAsync(account, session, newDeals, ct);
            account.SyncedUntil = syncedUntil;
            await db.SaveChangesAsync(ct);

            await BuildChartsAsync(account, session, ct);

            account.LastSyncAt = clock.GetUtcNow();
            account.LastSyncError = null;
            account.LastSyncErrorAt = null;
        }
        catch (Exception ex) when (ex is CTraderException or HttpRequestException or System.Net.WebSockets.WebSocketException or FormatException)
        {
            if (ex is CTraderException { IsTokenProblem: true }) links.MarkNeedsRelink(link, ex.Message);
            account.LastSyncError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            account.LastSyncErrorAt = clock.GetUtcNow();
            logger.LogWarning("Sync of account {AccountId} failed: {Error}", account.Id, ex.Message);
        }
        account.SyncRequestedAt = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reads executions from where the last sync stopped, week by week, following hasMore. Returns the new ones.</summary>
    private async Task<List<DealRecord>> ImportDealsAsync(TradingAccount account, CTraderAccountSession session, DateTimeOffset until, CancellationToken ct)
    {
        var from = account.SyncedUntil is { } synced ? synced - Overlap : account.HistoryStart!.Value;
        var received = new Dictionary<long, DealRecord>();

        for (var windowStart = from; windowStart < until; windowStart += DealWindow)
        {
            var windowEnd = windowStart + DealWindow < until ? windowStart + DealWindow : until;
            var cursor = windowStart;
            for (var page = 0; page < 500; page++)
            {
                var result = await session.GetDealsAsync(cursor, windowEnd, account.MoneyDigits!.Value, ct);
                foreach (var deal in result.Deals) received.TryAdd(deal.Fill.DealId, deal);
                if (!result.HasMore || result.LatestExecution is null) break;
                // Continue after the latest execution received; always move forward, even if a page is all one millisecond.
                var next = result.LatestExecution.Value.AddMilliseconds(1);
                cursor = next > cursor ? next : cursor.AddMilliseconds(1);
                if (cursor > windowEnd) break;
            }
        }

        var ids = received.Keys.ToList();
        var known = (await db.TradeDeals.Where(d => d.TradingAccountId == account.Id && ids.Contains(d.DealId)).Select(d => d.DealId).ToListAsync(ct)).ToHashSet();
        var fresh = received.Values.Where(d => !known.Contains(d.Fill.DealId)).ToList();
        foreach (var deal in fresh) db.TradeDeals.Add(ToEntity(account.Id, deal));
        return fresh;
    }

    /// <summary>Creates or updates the trade of every position that received an execution.</summary>
    private async Task UpdateTradesAsync(TradingAccount account, CTraderAccountSession session, List<DealRecord> newDeals, CancellationToken ct)
    {
        var positionIds = newDeals.Select(d => d.Fill.PositionId).Distinct().ToList();
        if (positionIds.Count == 0) return;

        var stored = await db.TradeDeals.Where(d => d.TradingAccountId == account.Id && positionIds.Contains(d.PositionId)).ToListAsync(ct);
        var fills = stored.Select(ToFill).Concat(newDeals.Select(d => d.Fill)).GroupBy(f => f.PositionId).ToDictionary(g => g.Key, g => g.ToList());
        var trades = await db.Trades.Where(t => t.TradingAccountId == account.Id && positionIds.Contains(t.PositionId)).ToDictionaryAsync(t => t.PositionId, ct);

        var positions = fills.Values.Select(PositionBuilder.Build).OfType<Position>().ToList();
        var newSymbols = positions.Where(p => !trades.ContainsKey(p.PositionId)).Select(p => p.SymbolId).Distinct().ToList();
        var symbols = (await session.GetSymbolsAsync(newSymbols, ct)).ToDictionary(s => s.SymbolId);
        var now = clock.GetUtcNow();

        foreach (var position in positions)
        {
            if (!trades.TryGetValue(position.PositionId, out var trade))
            {
                trade = await NewTradeAsync(account, session, position, symbols.GetValueOrDefault(position.SymbolId), now, ct);
                db.Trades.Add(trade);
            }
            ApplyPosition(trade, position, now);
        }
    }

    private async Task<Trade> NewTradeAsync(TradingAccount account, CTraderAccountSession session, Position position, SymbolInfo? symbol, DateTimeOffset now, CancellationToken ct)
    {
        OrderInfo? order = null;
        try
        {
            order = await session.GetOrderAsync(position.OpeningOrderId, ct);
        }
        catch (CTraderException ex) when (!ex.IsTokenProblem)
        {
            // Without its order the trade is still imported, as a trade to qualify.
            logger.LogWarning("Order {OrderId} of position {PositionId} not readable: {Error}", position.OpeningOrderId, position.PositionId, ex.Message);
        }

        var digits = symbol?.Digits ?? 5;
        var qualification = Qualification.FromOrder(order?.Label, order?.Comment);
        var (stopLoss, takeProfit) = StopLevels.FromOrder(position.Direction, position.EntryPrice, digits,
            order?.StopLoss, order?.TakeProfit, order?.RelativeStopLoss, order?.RelativeTakeProfit);

        return new Trade
        {
            Id = Guid.NewGuid(),
            TradingAccountId = account.Id,
            PositionId = position.PositionId,
            SymbolId = position.SymbolId,
            SymbolName = symbol?.Name ?? $"#{position.SymbolId}",
            Digits = digits,
            PipSize = StrategyPip.FromDigits(digits),
            LotSizeCents = symbol?.LotSizeCents,
            OpeningOrderId = position.OpeningOrderId,
            OrderType = order?.OrderType,
            Label = order?.Label,
            Comment = order?.Comment,
            Origin = qualification.Origin.ToString(),
            Technique = qualification.Technique,
            Timeframe = qualification.Timeframe,
            Status = qualification.IsQualified ? TradeStatus.Qualified : TradeStatus.ToQualify,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            CandlesStatus = CandlesStatus.Pending,
            ImportedAt = now,
        };
    }

    private static void ApplyPosition(Trade trade, Position position, DateTimeOffset now)
    {
        trade.Direction = position.Direction.ToString();
        trade.OpenedAt = position.OpenedAt;
        trade.EntryPrice = position.EntryPrice;
        trade.VolumeCents = position.VolumeCents;
        trade.ClosedVolumeCents = position.ClosedVolumeCents;
        trade.ClosedAt = position.ClosedAt;
        trade.ExitPrice = position.ExitPrice;
        trade.GrossProfit = position.GrossProfit;
        trade.Swap = position.Swap;
        trade.Commission = position.Commission;
        trade.ConversionFee = position.ConversionFee;
        trade.NetProfit = position.NetProfit;
        trade.Outcome = position.Outcome.ToString();
        trade.Pips = position.IsClosed ? position.Pips(trade.PipSize) : null;
        trade.UpdatedAt = now;
    }

    /// <summary>
    /// Builds the charts of trades still waiting for them (oldest first, a limited number per run).
    /// One failure doesn't stop the others; a trade is retried on the next runs, up to 3 times.
    /// </summary>
    private async Task BuildChartsAsync(TradingAccount account, CTraderAccountSession session, CancellationToken ct)
    {
        var waiting = await db.Trades
            .Where(t => t.TradingAccountId == account.Id
                && (t.CandlesStatus == CandlesStatus.Pending || (t.CandlesStatus == CandlesStatus.Failed && t.CandlesAttempts < MaxChartAttempts)))
            .OrderBy(t => t.OpenedAt).Take(ChartsPerRun).ToListAsync(ct);

        foreach (var trade in waiting)
        {
            try
            {
                var candles = await FetchChartsAsync(trade, session, ct);
                await db.TradeCandles.Where(c => c.TradeId == trade.Id).ExecuteDeleteAsync(ct);
                db.TradeCandles.AddRange(candles);
                trade.CandlesStatus = CandlesStatus.Stored;
                trade.CandlesError = null;
            }
            catch (Exception ex) when (ex is CTraderException { IsTokenProblem: false } or InvalidOperationException or FormatException)
            {
                trade.CandlesAttempts++;
                trade.CandlesStatus = CandlesStatus.Failed;
                trade.CandlesError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning("Chart of trade {TradeId} (position {PositionId}) failed: {Error}", trade.Id, trade.PositionId, ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The 54 candles of every timeframe to store, the last one cut at the fill: one trendbar request per timeframe,
    /// one M1 request covering the longest fill candle, one tick request for the fill minute.
    /// </summary>
    private static async Task<List<TradeCandle>> FetchChartsAsync(Trade trade, CTraderAccountSession session, CancellationToken ct)
    {
        var qualification = new Qualification(Enum.Parse<TradeOrigin>(trade.Origin), trade.Technique, trade.Timeframe);
        var timeframes = qualification.TimeframesToStore;
        var fill = trade.OpenedAt;

        var bars = new Dictionary<string, IReadOnlyList<Bar>>();
        foreach (var tf in timeframes)
            bars[tf.Label] = await session.GetLastBarsAsync(trade.SymbolId, trade.Digits, tf, fill, TradeChart.CandleCount, ct);

        var longest = timeframes.Max(t => t.Duration);
        var m1 = longest > Timeframes.M1.Duration
            ? await session.GetBarsAsync(trade.SymbolId, trade.Digits, Timeframes.M1, fill - longest, fill, ct)
            : [];
        var fillMinute = new DateTimeOffset(fill.Ticks - fill.Ticks % TimeSpan.TicksPerMinute, fill.Offset);
        var ticks = await session.GetBidTicksAsync(trade.SymbolId, trade.Digits, fillMinute, fill, ct);

        return timeframes.SelectMany(tf => TradeChart.Build(bars[tf.Label], tf.Duration, fill, m1, ticks)
                .Select(c => new TradeCandle
                {
                    TradeId = trade.Id,
                    Timeframe = tf.Label,
                    OpenTime = c.OpenTime,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume,
                    IsCut = c.IsCut,
                }))
            .ToList();
    }

    private static TradeDeal ToEntity(Guid accountId, DealRecord deal) => new()
    {
        TradingAccountId = accountId,
        DealId = deal.Fill.DealId,
        PositionId = deal.Fill.PositionId,
        OrderId = deal.Fill.OrderId,
        SymbolId = deal.Fill.SymbolId,
        Side = deal.Fill.Side.ToString(),
        VolumeCents = deal.Fill.VolumeCents,
        ExecutedAt = deal.Fill.ExecutedAt,
        Price = deal.Fill.Price,
        Commission = deal.Fill.Commission,
        IsClosing = deal.Fill.IsClosing,
        ClosedVolumeCents = deal.Fill.Close?.ClosedVolumeCents ?? 0,
        GrossProfit = deal.Fill.Close?.GrossProfit ?? 0,
        Swap = deal.Fill.Close?.Swap ?? 0,
        CloseCommission = deal.Fill.Close?.Commission ?? 0,
        ConversionFee = deal.Fill.Close?.ConversionFee ?? 0,
        RawJson = deal.RawJson,
    };

    private static Fill ToFill(TradeDeal d) => new(
        d.DealId, d.PositionId, d.OrderId, d.SymbolId, Enum.Parse<Direction>(d.Side), d.VolumeCents, d.ExecutedAt, d.Price, d.Commission,
        d.IsClosing ? new CloseDetail(0, d.ClosedVolumeCents, d.GrossProfit, d.Swap, d.CloseCommission, d.ConversionFee) : null);
}
