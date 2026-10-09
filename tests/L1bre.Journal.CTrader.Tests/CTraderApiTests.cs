using FakeCTrader;
using L1bre.Journal.CTrader;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace L1bre.Journal.CTrader.Tests;

/// <summary>The real client against the fake cTrader server, over real HTTP and WebSocket.</summary>
public class CTraderApiTests : IAsyncLifetime
{
    private FakeCTraderServer fake = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        fake = await FakeCTraderServer.StartAsync();
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await fake.DisposeAsync();
    }

    private CTraderApi Api(string secret = FakeCTraderServer.ClientSecret) => new(http, Options.Create(new CTraderOptions
    {
        ClientId = FakeCTraderServer.ClientId,
        ClientSecret = secret,
        AuthorizeUrl = fake.BaseUrl + "/authorize",
        TokenUrl = fake.BaseUrl + "/apps/token",
        DemoEndpoint = fake.WebSocketUrl + "/",
    }), TimeProvider.System, NullLogger<CTraderApi>.Instance);

    [Fact]
    public async Task Full_link_flow_authorize_exchange_identity()
    {
        var api = Api();
        const string redirect = "https://fta.trading/ctrader/callback";

        var authorize = await http.GetAsync(api.BuildAuthorizeUrl(redirect, "s1"));
        var back = authorize.Headers.Location!.ToString();
        Assert.StartsWith(redirect + "?code=", back);
        Assert.Contains("state=s1", back);

        var tokens = await api.ExchangeCodeAsync(FakeCTraderServer.GoodCode, redirect);
        Assert.Equal(fake.AccessToken, tokens.AccessToken);
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29));

        var identity = await api.GetIdentityAsync(tokens.AccessToken);
        Assert.Equal(FakeCTraderServer.CtidUserId, identity.CtidUserId);
        Assert.Equal([1001L, 1002L], identity.Accounts.Select(a => a.CtidTraderAccountId));
        Assert.Equal([false, true], identity.Accounts.Select(a => a.IsLive));
        Assert.Equal([2100, 2151, 2149], fake.ReceivedPayloadTypes.Where(t => t != 51));
    }

    [Fact]
    public async Task Wrong_code_is_refused()
    {
        var ex = await Assert.ThrowsAsync<CTraderException>(() => Api().ExchangeCodeAsync("expired-code", "https://x/cb"));
        Assert.Equal("ACCESS_DENIED", ex.ErrorCode);
    }

    [Fact]
    public async Task Token_endpoint_answering_without_json_is_a_clear_error()
    {
        // The fake's "/" answers plain HTTP with an empty 400, like a proxy or maintenance page would.
        var api = new CTraderApi(http, Options.Create(new CTraderOptions
        {
            ClientId = FakeCTraderServer.ClientId,
            ClientSecret = FakeCTraderServer.ClientSecret,
            TokenUrl = fake.BaseUrl + "/",
        }), TimeProvider.System, NullLogger<CTraderApi>.Instance);

        var ex = await Assert.ThrowsAsync<CTraderException>(() => api.ExchangeCodeAsync(FakeCTraderServer.GoodCode, "https://x/cb"));
        Assert.Equal("BAD_RESPONSE", ex.ErrorCode);
    }

    [Fact]
    public async Task Wrong_application_secret_is_refused()
    {
        var ex = await Assert.ThrowsAsync<CTraderException>(() => Api("wrong").GetIdentityAsync(fake.AccessToken));
        Assert.Equal("CH_CLIENT_AUTH_FAILURE", ex.ErrorCode);
    }

    [Fact]
    public async Task Revoked_token_is_a_token_problem()
    {
        var ex = await Assert.ThrowsAsync<CTraderException>(() => Api().GetIdentityAsync("revoked"));
        Assert.True(ex.IsTokenProblem);
    }

    [Fact]
    public async Task Refresh_gives_new_tokens_and_old_ones_stop_working()
    {
        var api = Api();
        var oldAccess = fake.AccessToken;

        var tokens = await api.RefreshAsync(fake.RefreshToken);

        Assert.NotEqual(oldAccess, tokens.AccessToken);
        Assert.Equal(fake.AccessToken, tokens.AccessToken);
        await Assert.ThrowsAsync<CTraderException>(() => api.GetIdentityAsync(oldAccess));
        Assert.Equal(FakeCTraderServer.CtidUserId, (await api.GetIdentityAsync(tokens.AccessToken)).CtidUserId);
    }

    [Fact]
    public async Task Not_configured_is_reported_clearly()
    {
        var api = new CTraderApi(http, Options.Create(new CTraderOptions()), TimeProvider.System, NullLogger<CTraderApi>.Instance);
        var ex = await Assert.ThrowsAsync<CTraderException>(() => api.GetIdentityAsync("x"));
        Assert.Equal("NOT_CONFIGURED", ex.ErrorCode);
    }
}
