using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace L1bre.Journal.CTrader;

/// <summary>
/// What the app needs from cTrader for linking accounts:
/// the OAuth pages (log in with a cTrader ID, get tokens) and three Open API calls
/// (who is this token, which accounts does it open, refresh it).
/// Everything is read-only: the app asks for the "accounts" scope only.
/// </summary>
public class CTraderApi(HttpClient http, IOptions<CTraderOptions> options, TimeProvider clock, ILogger<CTraderApi> logger)
{
    private CTraderOptions Options => options.Value;

    /// <summary>
    /// The cTrader page where the trader logs in and allows read-only access.
    /// <paramref name="state"/> is not documented by cTrader; it is sent anyway and checked if it comes back.
    /// </summary>
    public string BuildAuthorizeUrl(string redirectUri, string state) =>
        QueryHelpers.AddQueryString(Options.AuthorizeUrl, new Dictionary<string, string?>
        {
            ["client_id"] = Options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Options.Scope,
            ["product"] = "web",
            ["state"] = state,
        });

    /// <summary>Exchanges the code received on the redirect URI (valid one minute) for tokens.</summary>
    public async Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString(Options.TokenUrl, new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret,
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, ct);
        JsonObject? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        }
        catch (System.Text.Json.JsonException)
        {
            // e.g. an HTML error page from a proxy or maintenance.
            throw new CTraderException("BAD_RESPONSE", $"HTTP {(int)response.StatusCode} from the token endpoint, not JSON.");
        }
        if (body is null)
            throw new CTraderException("EMPTY_RESPONSE", $"HTTP {(int)response.StatusCode} without body from the token endpoint.");
        return ParseTokenResponse(body, clock.GetUtcNow());
    }

    /// <summary>The documented token answer: accessToken, refreshToken, expiresIn, and errorCode/description on failure.</summary>
    public static TokenSet ParseTokenResponse(JsonObject body, DateTimeOffset now)
    {
        var errorCode = Text(body, "errorCode", "error");
        if (!string.IsNullOrWhiteSpace(errorCode))
            throw new CTraderException(errorCode, Text(body, "description", "error_description"));

        var access = Text(body, "accessToken", "access_token") ?? throw new CTraderException("NO_ACCESS_TOKEN", "The token endpoint returned no access token.");
        var refresh = Text(body, "refreshToken", "refresh_token") ?? throw new CTraderException("NO_REFRESH_TOKEN", "The token endpoint returned no refresh token.");
        var expiresIn = OpenApiMessage.ReadInt64(body["expiresIn"] ?? body["expires_in"]) ?? 0;
        return new TokenSet(access, refresh, now.AddSeconds(expiresIn));
    }

    /// <summary>The cTrader ID behind a token and the trading accounts it opens (demo and live).</summary>
    public async Task<CTraderIdentity> GetIdentityAsync(string accessToken, CancellationToken ct = default)
    {
        await using var connection = await OpenAuthorizedAsync(ct);

        var profile = await connection.RequestAsync(PayloadTypes.GetCtidProfileByTokenReq, new JsonObject { ["accessToken"] = accessToken }, ct);
        var accounts = await connection.RequestAsync(PayloadTypes.GetAccountsByAccessTokenReq, new JsonObject { ["accessToken"] = accessToken }, ct);

        return new CTraderIdentity(
            OpenApiParsing.CtidUserId(profile),
            OpenApiParsing.Accounts(accounts),
            accounts.GetString("permissionScope"));
    }

    /// <summary>New tokens from the refresh token. The old access and refresh tokens stop working.</summary>
    public async Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        await using var connection = await OpenAuthorizedAsync(ct);
        var res = await connection.RequestAsync(PayloadTypes.RefreshTokenReq, new JsonObject { ["refreshToken"] = refreshToken }, ct);
        return OpenApiParsing.RefreshedTokens(res, clock.GetUtcNow());
    }

    /// <summary>
    /// Opens a connection and authorises our application on it, which every Open API session must do first.
    /// Token-level calls (profile, account list, refresh) work on either endpoint; demo is used.
    /// </summary>
    private async Task<OpenApiConnection> OpenAuthorizedAsync(CancellationToken ct)
    {
        if (!Options.IsConfigured)
            throw new CTraderException("NOT_CONFIGURED", "CTrader__ClientId and CTrader__ClientSecret are not set.");

        var connection = await OpenApiConnection.OpenAsync(new Uri(Options.DemoEndpoint), logger, ct);
        try
        {
            await connection.RequestAsync(PayloadTypes.ApplicationAuthReq, new JsonObject
            {
                ["clientId"] = Options.ClientId,
                ["clientSecret"] = Options.ClientSecret,
            }, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string? Text(JsonObject body, params string[] names) =>
        names.Select(n => body[n]).OfType<JsonValue>().Select(v => v.ToString()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}
