using L1bre.Journal.Domain.Linking;

namespace L1bre.Journal.Services;

/// <summary>
/// Background job: every 6 hours, renews cTrader access tokens that expire within a week,
/// so links keep working without the trader doing anything.
/// </summary>
public class TokenRenewalWorker(IServiceScopeFactory scopes, ILogger<TokenRenewalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app time to start (migrations) before the first run.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        using var timer = new PeriodicTimer(TokenRules.CheckEvery);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var links = scope.ServiceProvider.GetRequiredService<CTraderLinkService>();
                if (links.IsConfigured) await links.RenewExpiringTokensAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Token renewal run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
