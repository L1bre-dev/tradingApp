using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace FakeCTrader;

/// <summary>
/// A stand-in for cTrader, for tests and local runs only (the cloud build machine can't reach the real one).
/// It mimics what the documentation describes:
///   GET  /authorize   the cTrader ID login page: approves at once and redirects back with ?code=
///   GET  /apps/token  exchanges the code for tokens
///   GET  /test/close-open-position  closes the open seeded position (manual testing of a later sync)
///   WS   /            the JSON Open API: application auth, profile, account list, token refresh,
///                     and the account history of <see cref="FakeMarket"/> (deals, orders, symbols, candles, ticks)
/// </summary>
public sealed class FakeCTraderServer : IAsyncDisposable
{
    public const string ClientId = "fake-client-id";
    public const string ClientSecret = "fake-client-secret";
    public const string GoodCode = "good-code";
    public const long CtidUserId = 4242;

    private readonly WebApplication app;
    private int tokenGeneration = 1;

    /// <summary>Tokens currently valid. A refresh replaces both, like the real service.</summary>
    public string AccessToken => $"access-{tokenGeneration}";
    public string RefreshToken => $"refresh-{tokenGeneration}";

    /// <summary>Accounts returned for the token. Tests can change them.</summary>
    public List<JsonObject> Accounts { get; } =
    [
        new() { ["ctidTraderAccountId"] = 1001, ["isLive"] = false, ["traderLogin"] = "5551001", ["brokerTitleShort"] = "Fusion Markets" },
        new() { ["ctidTraderAccountId"] = "1002", ["isLive"] = true, ["traderLogin"] = 5551002, ["brokerTitleShort"] = "Fusion Markets" },
    ];

    /// <summary>When false, the authorize page doesn't send state back (cTrader doesn't document it).</summary>
    public bool EchoState { get; set; } = true;

    /// <summary>The history every fake account answers with. Tests can add deals to it.</summary>
    public FakeMarket Market { get; } = new(DateTimeOffset.UtcNow);

    public string BaseUrl { get; }
    public string WebSocketUrl => BaseUrl.Replace("http://", "ws://");
    public List<int> ReceivedPayloadTypes { get; } = [];

    private FakeCTraderServer(WebApplication app, string baseUrl)
    {
        this.app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeCTraderServer> StartAsync(int port = 0)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        FakeCTraderServer? server = null;

        app.UseWebSockets();
        app.MapGet("/authorize", (HttpContext ctx) => server!.Authorize(ctx));
        app.MapGet("/apps/token", (HttpContext ctx) => server!.Token(ctx));
        // Manual testing only: closes the open seeded position (5) one minute ago, as if it hit its TP between two syncs.
        app.MapGet("/test/close-open-position", () =>
        {
            server!.Market.AddDeal(542, positionId: 5, "SELL", 100, DateTimeOffset.UtcNow.AddMinutes(-1), 4004.00, commission: -35, gross: 360, closeCommission: -70);
            return "closed";
        });
        app.Map("/", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await server!.ServeAsync(socket);
        });

