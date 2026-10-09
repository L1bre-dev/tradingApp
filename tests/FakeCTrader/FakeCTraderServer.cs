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
///   WS   /            the JSON Open API: application auth, profile, account list, token refresh
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

            JsonObject? answer = type switch
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
                _ => Error("UNSUPPORTED_MESSAGE", $"Fake server doesn't know {type}"),
            };
            if (answer is null) continue;

            if (id is not null) answer["clientMsgId"] = id;
            await socket.SendAsync(Encoding.UTF8.GetBytes(answer.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

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
