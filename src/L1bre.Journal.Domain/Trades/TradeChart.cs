namespace L1bre.Journal.Domain.Trades;

/// <summary>One candle. Prices are in the symbol's price (already divided by 100000 and rounded to its digits).</summary>
public sealed record Bar(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

/// <summary>One historical Bid price.</summary>
public sealed record Tick(DateTimeOffset Time, decimal Price);

/// <summary>A chart candle as stored for a trade. The last one of a chart is cut at the fill moment.</summary>
public sealed record ChartCandle(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume, bool IsCut);

/// <summary>
/// Builds the chart of a trade for one timeframe: 54 candles, the last being the candle in which the order was filled,
/// cut at the fill moment, so the chart shows the market exactly as it was when the trade was taken (design v3, section 4).
/// </summary>
public static class TradeChart
{
    public const int CandleCount = 54;

    /// <param name="bars">Candles of the timeframe up to the fill (any order; later ones are ignored).</param>
    /// <param name="duration">The timeframe's candle length.</param>
    /// <param name="fillTime">When the position was opened.</param>
    /// <param name="m1Bars">M1 candles from at least the fill candle's open up to the fill minute.</param>
    /// <param name="fillMinuteTicks">Bid ticks of the fill minute, up to at least the fill time.</param>
    public static IReadOnlyList<ChartCandle> Build(
        IEnumerable<Bar> bars, TimeSpan duration, DateTimeOffset fillTime, IEnumerable<Bar> m1Bars, IEnumerable<Tick> fillMinuteTicks)
    {
        var known = bars.Where(b => b.OpenTime <= fillTime).DistinctBy(b => b.OpenTime).OrderBy(b => b.OpenTime).ToList();
        if (known.Count == 0) throw new InvalidOperationException("No candle before the fill.");

        // The candle containing the fill is normally the last one returned. If the fill fell in a period
        // with no candle yet (no Bid tick in it), its opening time is computed from the last known candle.
        var last = known[^1];
        DateTimeOffset fillOpen;
        Bar? fillBar;
        if (fillTime < last.OpenTime + duration)
        {
            fillOpen = last.OpenTime;
            fillBar = last;
            known.RemoveAt(known.Count - 1);
        }
        else
        {
            var periods = (fillTime - last.OpenTime).Ticks / duration.Ticks;
            fillOpen = last.OpenTime + TimeSpan.FromTicks(duration.Ticks * periods);
            fillBar = null;
        }

        var before = known.TakeLast(CandleCount - 1)
            .Select(b => new ChartCandle(b.OpenTime, b.Open, b.High, b.Low, b.Close, b.Volume, IsCut: false));
        var priceIfEmpty = fillBar?.Open ?? known.LastOrDefault()?.Close;
        return [.. before, Cut(fillOpen, fillTime, m1Bars, fillMinuteTicks, priceIfEmpty)];
    }

    /// <summary>
    /// The fill candle up to the fill moment: open = the candle's real open, high/low = extremes from that open
    /// up to the fill, close = the last Bid at the fill. Built from the complete M1 candles before the fill minute
    /// plus the ticks of the fill minute.
    /// </summary>
    /// <param name="priceIfEmpty">Used when no price at all exists between the candle open and the fill.</param>
    public static ChartCandle Cut(DateTimeOffset candleOpen, DateTimeOffset fillTime, IEnumerable<Bar> m1Bars, IEnumerable<Tick> ticks, decimal? priceIfEmpty = null)
    {
        var fillMinute = new DateTimeOffset(fillTime.Ticks - fillTime.Ticks % TimeSpan.TicksPerMinute, fillTime.Offset);
        var minutes = m1Bars.Where(b => b.OpenTime >= candleOpen && b.OpenTime < fillMinute)
            .DistinctBy(b => b.OpenTime).OrderBy(b => b.OpenTime).ToList();
        var from = candleOpen > fillMinute ? candleOpen : fillMinute;
        var lastTicks = ticks.Where(t => t.Time >= from && t.Time <= fillTime).OrderBy(t => t.Time).ToList();

        if (minutes.Count == 0 && lastTicks.Count == 0)
        {
            // No price at all between the candle open and the fill: a flat candle at the last known price.
            var price = priceIfEmpty ?? throw new InvalidOperationException("No price between the candle open and the fill.");
            return new ChartCandle(candleOpen, price, price, price, price, 0, IsCut: true);
        }

        var open = minutes.Count > 0 ? minutes[0].Open : lastTicks[0].Price;
        var close = lastTicks.Count > 0 ? lastTicks[^1].Price : minutes[^1].Close;
        var highs = minutes.Select(b => b.High).Concat(lastTicks.Select(t => t.Price));
        var lows = minutes.Select(b => b.Low).Concat(lastTicks.Select(t => t.Price));
        return new ChartCandle(candleOpen, open, highs.Max(), lows.Min(), close, minutes.Sum(b => b.Volume) + lastTicks.Count, IsCut: true);
    }
}
