namespace L1bre.Journal.Domain.Trades;

public enum Direction { Buy, Sell }

/// <summary>
/// One execution (a cTrader "deal") as the journal needs it. Money is already in the account currency
/// (cTrader sends integers scaled by 10^moneyDigits; the client converts them).
/// Charges (commission, swap paid) are negative, as cTrader sends them.
/// </summary>
public sealed record Fill(
    long DealId,
    long PositionId,
    long OrderId,
    long SymbolId,
    Direction Side,
    long VolumeCents,
    DateTimeOffset ExecutedAt,
    decimal Price,
    decimal Commission,
    CloseDetail? Close)
{
    /// <summary>True when this execution reduced or closed the position (cTrader sends closePositionDetail).</summary>
    public bool IsClosing => Close is not null;
}

/// <summary>What cTrader reports on a closing execution (ProtoOAClosePositionDetail).</summary>
public sealed record CloseDetail(
    decimal EntryPrice,
    long ClosedVolumeCents,
    decimal GrossProfit,
    decimal Swap,
    decimal Commission,
    decimal ConversionFee);
