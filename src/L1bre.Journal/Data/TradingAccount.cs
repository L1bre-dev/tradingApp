namespace L1bre.Journal.Data;

/// <summary>A cTrader trading account reachable through a link. Only selected accounts are imported.</summary>
public class TradingAccount
{
    public Guid Id { get; set; }
    public Guid CTraderLinkId { get; set; }
    public CTraderLink? CTraderLink { get; set; }

    /// <summary>cTrader's id for the account in the Open API (not the login number).</summary>
    public long CtidTraderAccountId { get; set; }

    /// <summary>Account number as shown in cTrader.</summary>
    public long? TraderLogin { get; set; }
    public string? BrokerName { get; set; }
    public bool IsLive { get; set; }

    /// <summary>Chosen by the trader for import (milestone 3).</summary>
    public bool IsSelected { get; set; }

    /// <summary>False when the token no longer opens this account (e.g. the account was closed).</summary>
    public bool IsAvailable { get; set; } = true;

    public DateTimeOffset AddedAt { get; set; }

    // Sync (milestone 3)
    public string? DepositCurrency { get; set; }
    public int? MoneyDigits { get; set; }

    /// <summary>Where the first import starts: the account's registration date.</summary>
    public DateTimeOffset? HistoryStart { get; set; }

    /// <summary>Executions are imported up to this time; the next sync continues from here.</summary>
    public DateTimeOffset? SyncedUntil { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? SyncRequestedAt { get; set; }
    public string? LastSyncError { get; set; }
    public DateTimeOffset? LastSyncErrorAt { get; set; }

    public List<Trade> Trades { get; set; } = [];
}
