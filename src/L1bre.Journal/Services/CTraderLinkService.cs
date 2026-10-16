using System.Security.Cryptography;
using L1bre.Journal.CTrader;
using L1bre.Journal.Data;
using L1bre.Journal.Domain.Linking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace L1bre.Journal.Services;

/// <summary>
/// Linking cTrader IDs to a user: start the cTrader login, finish it on the callback,
/// refresh the list of accounts, renew tokens, choose accounts to import, unlink.
/// </summary>
public class CTraderLinkService(
    ApplicationDbContext db,
    CTraderApi api,
    CTraderTokenProtector tokens,
    IDataProtectionProvider protection,
    IOptions<CTraderOptions> options,
    IConfiguration config,
    TimeProvider clock,
    ILogger<CTraderLinkService> logger)
{
    public const string PendingCookie = "l1bre.ctrader.link";
    private readonly IDataProtector cookieProtector = protection.CreateProtector("L1bre.Journal.PendingLink.v1");

    public bool IsConfigured => options.Value.IsConfigured;

    /// <summary>The redirect URI registered on the cTrader application. Must match exactly.</summary>
    public string RedirectUri => $"{(config["App:PublicUrl"] ?? "").TrimEnd('/')}/ctrader/callback";

    /// <summary>Remembers who is linking (cookie) and returns the cTrader login page to send them to.</summary>
    public string Start(HttpContext http, string userId)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pending = PendingLink.Create(userId, state, clock.GetUtcNow());
        http.Response.Cookies.Append(PendingCookie, cookieProtector.Protect(pending.Serialize()), new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            // Lax: the cookie still comes back on the top-level redirect from id.ctrader.com.
            SameSite = SameSiteMode.Lax,
            MaxAge = PendingLink.Lifetime,
            Path = "/ctrader",
        });
        return api.BuildAuthorizeUrl(RedirectUri, state);
    }

    public enum LinkResult { Linked, Cancelled, Expired, Failed }

    /// <summary>Handles the redirect back from cTrader: checks it, gets the tokens, stores the link and its accounts.</summary>
    public async Task<(LinkResult Result, string? ErrorCode)> CompleteAsync(HttpContext http, string userId, string? code, string? state)
    {
        PendingLink? pending = null;
        try
        {
            var cookie = http.Request.Cookies[PendingCookie];
            if (cookie is not null) pending = PendingLink.Deserialize(cookieProtector.Unprotect(cookie));
        }
        catch (CryptographicException)
        {
            // Tampered or from an older key: treated as missing.
        }
        http.Response.Cookies.Delete(PendingCookie, new CookieOptions { Path = "/ctrader" });

        if (pending is null || !pending.Accepts(userId, state, clock.GetUtcNow())) return (LinkResult.Expired, null);
        if (string.IsNullOrEmpty(code)) return (LinkResult.Cancelled, null);

        try
        {
            var tokenSet = await api.ExchangeCodeAsync(code, RedirectUri, http.RequestAborted);
            var identity = await api.GetIdentityAsync(tokenSet.AccessToken, http.RequestAborted);

            var link = await db.CTraderLinks.Include(l => l.Accounts)
                .SingleOrDefaultAsync(l => l.UserId == userId && l.CtidUserId == identity.CtidUserId);
            if (link is null)
            {
                link = new CTraderLink { Id = Guid.NewGuid(), UserId = userId, CtidUserId = identity.CtidUserId, LinkedAt = clock.GetUtcNow() };
                db.CTraderLinks.Add(link);
            }

            tokens.Store(link, tokenSet);
            link.TokenRefreshedAt = clock.GetUtcNow();
            link.Status = LinkStatus.Active;
            link.LastError = null;
            link.LastErrorAt = null;
            MergeAccounts(link, identity.Accounts);
            await db.SaveChangesAsync();

            logger.LogInformation("User {UserId} linked cTrader ID {CtidUserId} with {Count} accounts (scope {Scope})",
                userId, identity.CtidUserId, identity.Accounts.Count, identity.PermissionScope);
            return (LinkResult.Linked, null);
        }
        catch (CTraderException ex)
        {
            logger.LogWarning("Linking cTrader failed for user {UserId}: {Error}", userId, ex.Message);
            return (LinkResult.Failed, ex.ErrorCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Net.WebSockets.WebSocketException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Linking cTrader failed for user {UserId}: cTrader unreachable", userId);
            return (LinkResult.Failed, "UNREACHABLE");
        }
    }

    public Task<List<CTraderLink>> ListAsync(string userId) =>
        db.CTraderLinks.Include(l => l.Accounts).Where(l => l.UserId == userId).OrderBy(l => l.LinkedAt).ToListAsync();

    /// <summary>Asks cTrader again which accounts the link opens (new accounts appear, closed ones are flagged).</summary>
    public async Task RefreshAccountsAsync(string userId, Guid linkId, CancellationToken ct = default)
    {
        var link = await db.CTraderLinks.Include(l => l.Accounts).SingleOrDefaultAsync(l => l.Id == linkId && l.UserId == userId, ct);
        if (link is null) return;

        await RunAsync(link, async () =>
        {
            await RenewIfNeededAsync(link, ct);
            var identity = await api.GetIdentityAsync(tokens.AccessToken(link), ct);
            MergeAccounts(link, identity.Accounts);
            link.AccountsCheckedAt = clock.GetUtcNow();
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Used by the background job: renews every active token that gets close to its expiry.</summary>
    public async Task RenewExpiringTokensAsync(CancellationToken ct)
    {
        var soon = clock.GetUtcNow() + TokenRules.RefreshBefore;
        var links = await db.CTraderLinks.Where(l => l.Status == LinkStatus.Active && l.AccessTokenExpiresAt < soon).ToListAsync(ct);
        foreach (var link in links)
        {
            await RunAsync(link, () => RenewIfNeededAsync(link, ct));
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetSelectedAsync(string userId, Guid accountId, bool selected)
    {
        var account = await db.TradingAccounts.Include(a => a.CTraderLink)
            .SingleOrDefaultAsync(a => a.Id == accountId && a.CTraderLink!.UserId == userId);
        if (account is null) return;
        account.IsSelected = selected;
        await db.SaveChangesAsync();
    }

    /// <summary>Forgets the tokens and accounts of this cTrader ID.</summary>
    public async Task UnlinkAsync(string userId, Guid linkId)
    {
        var link = await db.CTraderLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.UserId == userId);
        if (link is null) return;
        db.CTraderLinks.Remove(link);
        await db.SaveChangesAsync();
        logger.LogInformation("User {UserId} unlinked cTrader ID {CtidUserId}", userId, link.CtidUserId);
    }

    private async Task RenewIfNeededAsync(CTraderLink link, CancellationToken ct)
    {
        if (!TokenRules.NeedsRefresh(link.AccessTokenExpiresAt, clock.GetUtcNow())) return;
        tokens.Store(link, await api.RefreshAsync(tokens.RefreshToken(link), ct));
        link.TokenRefreshedAt = clock.GetUtcNow();
        logger.LogInformation("Renewed cTrader token of link {LinkId}", link.Id);
    }

    /// <summary>Runs a cTrader call for a link and records the outcome on the link.</summary>
    private async Task RunAsync(CTraderLink link, Func<Task> action)
    {
        try
        {
            await action();
            link.LastError = null;
            link.LastErrorAt = null;
        }
        catch (Exception ex) when (ex is CTraderException or HttpRequestException or System.Net.WebSockets.WebSocketException or TaskCanceledException)
        {
            if (ex is CTraderException { IsTokenProblem: true }) link.Status = LinkStatus.NeedsRelink;
            link.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            link.LastErrorAt = clock.GetUtcNow();
            logger.LogWarning("cTrader call failed for link {LinkId}: {Error}", link.Id, ex.Message);
        }
    }

    private void MergeAccounts(CTraderLink link, IReadOnlyList<CTraderAccountInfo> accounts)
    {
        foreach (var info in accounts)
        {
            var account = link.Accounts.SingleOrDefault(a => a.CtidTraderAccountId == info.CtidTraderAccountId);
            if (account is null)
            {
                account = new TradingAccount { Id = Guid.NewGuid(), CtidTraderAccountId = info.CtidTraderAccountId, AddedAt = clock.GetUtcNow() };
                link.Accounts.Add(account);
            }
            account.IsLive = info.IsLive;
            account.TraderLogin = info.TraderLogin;
            account.BrokerName = info.BrokerName;
            account.IsAvailable = true;
        }

        var seen = accounts.Select(a => a.CtidTraderAccountId).ToHashSet();
        foreach (var gone in link.Accounts.Where(a => !seen.Contains(a.CtidTraderAccountId)))
            gone.IsAvailable = false;

        link.AccountsCheckedAt = clock.GetUtcNow();
    }
}
