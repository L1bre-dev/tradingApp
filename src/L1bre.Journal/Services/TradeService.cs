using L1bre.Journal.Data;
using L1bre.Journal.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

/// <summary>Changes a trader makes to their own trades.</summary>
public class TradeService(ApplicationDbContext db, TimeProvider clock, ILogger<TradeService> logger)
{
    /// <summary>
    /// Sets the technique and timeframe of one of the trader's trades (a manual one to qualify, or a correction).
    /// Returns why it was refused, or null when saved.
    /// </summary>
    public async Task<string?> QualifyAsync(string userId, Guid tradeId, string? technique, string? timeframe)
    {
        var trade = await db.Trades.SingleOrDefaultAsync(t => t.Id == tradeId && t.TradingAccount!.CTraderLink!.UserId == userId);
        if (trade is null) return "This trade was not found.";

        var stored = await db.TradeCandles.Where(c => c.TradeId == tradeId).Select(c => c.Timeframe).Distinct().ToListAsync();
        if (Qualification.CheckChoice(technique, timeframe, stored) is { } error) return error;

        trade.Technique = technique;
        trade.Timeframe = Timeframes.TryParse(timeframe)!.Label;
        trade.Status = TradeStatus.Qualified;
        trade.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        logger.LogInformation("Trade {TradeId} qualified as {Technique} {Timeframe}", tradeId, trade.Technique, trade.Timeframe);
        return null;
    }
}