        await app.StartAsync();
        var address = app.Urls.First();
        server = new FakeCTraderServer(app, address);
        return server;
    }

    private IResult Authorize(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        if (q["client_id"] != ClientId || q["scope"] != "accounts")
            return Results.BadRequest("wrong client_id or scope");
        var back = new Dictionary<string, string?> { ["code"] = GoodCode };
        if (EchoState) back["state"] = q["state"];
        return Results.Redirect(QueryHelpers.AddQueryString(q["redirect_uri"]!, back));
    }

    private IResult Token(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        if (q["client_id"] != ClientId || q["client_secret"] != ClientSecret)
            return Results.Json(new { errorCode = "INVALID_CLIENT", description = "Unknown client" });
        if (q["grant_type"] != "authorization_code" || q["code"] != GoodCode)
            return Results.Json(new { errorCode = "ACCESS_DENIED", description = "Invalid code" });
        return Results.Json(new
        {
            accessToken = AccessToken,
            tokenType = "bearer",
            expiresIn = 2628000,
            refreshToken = RefreshToken,
            errorCode = (string?)null,
            description = (string?)null,
        });
    }

    private async Task ServeAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var appAuthorized = false;
        var authorizedAccounts = new HashSet<long>();
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try { result = await socket.ReceiveAsync(buffer, CancellationToken.None); }
            catch (WebSocketException) { return; }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return;
            }

            var request = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!.AsObject();
            var type = request["payloadType"]!.GetValue<int>();
            var id = request["clientMsgId"]?.GetValue<string>();
            var payload = request["payload"]?.AsObject() ?? [];
            lock (ReceivedPayloadTypes) ReceivedPayloadTypes.Add(type);

            JsonObject? answer;
            try
            {
                answer = Answer(type, payload, ref appAuthorized, authorizedAccounts);
            }
            catch (Exception ex)
            {
                // A bug in the fake must show in the test output, not as a silent disconnect.
                answer = Error("FAKE_SERVER_BUG", ex.ToString());
            }
            if (answer is null) continue;

            if (id is not null) answer["clientMsgId"] = id;
            await socket.SendAsync(Encoding.UTF8.GetBytes(answer.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    private JsonObject? Answer(int type, JsonObject payload, ref bool appAuthorized, HashSet<long> authorizedAccounts)
    {
        return type switch
        {
                51 => null,
                2100 when payload["clientId"]?.GetValue<string>() == ClientId && payload["clientSecret"]?.GetValue<string>() == ClientSecret
                    => Reply(2101, [], ref appAuthorized),
                2100 => Error("CH_CLIENT_AUTH_FAILURE", "clientId or clientSecret is incorrect"),
                _ when !appAuthorized => Error("CH_CLIENT_NOT_AUTHENTICATED", "Application is not authorized"),
                2151 when payload["accessToken"]?.GetValue<string>() == AccessToken
                    => Ok(2152, new JsonObject { ["profile"] = new JsonObject { ["userId"] = CtidUserId.ToString() } }),
                2149 when payload["accessToken"]?.GetValue<string>() == AccessToken
                    => Ok(2150, new JsonObject
                    {
                        ["accessToken"] = AccessToken,
                        ["permissionScope"] = "SCOPE_VIEW",
                        ["ctidTraderAccount"] = new JsonArray(Accounts.Select(a => (JsonNode)a.DeepClone()).ToArray()),
                    }),
                2173 when payload["refreshToken"]?.GetValue<string>() == RefreshToken => Refresh(),
                2151 or 2149 or 2173 => Error("CH_ACCESS_TOKEN_INVALID", "Invalid access token"),
                2102 => AccountAuth(payload, authorizedAccounts),
                _ when payload["ctidTraderAccountId"] is { } account && !authorizedAccounts.Contains(account.GetValue<long>())
                    => Error("CH_CTID_TRADER_ACCOUNT_NOT_AUTHORIZED", "Trading account is not authorized"),
                _ when Market.Answer(type, payload, payload["ctidTraderAccountId"]?.GetValue<long>() ?? 0) is { } marketAnswer => marketAnswer,
                _ => Error("UNSUPPORTED_MESSAGE", $"Fake server doesn't know {type}"),
            };
    }

    private JsonObject AccountAuth(JsonObject payload, HashSet<long> authorized)
    {
        if (payload["accessToken"]?.GetValue<string>() != AccessToken) return Error("CH_ACCESS_TOKEN_INVALID", "Invalid access token");
        var account = payload["ctidTraderAccountId"]?.GetValue<long>() ?? 0;
        if (!Accounts.Any(a => OpenApiLong(a["ctidTraderAccountId"]) == account)) return Error("ACCOUNT_NOT_AUTHORIZED", "Account not reachable with this token");
        authorized.Add(account);
        return Ok(2103, new JsonObject { ["ctidTraderAccountId"] = account });
    }

    private static long OpenApiLong(JsonNode? node) => long.Parse(node!.ToString(), System.Globalization.CultureInfo.InvariantCulture);

    private JsonObject Refresh()
    {
        Interlocked.Increment(ref tokenGeneration);
        return Ok(2174, new JsonObject
        {
            ["accessToken"] = AccessToken,
            ["tokenType"] = "bearer",
            ["expiresIn"] = 2628000,
            ["refreshToken"] = RefreshToken,
        });
    }

    private static JsonObject Reply(int type, JsonObject payload, ref bool appAuthorized)
    {
        appAuthorized = true;
        return Ok(type, payload);
    }

    private static JsonObject Ok(int type, JsonObject payload) => new() { ["payloadType"] = type, ["payload"] = payload };

    private static JsonObject Error(string code, string description) =>
        Ok(2142, new JsonObject { ["errorCode"] = code, ["description"] = description });

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
