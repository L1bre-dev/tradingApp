using L1bre.Journal.Domain.Trades;

namespace L1bre.Journal.Domain.Tests;

public class PositionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 9, 15, 30, TimeSpan.Zero);

    private static Fill Open(long deal, Direction side, long volume, decimal price, int minutes = 0, long position = 7) =>
        new(deal, position, OrderId: 100 + deal, SymbolId: 41, side, volume, T0.AddMinutes(minutes), price, Commission: -0.35m, Close: null);

    private static Fill CloseFill(long deal, Direction side, long volume, decimal price, decimal gross, int minutes, decimal commission = -0.70m, decimal swap = 0, long position = 7) =>
        new(deal, position, OrderId: 100 + deal, SymbolId: 41, side, volume, T0.AddMinutes(minutes), price, Commission: -0.35m,
            new CloseDetail(EntryPrice: 0, volume, gross, swap, commission, ConversionFee: 0));

    [Fact]
    public void Buy_closed_in_profit_is_a_win_with_net_after_charges()
    {
        var position = PositionBuilder.Build([Open(1, Direction.Buy, 100, 4000.00m), CloseFill(2, Direction.Sell, 100, 4005.00m, gross: 5.00m, minutes: 30)])!;

        Assert.Equal(Direction.Buy, position.Direction);
        Assert.Equal(T0, position.OpenedAt);
        Assert.Equal(101, position.OpeningOrderId);
        Assert.Equal(4000.00m, position.EntryPrice);
        Assert.Equal(4005.00m, position.ExitPrice);
        Assert.Equal(T0.AddMinutes(30), position.ClosedAt);
        Assert.Equal(4.30m, position.NetProfit);
        Assert.Equal(Outcome.Win, position.Outcome);
        Assert.Equal(500m, position.Pips(StrategyPip.FromDigits(2)));
    }

    [Fact]
    public void Breakeven_paying_commission_is_a_loss()
    {
        var position = PositionBuilder.Build([Open(1, Direction.Sell, 100, 4000m), CloseFill(2, Direction.Buy, 100, 4000m, gross: 0m, minutes: 5)])!;
        Assert.Equal(-0.70m, position.NetProfit);
        Assert.Equal(Outcome.Loss, position.Outcome);
        Assert.Equal(0m, position.Pips(0.01m));
    }

    [Fact]
    public void Sell_pips_are_positive_when_price_falls()
    {
        var position = PositionBuilder.Build([Open(1, Direction.Sell, 100, 4000m), CloseFill(2, Direction.Buy, 100, 3995m, gross: 5m, minutes: 5)])!;
        Assert.Equal(500m, position.Pips(0.01m));
    }

    [Fact]
    public void Partial_closes_make_one_trade_closed_by_the_last_execution()
    {
        var fills = new[]
        {
            Open(1, Direction.Buy, 200, 4000m),
            CloseFill(2, Direction.Sell, 100, 4002m, gross: 2m, minutes: 10),
            CloseFill(3, Direction.Sell, 100, 4006m, gross: 6m, minutes: 20),
        };
        var position = PositionBuilder.Build(fills)!;

        Assert.True(position.IsClosed);
        Assert.Equal(T0.AddMinutes(20), position.ClosedAt);
        Assert.Equal(4004m, position.ExitPrice);
        Assert.Equal(8m, position.GrossProfit);
        Assert.Equal(-1.40m, position.Commission);
    }

    [Fact]
    public void Half_closed_position_is_still_open()
    {
        var position = PositionBuilder.Build([Open(1, Direction.Buy, 200, 4000m), CloseFill(2, Direction.Sell, 100, 4002m, gross: 2m, minutes: 10)])!;
        Assert.False(position.IsClosed);
        Assert.Equal(Outcome.Open, position.Outcome);
        Assert.Null(position.ClosedAt);
    }

    [Fact]
    public void Scaling_in_averages_the_entry_and_keeps_the_first_fill_time()
    {
        var position = PositionBuilder.Build([Open(2, Direction.Buy, 300, 4003m, minutes: 3), Open(1, Direction.Buy, 100, 3999m)])!;
        Assert.Equal(T0, position.OpenedAt);
        Assert.Equal(400, position.VolumeCents);
        Assert.Equal(4002m, position.EntryPrice);
    }

    [Fact]
    public void Same_execution_twice_counts_once()
    {
        var open = Open(1, Direction.Buy, 100, 4000m);
        Assert.Equal(100, PositionBuilder.Build([open, open])!.VolumeCents);
    }

    [Fact]
    public void Unknown_opening_execution_gives_no_trade_yet()
    {
        Assert.Null(PositionBuilder.Build([CloseFill(2, Direction.Sell, 100, 4005m, gross: 5m, minutes: 30)]));
    }

    [Theory]
    [InlineData(2, "0.01")]
    [InlineData(5, "0.0001")]
    [InlineData(3, "0.01")]
    [InlineData(1, "0.1")]
    public void Strategy_pip_follows_the_cbot_rule(int digits, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), StrategyPip.FromDigits(digits));
}

