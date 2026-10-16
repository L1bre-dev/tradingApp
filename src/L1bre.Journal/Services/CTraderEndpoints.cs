using System.Security.Claims;
using L1bre.Journal.Domain.Accounts;
using Microsoft.AspNetCore.Identity;

namespace L1bre.Journal.Services;

/// <summary>
/// The two URLs of the cTrader login round trip. They are plain endpoints (not pages)
/// because they only redirect.
/// </summary>
public static class CTraderEndpoints
{
    public static void MapCTraderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/ctrader").RequireAuthorization(Roles.Trader);

        // "Link a cTrader ID" button → cTrader login page.
        group.MapGet("/link", (HttpContext http, ClaimsPrincipal user, UserManager<Data.ApplicationUser> users, CTraderLinkService links) =>
        {
            if (!links.IsConfigured) return Results.Redirect("/accounts?link=not-configured");
            return Results.Redirect(links.Start(http, users.GetUserId(user)!));
        });

        // cTrader sends the browser back here with ?code=... after the trader allowed access.
        group.MapGet("/callback", async (HttpContext http, ClaimsPrincipal user, UserManager<Data.ApplicationUser> users, CTraderLinkService links,
            string? code, string? state) =>
        {
            var (result, error) = await links.CompleteAsync(http, users.GetUserId(user)!, code, state);
            var query = $"link={result.ToString().ToLowerInvariant()}";
            if (error is not null) query += $"&error={Uri.EscapeDataString(error)}";
            return Results.Redirect($"/accounts?{query}");
        });
    }
}
