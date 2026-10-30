namespace L1bre.Journal.Domain.Trades;

/// <summary>
/// The pip used by the strategies, same rule as the cBots (L1bre.Core StrategyPips.FromSymbol, itself from MT5 GetPipSize):
/// 5- or 3-digit symbols (forex) → 10 × point; anything else (gold, indices) → point.
/// XAUUSD with 2 digits → 0.01, so 500 pips = $5 (decided 2026-10-01).
/// </summary>
public static class StrategyPip
{
    public static decimal FromDigits(int digits)
    {
        var point = 1m;
        for (var i = 0; i < digits; i++) point /= 10;
        return digits is 5 or 3 ? point * 10 : point;
    }
}
