using System.Threading.Channels;
using L1bre.Journal.Data;
using Microsoft.EntityFrameworkCore;

namespace L1bre.Journal.Services;

/// <summary>Wakes the sync job before its next round, e.g. when a trader clicks Sync now or selects an account.</summary>
public class SyncSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await channel.Reader.ReadAsync(timer.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout: the regular round.
        }
    }
}

/// <summary>
/// Background job: every 5 minutes (or at once when woken), imports new trades of every selected account,
/// one account after the other, so the cTrader rate limits are respected.
/// </summary>
public class SyncWorker(IServiceScopeFactory scopes, SyncSignal signal, IConfiguration config, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(config.GetValue("Sync:IntervalMinutes", 5));

        // Give the app time to start (migrations) before the first round; a Sync now click starts it earlier.
        await signal.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRoundAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sync round failed");
            }
            await signal.WaitAsync(interval, stoppingToken);
        }
    }

    private async Task RunRoundAsync(CancellationToken ct)
    {
        List<Guid> accounts;
        using (var scope = scopes.CreateScope())
        {
            var links = scope.ServiceProvider.GetRequiredService<CTraderLinkService>();
            if (!links.IsConfigured) return;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Accounts someone just asked for go first.
            accounts = await db.TradingAccounts
                .Where(a => a.IsSelected && a.IsAvailable && a.CTraderLink!.Status == LinkStatus.Active)
                .OrderBy(a => a.SyncRequestedAt == null).ThenBy(a => a.LastSyncAt)
                .Select(a => a.Id).ToListAsync(ct);
        }

        foreach (var id in accounts)
        {
            // A fresh scope per account: one account's tracked entities never leak into the next.
            // An unexpected failure on one account must not stop the others.
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TradeSyncService>().SyncAccountAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Sync of account {AccountId} failed unexpectedly", id);
            }
        }
    }
}