public class QualificationTests
{
    [Fact]
    public void Frame_order_is_qualified_from_label_and_comment()
    {
        var q = Qualification.FromOrder("Frame", "M15");
        Assert.Equal(new Qualification(TradeOrigin.Bot, "Frame", "M15"), q);
        Assert.True(q.IsQualified);
        Assert.Equal(["M15"], q.TimeframesToStore.Select(t => t.Label));
    }

    [Fact]
    public void Label_and_comment_are_read_loosely()
    {
        Assert.Equal(new Qualification(TradeOrigin.Bot, "Frame", "H1"), Qualification.FromOrder(" frame ", "h1"));
    }

    [Fact]
    public void Manual_trade_waits_for_the_trader_and_keeps_all_six_timeframes()
    {
        var q = Qualification.FromOrder(null, null);
        Assert.Equal(TradeOrigin.Manual, q.Origin);
        Assert.False(q.IsQualified);
        Assert.Equal(["M1", "M5", "M15", "M30", "H1", "H4"], q.TimeframesToStore.Select(t => t.Label));
    }

    [Fact]
    public void Bot_trade_without_a_timeframe_is_not_qualified()
    {
        var q = Qualification.FromOrder("Frame", "my note");
        Assert.Equal("Frame", q.Technique);
        Assert.Null(q.Timeframe);
        Assert.False(q.IsQualified);
        Assert.Equal(6, q.TimeframesToStore.Count);
    }

    [Fact]
    public void Unknown_bot_label_has_no_technique()
    {
        var q = Qualification.FromOrder("SomeOtherBot", "M15");
        Assert.Equal(TradeOrigin.Bot, q.Origin);
        Assert.Null(q.Technique);
        Assert.False(q.IsQualified);
    }
}

public class TradeChartTests
{
    private static readonly DateTimeOffset Open0 = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private static Bar B(DateTimeOffset time, decimal o, decimal h, decimal l, decimal c, long v = 10) => new(time, o, h, l, c, v);

    /// <summary>M15 candles from 60 candles before Open0 up to the candle opening at Open0 (the fill candle).</summary>
    private static List<Bar> M15Bars() =>
        Enumerable.Range(-60, 61).Select(i => B(Open0.AddMinutes(15 * i), 4000 + i, 4010 + i, 3990 + i, 4001 + i)).ToList();

    [Fact]
    public void Chart_has_54_candles_ending_with_the_cut_fill_candle()
    {
        var fill = Open0.AddMinutes(7).AddSeconds(20);
        var m1 = Enumerable.Range(0, 7).Select(i => B(Open0.AddMinutes(i), 4100 + i, 4105 + i, 4098 + i, 4101 + i)).ToList();
        var ticks = new[] { new Tick(fill.AddSeconds(-15), 4108m), new Tick(fill.AddSeconds(-5), 4112m), new Tick(fill, 4109m), new Tick(fill.AddSeconds(1), 4200m) };

        var chart = TradeChart.Build(M15Bars(), TimeSpan.FromMinutes(15), fill, m1, ticks);

        Assert.Equal(54, chart.Count);
        Assert.Equal(Open0.AddMinutes(-15 * 53), chart[0].OpenTime);
        Assert.All(chart.Take(53), c => Assert.False(c.IsCut));
        Assert.Equal(Open0.AddMinutes(-15), chart[52].OpenTime);

        var cut = chart[^1];
        Assert.True(cut.IsCut);
        Assert.Equal(Open0, cut.OpenTime);
        Assert.Equal(4100m, cut.Open);       // real open of the candle (first M1)
        Assert.Equal(4112m, cut.High);       // highest of M1 highs (4111) and ticks up to the fill (4112)
        Assert.Equal(4098m, cut.Low);
        Assert.Equal(4109m, cut.Close);      // last Bid at the fill; the tick after the fill is ignored
        Assert.Equal(7 * 10 + 3, cut.Volume);
    }

