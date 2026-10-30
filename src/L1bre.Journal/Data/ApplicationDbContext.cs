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
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeCandle> TradeCandles => Set<TradeCandle>();
    public DbSet<TradeDeal> TradeDeals => Set<TradeDeal>();

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
            account.Property(a => a.DepositCurrency).HasMaxLength(10);
            account.Property(a => a.LastSyncError).HasMaxLength(500);
        });

        builder.Entity<Trade>(trade =>
        {
            trade.HasOne(t => t.TradingAccount).WithMany(a => a.Trades).HasForeignKey(t => t.TradingAccountId).OnDelete(DeleteBehavior.Cascade);
            trade.HasIndex(t => new { t.TradingAccountId, t.PositionId }).IsUnique();
            trade.HasIndex(t => new { t.TradingAccountId, t.OpenedAt });
            trade.Property(t => t.SymbolName).HasMaxLength(30);
            trade.Property(t => t.Direction).HasMaxLength(4);
            trade.Property(t => t.OrderType).HasMaxLength(30);
            trade.Property(t => t.Label).HasMaxLength(100);
            trade.Property(t => t.Comment).HasMaxLength(200);
            trade.Property(t => t.Origin).HasMaxLength(10);
            trade.Property(t => t.Technique).HasMaxLength(20);
            trade.Property(t => t.Timeframe).HasMaxLength(3);
            trade.Property(t => t.Outcome).HasMaxLength(5);
            trade.Property(t => t.Status).HasConversion<string>().HasMaxLength(12);
            trade.Property(t => t.CandlesStatus).HasConversion<string>().HasMaxLength(8);
            trade.Property(t => t.CandlesError).HasMaxLength(500);
            trade.Ignore(t => t.Lots);
            foreach (var price in new[] { "EntryPrice", "StopLoss", "TakeProfit", "ExitPrice", "PipSize", "Pips",
                         "GrossProfit", "Swap", "Commission", "ConversionFee", "NetProfit" })
                trade.Property(price).HasPrecision(20, 8);
        });

        builder.Entity<TradeCandle>(candle =>
        {
            candle.HasKey(c => new { c.TradeId, c.Timeframe, c.OpenTime });
            candle.HasOne<Trade>().WithMany(t => t.Candles).HasForeignKey(c => c.TradeId).OnDelete(DeleteBehavior.Cascade);
            candle.Property(c => c.Timeframe).HasMaxLength(3);
            foreach (var price in new[] { "Open", "High", "Low", "Close" }) candle.Property(price).HasPrecision(20, 8);
        });

        builder.Entity<TradeDeal>(deal =>
        {
            deal.HasKey(d => new { d.TradingAccountId, d.DealId });
            deal.HasOne<TradingAccount>().WithMany().HasForeignKey(d => d.TradingAccountId).OnDelete(DeleteBehavior.Cascade);
            deal.HasIndex(d => new { d.TradingAccountId, d.PositionId });
            deal.Property(d => d.Side).HasMaxLength(4);
            deal.Property(d => d.RawJson).HasColumnType("jsonb");
            foreach (var price in new[] { "Price", "Commission", "GrossProfit", "Swap", "CloseCommission", "ConversionFee" })
                deal.Property(price).HasPrecision(20, 8);
        });
    }
}
