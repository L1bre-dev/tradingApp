namespace L1bre.Journal.Domain.Trades;

/// <summary>Stop loss and take profit of a trade, as set on its opening order.</summary>
public static class StopLevels
{
    /// <summary>
    /// cTrader gives either absolute prices (what the Frame cBot sets) or distances from the entry in 1/100000
    /// of the price (relativeStopLoss / relativeTakeProfit). Absolute wins when both exist.
    /// </summary>
    public static (decimal? StopLoss, decimal? TakeProfit) FromOrder(
        Direction direction, decimal entry, int digits, decimal? stopLoss, decimal? takeProfit, long? relativeStopLoss, long? relativeTakeProfit)
    {
        var sign = direction == Direction.Buy ? 1 : -1;
        decimal? Relative(long? distance, int side) =>
            distance is > 0 ? Math.Round(entry + side * sign * distance.Value / 100000m, digits) : null;
        return (stopLoss ?? Relative(relativeStopLoss, -1), takeProfit ?? Relative(relativeTakeProfit, +1));
    }
}