    [Fact]
    public void Candles_after_the_fill_are_never_used()
    {
        var fill = Open0.AddMinutes(7);
        var bars = M15Bars();
        bars.Add(B(Open0.AddMinutes(15), 9999, 9999, 9999, 9999));
        var chart = TradeChart.Build(bars, TimeSpan.FromMinutes(15), fill, [], [new Tick(fill, 4100m)]);
        Assert.Equal(Open0, chart[^1].OpenTime);
        Assert.DoesNotContain(chart, c => c.High == 9999);
    }

    [Fact]
    public void M1_fill_candle_is_built_from_the_fill_minute_ticks_only()
    {
        var fill = Open0.AddSeconds(42);
        var m1 = Enumerable.Range(-60, 61).Select(i => B(Open0.AddMinutes(i), 4000, 4001, 3999, 4000)).ToList();
        var ticks = new[] { new Tick(Open0.AddSeconds(3), 4002m), new Tick(Open0.AddSeconds(30), 3997m), new Tick(Open0.AddSeconds(41), 3998m) };

        var chart = TradeChart.Build(m1, TimeSpan.FromMinutes(1), fill, m1, ticks);
        var cut = chart[^1];
        Assert.Equal(new ChartCandle(Open0, 4002m, 4002m, 3997m, 3998m, 3, IsCut: true), cut);
        Assert.Equal(54, chart.Count);
    }

    [Fact]
    public void Fill_in_a_period_without_candle_gets_its_own_aligned_candle()
    {
        // Last candle opened at 08:45 (no candle at 09:00 yet, e.g. the market was quiet); fill at 09:20:10 → M15 candle 09:15.
        var bars = M15Bars().Where(b => b.OpenTime < Open0).ToList();
        var fill = Open0.AddMinutes(20).AddSeconds(10);
        var chart = TradeChart.Build(bars, TimeSpan.FromMinutes(15), fill, [], [new Tick(fill.AddSeconds(-2), 4050m)]);

        Assert.Equal(Open0.AddMinutes(15), chart[^1].OpenTime);
        Assert.Equal(Open0.AddMinutes(-15), chart[^2].OpenTime);
        Assert.Equal(54, chart.Count);
        Assert.Equal(4050m, chart[^1].Close);
    }

    [Fact]
    public void Fill_without_any_price_since_the_previous_candle_is_flat_at_its_close()
    {
        var bars = M15Bars().Where(b => b.OpenTime < Open0).ToList();
        var chart = TradeChart.Build(bars, TimeSpan.FromMinutes(15), Open0.AddMinutes(20), [], []);
        Assert.Equal(new ChartCandle(Open0.AddMinutes(15), 4000m, 4000m, 4000m, 4000m, 0, IsCut: true), chart[^1]);
    }

    [Fact]
    public void Short_history_gives_fewer_candles()
    {
        var bars = M15Bars().TakeLast(10).ToList();
        Assert.Equal(10, TradeChart.Build(bars, TimeSpan.FromMinutes(15), Open0.AddMinutes(1), [], [new Tick(Open0, 1m)]).Count);
    }

    [Fact]
    public void No_price_up_to_the_fill_uses_the_candle_open()
    {
        var cut = TradeChart.Cut(Open0, Open0.AddSeconds(1), [], [], priceIfEmpty: 4000m);
        Assert.Equal(new ChartCandle(Open0, 4000m, 4000m, 4000m, 4000m, 0, IsCut: true), cut);
    }
}

public class StopLevelTests
{
    [Fact]
    public void Absolute_levels_are_kept() =>
        Assert.Equal((3995m, 4010m), StopLevels.FromOrder(Direction.Buy, 4001.2m, 2, 3995m, 4010m, 500000, 500000));

    [Fact]
    public void Relative_levels_are_placed_on_the_right_side()
    {
        Assert.Equal((3998.5m, 4013.5m), StopLevels.FromOrder(Direction.Buy, 4003.5m, 2, null, null, 500000, 1000000));
        Assert.Equal((4008.5m, 3993.5m), StopLevels.FromOrder(Direction.Sell, 4003.5m, 2, null, null, 500000, 1000000));
    }

    [Fact]
    public void No_protection_gives_nothing() =>
        Assert.Equal((null, null), StopLevels.FromOrder(Direction.Sell, 4003.5m, 2, null, null, null, 0));
}
