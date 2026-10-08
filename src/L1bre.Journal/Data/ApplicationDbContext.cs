using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Invitation> Invitations => Set<Invitation>();

    /// <summary>
    /// Keys that sign the login cookies (and, from milestone 2, encrypt cTrader tokens).
    /// Kept in the database so logins survive a restart and are included in backups.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(100);
        });

        builder.Entity<Invitation>(invitation =>
        {
            invitation.Property(i => i.Email).HasMaxLength(256);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.Property(i => i.Roles).HasMaxLength(100);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.Ignore(i => i.RoleList);
        });
    }
}
