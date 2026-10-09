using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<CTraderLink> CTraderLinks => Set<CTraderLink>();
    public DbSet<TradingAccount> TradingAccounts => Set<TradingAccount>();

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

        builder.Entity<CTraderLink>(link =>
        {
            link.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
            link.HasIndex(l => new { l.UserId, l.CtidUserId }).IsUnique();
            link.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            link.Property(l => l.LastError).HasMaxLength(500);
        });

        builder.Entity<TradingAccount>(account =>
        {
            account.HasOne(a => a.CTraderLink).WithMany(l => l.Accounts).HasForeignKey(a => a.CTraderLinkId).OnDelete(DeleteBehavior.Cascade);
            account.HasIndex(a => new { a.CTraderLinkId, a.CtidTraderAccountId }).IsUnique();
            account.Property(a => a.BrokerName).HasMaxLength(100);
        });
    }
}
