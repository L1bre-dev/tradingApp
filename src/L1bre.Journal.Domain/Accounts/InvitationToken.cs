using System.Security.Cryptography;
using System.Text;

namespace L1bre.Journal.Domain.Accounts;

/// <summary>
/// The secret part of an invitation link.
/// Only its SHA-256 hash is stored in the database: someone reading a database
/// backup cannot rebuild a working link from it (same idea as a password hash).
/// </summary>
public static class InvitationToken
{
    /// <summary>A new random token, URL-safe, 256 bits.</summary>
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The value stored in the database for a token.</summary>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
