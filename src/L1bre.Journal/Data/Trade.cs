namespace L1bre.Journal.Data;

public enum TradeStatus
{
    /// <summary>Manual trade (or bot trade without timeframe): the trader picks timeframe and technique.</summary>
    ToQualify,
    Qualified,
}

public enum CandlesStatus { Pending, Stored, Failed }

/// <summary>
/// One cTrader position of an imported account = one trade in the journal.
/// Money is in the account currency; prices in the symbol's price.
/// </summary>
public class Trade
{
    public Guid Id { get; set; }
    public Guid TradingAccountId { get; set; }
    public TradingAccount? TradingAccount { get; set; }

    public long PositionId { get; set; }
    public long SymbolId { get; set; }
    public string SymbolName { get; set; } = "";
    public int Digits { get; set; }

    /// <summary>Strategy pip of the symbol (0.01 on XAUUSD), same rule as the cBots.</summary>
    public decimal PipSize { get; set; }

    public string Direction { get; set; } = "";
    public long VolumeCents { get; set; }
    public long? LotSizeCents { get; set; }

    // The opening order: where label, comment and protection come from.
    public long OpeningOrderId { get; set; }
    public string? OrderType { get; set; }
    public string? Label { get; set; }
    public string? Comment { get; set; }

    public string Origin { get; set; } = "";
    public string? Technique { get; set; }
    public string? Timeframe { get; set; }
    public TradeStatus Status { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }

    public long ClosedVolumeCents { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal Swap { get; set; }
    public decimal Commission { get; set; }
    public decimal ConversionFee { get; set; }
    public decimal NetProfit { get; set; }
    public string Outcome { get; set; } = "";
    public decimal? Pips { get; set; }

    public CandlesStatus CandlesStatus { get; set; }
    public int CandlesAttempts { get; set; }
    public string? CandlesError { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<TradeCandle> Candles { get; set; } = [];

    /// <summary>Volume in lots when the symbol's lot size is known (XAUUSD: 1 lot = 100 oz).</summary>
    public decimal? Lots => LotSizeCents is > 0 ? (decimal)VolumeCents / LotSizeCents.Value : null;
}

/// <summary>One candle of a trade's chart. The last candle of each timeframe is cut at the fill (IsCut).</summary>
public class TradeCandle
{
    public Guid TradeId { get; set; }
    public string Timeframe { get; set; } = "";
    public DateTimeOffset OpenTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public bool IsCut { get; set; }
}

/// <summary>
/// An execution received from cTrader, kept as received (RawJson) plus the fields needed to rebuild its position.
/// Positions are always rebuilt from all their stored executions, so a close arriving days later completes the trade.
/// </summary>
public class TradeDeal
{
    public Guid TradingAccountId { get; set; }
    public long DealId { get; set; }
    public long PositionId { get; set; }
    public long OrderId { get; set; }
    public long SymbolId { get; set; }
    public string Side { get; set; } = "";
    public long VolumeCents { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public decimal Price { get; set; }
    public decimal Commission { get; set; }
    public bool IsClosing { get; set; }
    public long ClosedVolumeCents { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal Swap { get; set; }
    public decimal CloseCommission { get; set; }
    public decimal ConversionFee { get; set; }
    public string RawJson { get; set; } = "";
}
