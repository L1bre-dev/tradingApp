namespace L1bre.Journal.CTrader;

/// <summary>
/// Settings of the cTrader Open API application (section "CTrader" in configuration,
/// e.g. environment variables CTrader__ClientId and CTrader__ClientSecret).
/// The URLs default to the real cTrader services; tests point them to a fake server.
/// </summary>
public class CTraderOptions
{
    public const string Section = "CTrader";

    /// <summary>Client ID of the Open API application registered on openapi.ctrader.com.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Client secret of that application. Only in the VPS .env, never in Git.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Page where the trader logs in with their cTrader ID and grants access.</summary>
    public string AuthorizeUrl { get; set; } = "https://id.ctrader.com/my/settings/openapi/grantingaccess/";

    /// <summary>Endpoint exchanging the one-minute authorisation code for tokens.</summary>
    public string TokenUrl { get; set; } = "https://openapi.ctrader.com/apps/token";

    /// <summary>JSON-over-WebSocket endpoints (port 5036 is the JSON port; demo and live are separate).</summary>
    public string DemoEndpoint { get; set; } = "wss://demo.ctraderapi.com:5036";
    public string LiveEndpoint { get; set; } = "wss://live.ctraderapi.com:5036";

    /// <summary>"accounts" = read-only. The app never asks for "trading".</summary>
    public string Scope { get; set; } = "accounts";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
