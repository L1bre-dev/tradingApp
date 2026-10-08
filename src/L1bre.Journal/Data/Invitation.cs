using L1bre.Journal.Domain.Accounts;

namespace L1bre.Journal.Data;

/// <summary>
/// An invitation to create an account. Sign-up is only possible through one.
/// The link contains a secret token; only its hash is stored here.
/// </summary>
public class Invitation
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";

    /// <summary>Comma-separated roles given when the invitation is accepted, e.g. "Trader,Coach".</summary>
    public string Roles { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Null for the first admin invitation, created by the app itself at start-up.</summary>
    public string? CreatedByUserId { get; set; }
    public string? UsedByUserId { get; set; }

    public IReadOnlyList<string> RoleList => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public InvitationState StateAt(DateTimeOffset now) => InvitationRules.StateAt(now, ExpiresAt, UsedAt, RevokedAt);
}
