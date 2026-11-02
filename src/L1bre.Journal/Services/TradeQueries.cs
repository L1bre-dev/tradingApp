using L1bre.Journal.Data;
using L1bre.Journal.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

public record AccountTradeStats(int Trades, int ToQualify, int ChartsWaiting, int ChartsFailed);

public record AccountTab(Guid Id, long? TraderLogin, string? BrokerName, bool IsLive, string? Currency, int Trades, int ToQualify, DateTimeOffset? LastTradeAt);

/// <summary>Filters of the trades page: show = all | toqualify | win | loss | open.</summary>
public record TradeFilter(string? Show, string? Technique, string? Timeframe);

public record TradePage(List<(Trade Trade, List<string> ChartTimeframes)> Trades, int Total, int Wins, int Losses, decimal NetProfit);

/// <summary>A trade with its candles by timeframe (M1 first, each oldest first) and its neighbours in the account.</summary>
public record TradeView(Trade Trade, Dictionary<string, List<TradeCandle>> Charts, Guid? Older, Guid? Newer, Guid? NextToQualify);

/// <summary>Read-only views of imported trades for the pages. Every query is filtered by owner.</summary>
public class TradeQueries(ApplicationDbContext db)
{
    public async Task<Dictionary<Guid, AccountTradeStats>> StatsAsync(IReadOnlyCollection<Guid> accountIds) =>
        await db.Trades.Where(t => accountIds.Contains(t.TradingAccountId))
            .GroupBy(t => t.TradingAccountId)
            .Select(g => new
            {
                g.Key,
                Trades = g.Count(),
                ToQualify = g.Count(t => t.Status == TradeStatus.ToQualify),
                Waiting = g.Count(t => t.CandlesStatus == CandlesStatus.Pending
                    || (t.CandlesStatus == CandlesStatus.Failed && t.CandlesAttempts < TradeSyncService.MaxChartAttempts)),
                Failed = g.Count(t => t.CandlesStatus == CandlesStatus.Failed && t.CandlesAttempts >= TradeSyncService.MaxChartAttempts),
            })
            .ToDictionaryAsync(x => x.Key, x => new AccountTradeStats(x.Trades, x.ToQualify, x.Waiting, x.Failed));

    /// <summary>The trader's accounts that have trades or are imported, for the account tabs (live first, then by login).</summary>
    public async Task<List<AccountTab>> AccountTabsAsync(string userId)
    {
        var accounts = await db.TradingAccounts.AsNoTracking()
            .Where(a => a.CTraderLink!.UserId == userId && (a.IsSelected || a.Trades.Any()))
            .Select(a => new AccountTab(a.Id, a.TraderLogin, a.BrokerName, a.IsLive, a.DepositCurrency,
                a.Trades.Count(), a.Trades.Count(t => t.Status == TradeStatus.ToQualify), a.Trades.Max(t => (DateTimeOffset?)t.OpenedAt)))
            .ToListAsync();
        return accounts.OrderByDescending(a => a.IsLive).ThenBy(a => a.TraderLogin).ToList();
    }

    /// <summary>One page of an account's trades, newest first, with the filters of the trades page.</summary>
    public async Task<TradePage> ListAsync(string userId, Guid accountId, TradeFilter filter, int page, int pageSize)
    {
        var query = db.Trades.AsNoTracking().Where(t => t.TradingAccountId == accountId && t.TradingAccount!.CTraderLink!.UserId == userId);
        query = filter.Show switch
        {
            "toqualify" => query.Where(t => t.Status == TradeStatus.ToQualify),
            "win" => query.Where(t => t.Outcome == "Win"),
            "loss" => query.Where(t => t.Outcome == "Loss"),
            "open" => query.Where(t => t.Outcome == "Open"),
            _ => query,
        };
        if (!string.IsNullOrEmpty(filter.Technique)) query = query.Where(t => t.Technique == filter.Technique);
        if (!string.IsNullOrEmpty(filter.Timeframe)) query = query.Where(t => t.Timeframe == filter.Timeframe);

        var totals = await query.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            Wins = g.Count(t => t.Outcome == "Win"),
            Losses = g.Count(t => t.Outcome == "Loss"),
            Net = g.Sum(t => t.NetProfit),
        }).SingleOrDefaultAsync();
        var trades = await query.OrderByDescending(t => t.OpenedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = trades.Select(t => t.Id).ToList();
        var charts = await db.TradeCandles.Where(c => ids.Contains(c.TradeId))
            .GroupBy(c => new { c.TradeId, c.Timeframe }).Select(g => g.Key).ToListAsync();
        var byTrade = charts.GroupBy(c => c.TradeId).ToDictionary(g => g.Key, g => g.Select(c => c.Timeframe).OrderBy(Order).ToList());
        return new TradePage(trades.Select(t => (t, byTrade.GetValueOrDefault(t.Id) ?? [])).ToList(),
            totals?.Count ?? 0, totals?.Wins ?? 0, totals?.Losses ?? 0, totals?.Net ?? 0);
    }

    /// <summary>One of the trader's trades with its account and candles, or null if it isn't theirs.</summary>
    public async Task<TradeView?> GetAsync(string userId, Guid tradeId)
    {
        var trade = await db.Trades.AsNoTracking().Include(t => t.TradingAccount).Include(t => t.Candles)
            .SingleOrDefaultAsync(t => t.Id == tradeId && t.TradingAccount!.CTraderLink!.UserId == userId);
        if (trade is null) return null;

        var sameAccount = db.Trades.Where(t => t.TradingAccountId == trade.TradingAccountId);
        // Same order as the list (newest first): "older" is the next row, "newer" the previous one.
        // Two fills of one account in the same millisecond would skip each other here; not worth more code.
        var older = await sameAccount.Where(t => t.OpenedAt < trade.OpenedAt)
            .OrderByDescending(t => t.OpenedAt).Select(t => (Guid?)t.Id).FirstOrDefaultAsync();
        var newer = await sameAccount.Where(t => t.OpenedAt > trade.OpenedAt)
            .OrderBy(t => t.OpenedAt).Select(t => (Guid?)t.Id).FirstOrDefaultAsync();
        var nextToQualify = await NextToQualifyAsync(trade.TradingAccountId, trade.Id);

        var charts = trade.Candles.GroupBy(c => c.Timeframe).OrderBy(g => Order(g.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.OpenTime).ToList());
        return new TradeView(trade, charts, older, newer, nextToQualify);
    }

    /// <summary>The oldest trade of the account still to qualify, other than <paramref name="except"/>.</summary>
    public Task<Guid?> NextToQualifyAsync(Guid accountId, Guid except) =>
        db.Trades.Where(t => t.TradingAccountId == accountId && t.Status == TradeStatus.ToQualify && t.Id != except)
            .OrderBy(t => t.OpenedAt).Select(t => (Guid?)t.Id).FirstOrDefaultAsync();

    /// <summary>M1 first, H4 last.</summary>
    internal static int Order(string label) => Timeframes.All.Select(t => t.Label).ToList().IndexOf(label);

    /// <summary>For the admin page: the sync state of every selected account.</summary>
    public Task<List<TradingAccount>> SelectedAccountsAsync() =>
        db.TradingAccounts.AsNoTracking().Include(a => a.CTraderLink).ThenInclude(l => l!.User)
            .Where(a => a.IsSelected).OrderBy(a => a.CTraderLink!.User!.Email).ThenBy(a => a.TraderLogin).ToListAsync();
}
