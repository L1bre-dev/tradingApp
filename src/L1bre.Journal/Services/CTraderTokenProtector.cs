using L1bre.Journal.CTrader;
using L1bre.Journal.Data;
using Microsoft.AspNetCore.DataProtection;

namespace L1bre.Journal.Services;

/// <summary>
/// Encrypts cTrader tokens before they go into the database, with ASP.NET Data Protection
/// (the same keys that sign the login cookies). This keeps tokens out of plain sight in the tables and logs.
/// The keys themselves are stored in the same database (see Program.cs), so a full database backup
/// contains everything needed to read the tokens: backups must stay private.
/// </summary>
public class CTraderTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("L1bre.Journal.CTraderTokens.v1");

    public void Store(CTraderLink link, TokenSet tokens)
    {
        link.AccessTokenEncrypted = protector.Protect(tokens.AccessToken);
        link.RefreshTokenEncrypted = protector.Protect(tokens.RefreshToken);
        link.AccessTokenExpiresAt = tokens.ExpiresAt;
    }

    public string AccessToken(CTraderLink link) => protector.Unprotect(link.AccessTokenEncrypted);
    public string RefreshToken(CTraderLink link) => protector.Unprotect(link.RefreshTokenEncrypted);
}
