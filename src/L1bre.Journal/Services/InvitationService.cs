using L1bre.Journal.Data;
using L1bre.Journal.Domain.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

/// <summary>
/// Creates, revokes and accepts invitations. Sign-up only happens here.
/// </summary>
public class InvitationService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider clock,
    ILogger<InvitationService> logger)
{
    public record Created(Invitation Invitation, string Token);

    public async Task<Created> CreateAsync(string email, IEnumerable<string> roles, string? createdByUserId)
    {
        var now = clock.GetUtcNow();
        var token = InvitationToken.NewToken();
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Email = email.Trim(),
            TokenHash = InvitationToken.Hash(token),
            Roles = string.Join(',', InvitationRules.NormalizeRoles(roles)),
            CreatedAt = now,
            ExpiresAt = now + InvitationRules.Lifetime,
            CreatedByUserId = createdByUserId,
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();
        return new Created(invitation, token);
    }

    public async Task RevokeAsync(Guid id)
    {
        var invitation = await db.Invitations.FindAsync(id);
        if (invitation is null || invitation.StateAt(clock.GetUtcNow()) != InvitationState.Pending) return;
        invitation.RevokedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>The invitation behind a link, only if the link still works.</summary>
    public async Task<Invitation?> FindPendingAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = InvitationToken.Hash(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash);
        return invitation?.StateAt(clock.GetUtcNow()) == InvitationState.Pending ? invitation : null;
    }

    /// <summary>
    /// Creates the account for a pending invitation. Returns the new user, or the errors to show.
    /// </summary>
    public async Task<(ApplicationUser? User, IEnumerable<string> Errors)> AcceptAsync(string token, string displayName, string password)
    {
        var invitation = await FindPendingAsync(token);
        if (invitation is null) return (null, ["This invitation link is no longer valid."]);

        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
            return (null, ["An account already exists for this email. Please log in."]);

        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            // The invitation link was given to this address, so we treat the email as confirmed.
            EmailConfirmed = true,
            DisplayName = displayName.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded) return (null, created.Errors.Select(e => e.Description));

        var roles = await userManager.AddToRolesAsync(user, invitation.RoleList);
        if (!roles.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return (null, roles.Errors.Select(e => e.Description));
        }

        invitation.UsedAt = clock.GetUtcNow();
        invitation.UsedByUserId = user.Id;
        await db.SaveChangesAsync();

        logger.LogInformation("Invitation {InvitationId} accepted by {Email} with roles {Roles}", invitation.Id, user.Email, invitation.Roles);
        return (user, []);
    }

    public async Task<List<Invitation>> ListAsync() =>
        await db.Invitations.OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync();
}
