using L1bre.Journal.Data;
using L1bre.Journal.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

public record AccountTradeStats(int Trades, int ToQualify, int ChartsWaiting, int ChartsFailed);

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

    /// <summary>The trader's trades, newest first, with their account and the timeframes of their stored candles.</summary>
    public async Task<List<(Trade Trade, List<string> ChartTimeframes)>> ListAsync(string userId, int max)
    {
        var trades = await db.Trades.AsNoTracking().Include(t => t.TradingAccount).ThenInclude(a => a!.CTraderLink)
            .Where(t => t.TradingAccount!.CTraderLink!.UserId == userId)
            .OrderByDescending(t => t.OpenedAt).Take(max).ToListAsync();
        var ids = trades.Select(t => t.Id).ToList();
        var charts = await db.TradeCandles.Where(c => ids.Contains(c.TradeId))
            .GroupBy(c => new { c.TradeId, c.Timeframe }).Select(g => g.Key).ToListAsync();
        var byTrade = charts.GroupBy(c => c.TradeId).ToDictionary(g => g.Key, g => g.Select(c => c.Timeframe).OrderBy(Order).ToList());
        return trades.Select(t => (t, byTrade.GetValueOrDefault(t.Id) ?? [])).ToList();
    }

    /// <summary>M1 first, H4 last.</summary>
    private static int Order(string label) => Timeframes.All.Select(t => t.Label).ToList().IndexOf(label);

    /// <summary>For the admin page: the sync state of every selected account.</summary>
    public Task<List<TradingAccount>> SelectedAccountsAsync() =>
        db.TradingAccounts.AsNoTracking().Include(a => a.CTraderLink).ThenInclude(l => l!.User)
            .Where(a => a.IsSelected).OrderBy(a => a.CTraderLink!.User!.Email).ThenBy(a => a.TraderLogin).ToListAsync();
}
