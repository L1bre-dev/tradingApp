using FakeCTrader;
using L1bre.Journal.CTrader;
using L1bre.Journal.Domain.Trades;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace L1bre.Journal.CTrader.Tests;

/// <summary>The account-level calls used by the sync, against the fake cTrader.</summary>
public class AccountSessionTests : IAsyncLifetime
{
    private FakeCTraderServer fake = null!;
    private CTraderApi api = null!;
    private readonly HttpClient http = new();

    public async Task InitializeAsync()
    {
        fake = await FakeCTraderServer.StartAsync();
        api = new CTraderApi(http, Options.Create(new CTraderOptions
        {
            ClientId = FakeCTraderServer.ClientId,
            ClientSecret = FakeCTraderServer.ClientSecret,
            DemoEndpoint = fake.WebSocketUrl + "/",
            LiveEndpoint = fake.WebSocketUrl + "/",
        }), TimeProvider.System, NullLogger<CTraderApi>.Instance);
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await fake.DisposeAsync();
    }

    private Task<CTraderAccountSession> Open() => api.OpenAccountAsync(fake.AccessToken, 1001, isLive: false);

    [Fact]
    public async Task Trader_currency_and_symbol_format()
    {
        await using var session = await Open();
        var trader = await session.GetTraderAsync(default);
        Assert.Equal(2, trader.MoneyDigits);
        Assert.Equal("USD", trader.DepositCurrency);
        Assert.Equal(fake.Market.RegisteredAt, trader.RegisteredAt);

        var symbol = Assert.Single(await session.GetSymbolsAsync([FakeMarket.SymbolId], default));
        Assert.Equal(new SymbolInfo(FakeMarket.SymbolId, "XAUUSD", 2, 1, 10000), symbol);
    }

    [Fact]
    public async Task Deals_are_paged_and_rejected_ones_skipped()
    {
        await using var session = await Open();
        var from = fake.Market.RegisteredAt;
        var to = from.AddDays(7);

        var first = await session.GetDealsAsync(from, to, 2, default);
        Assert.True(first.HasMore);
        Assert.Equal(4, first.Deals.Count);

        var open = first.Deals[0].Fill;
        Assert.Equal(Direction.Buy, open.Side);
        Assert.Equal(1, open.PositionId);
        Assert.Equal(4001.20m, open.Price);
        Assert.Equal(-0.35m, open.Commission);
        Assert.Null(open.Close);
        Assert.Equal(250, open.ExecutedAt.Millisecond);

        var close = first.Deals[1].Fill;
        Assert.Equal(Direction.Sell, close.Side);
        Assert.Equal(new CloseDetail(0, 100, 8.80m, 0, -0.70m, 0), close.Close);

        var second = await session.GetDealsAsync(first.LatestExecution!.Value.AddMilliseconds(1), to, 2, default);
        Assert.False(second.HasMore);
        Assert.DoesNotContain(second.Deals, d => d.Fill.PositionId == 4);
        Assert.Contains("\"dealStatus\":\"FILLED\"", second.Deals[0].RawJson);
    }

    [Fact]
    public async Task Order_gives_label_comment_and_stops()
    {
        await using var session = await Open();
        var frame = await session.GetOrderAsync(501, default);
        Assert.Equal(new OrderInfo(501, "LIMIT", "Frame", "M15", 3995.00m, 4010.00m, null, null, null, null), frame);

        var manual = await session.GetOrderAsync(511, default);
        Assert.Null(manual.Label);
        Assert.Equal(500000, manual.RelativeStopLoss);
    }

    [Fact]
    public async Task Last_bars_end_with_the_candle_containing_the_time()
    {
        await using var session = await Open();
        var at = new DateTimeOffset(2026, 10, 28, 9, 7, 20, TimeSpan.Zero);
        var bars = await session.GetLastBarsAsync(FakeMarket.SymbolId, 2, Timeframes.M15, at, 54, default);

        Assert.Equal(54, bars.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), bars[^1].OpenTime);
        Assert.Equal(TimeSpan.FromMinutes(15), bars[^1].OpenTime - bars[^2].OpenTime);
        var minute = bars[^1].OpenTime.ToUnixTimeSeconds();
        Assert.Equal(AccountParsing.Relative(FakeMarket.PriceAt(minute), 2), bars[^1].Open);
    }

    [Fact]
    public async Task Ticks_are_decoded_from_deltas_across_pages()
    {
        await using var session = await Open();
        var from = new DateTimeOffset(2026, 10, 28, 9, 7, 0, TimeSpan.Zero);
        var to = from.AddSeconds(59);
        var ticks = await session.GetBidTicksAsync(FakeMarket.SymbolId, 2, from, to, default);

        Assert.Equal(60, ticks.Count); // more than one page of 25
        Assert.Equal(from, ticks[0].Time);
        Assert.All(ticks, t => Assert.Equal(AccountParsing.Relative(FakeMarket.PriceAt(t.Time.ToUnixTimeSeconds()), 2), t.Price));
    }

    [Fact]
    public async Task Revoked_token_is_refused_at_account_auth()
    {
        var ex = await Assert.ThrowsAsync<CTraderException>(() => api.OpenAccountAsync("old-token", 1001, isLive: false));
        Assert.True(ex.IsTokenProblem);
    }
}
