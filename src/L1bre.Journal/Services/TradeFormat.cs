using System.Globalization;
using L1bre.Journal.Data;

namespace L1bre.Journal.Services;

/// <summary>How trade numbers are written on the pages (invariant culture: 4001.20, not 4001,20).</summary>
public static class TradeFormat
{
    public static string Price(Trade trade, decimal? price) =>
        price?.ToString("F" + trade.Digits, CultureInfo.InvariantCulture) ?? "";

    public static string Lots(Trade trade) =>
        trade.Lots?.ToString("0.##", CultureInfo.InvariantCulture)
        ?? (trade.VolumeCents / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " u";

    public static string Money(decimal amount, string? currency) =>
        $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}".TrimEnd();

    /// <summary>Distance between two prices in strategy pips (XAUUSD: 0.01 = 1 pip).</summary>
    public static decimal? Pips(Trade trade, decimal? from, decimal? to) =>
        from is null || to is null || trade.PipSize <= 0 ? null : Math.Round(Math.Abs(to.Value - from.Value) / trade.PipSize, 1);
}
