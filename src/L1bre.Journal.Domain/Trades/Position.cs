namespace L1bre.Journal.Domain.Trades;

public enum Outcome { Open, Win, Loss }

/// <summary>
/// A cTrader position rebuilt from its executions: one position = one trade in the journal,
/// however many partial fills or partial closes it had.
/// </summary>
public sealed record Position(
    long PositionId,
    long SymbolId,
    Direction Direction,
    long OpeningOrderId,
    DateTimeOffset OpenedAt,
    decimal EntryPrice,
    long VolumeCents,
    long ClosedVolumeCents,
    DateTimeOffset? ClosedAt,
    decimal? ExitPrice,
    decimal GrossProfit,
    decimal Swap,
    decimal Commission,
    decimal ConversionFee)
{
    public bool IsClosed => ClosedAt is not null;

    /// <summary>Gross profit plus swap, commission and conversion fee (charges are negative).</summary>
    public decimal NetProfit => GrossProfit + Swap + Commission + ConversionFee;

    /// <summary>Win = net P&amp;L above zero (PE, 2026-10-08): a breakeven trade that paid commission is a loss.</summary>
    public Outcome Outcome => !IsClosed ? Outcome.Open : NetProfit > 0 ? Outcome.Win : Outcome.Loss;

    /// <summary>Distance from entry to exit in strategy pips, positive when the price moved in the trade's favour.</summary>
    public decimal? Pips(decimal pipSize) =>
        ExitPrice is null || pipSize <= 0 ? null
        : (ExitPrice.Value - EntryPrice) * (Direction == Direction.Buy ? 1 : -1) / pipSize;
}

public static class PositionBuilder
{
    /// <summary>
    /// Builds the position from all the executions known for it. Returns null while its opening execution
    /// is unknown (e.g. it happened before the imported period).
    /// </summary>
    public static Position? Build(IEnumerable<Fill> fills)
    {
        var all = fills.DistinctBy(f => f.DealId).ToList();
        var opening = all.Where(f => !f.IsClosing && f.VolumeCents > 0).OrderBy(f => f.ExecutedAt).ThenBy(f => f.DealId).ToList();
        if (opening.Count == 0) return null;

        var closing = all.Where(f => f.IsClosing).OrderBy(f => f.ExecutedAt).ThenBy(f => f.DealId).ToList();
        var first = opening[0];
        var volume = opening.Sum(f => f.VolumeCents);
        var closedVolume = closing.Sum(ClosedVolume);
        var isClosed = closing.Count > 0 && closedVolume >= volume;

        return new Position(
            first.PositionId,
            first.SymbolId,
            first.Side,
            first.OrderId,
            first.ExecutedAt,
            WeightedPrice(opening.Select(f => (f.Price, f.VolumeCents))),
            volume,
            closedVolume,
            isClosed ? closing[^1].ExecutedAt : null,
            closing.Count > 0 ? WeightedPrice(closing.Select(f => (f.Price, ClosedVolume(f)))) : null,
            closing.Sum(f => f.Close!.GrossProfit),
            closing.Sum(f => f.Close!.Swap),
            closing.Sum(f => f.Close!.Commission),
            closing.Sum(f => f.Close!.ConversionFee));
    }

    private static long ClosedVolume(Fill f) => f.Close!.ClosedVolumeCents > 0 ? f.Close.ClosedVolumeCents : f.VolumeCents;

    private static decimal WeightedPrice(IEnumerable<(decimal Price, long Volume)> parts)
    {
        var list = parts.ToList();
        var total = list.Sum(p => p.Volume);
        return total == 0 ? list[0].Price : list.Sum(p => p.Price * p.Volume) / total;
    }
}
