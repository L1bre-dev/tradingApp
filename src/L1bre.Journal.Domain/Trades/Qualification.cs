namespace L1bre.Journal.Domain.Trades;

public enum TradeOrigin { Bot, Manual }

/// <summary>
/// Technique and timeframe of a trade, read from its opening order. The Frame cBot places its orders with
/// label "Frame" and comment = timeframe ("M15"), so its trades arrive qualified. Anything else waits for the trader.
/// </summary>
public sealed record Qualification(TradeOrigin Origin, string? Technique, string? Timeframe)
{
    public bool IsQualified => Technique is not null && Timeframe is not null;

    /// <summary>The techniques of the strategies (design v3). Editable by the admin in a later milestone.</summary>
    public static readonly IReadOnlyList<string> Techniques = ["Frame", "FTM", "Family", "Jump"];

    public static Qualification FromOrder(string? label, string? comment)
    {
        if (string.IsNullOrWhiteSpace(label)) return new(TradeOrigin.Manual, null, null);
        var technique = Techniques.FirstOrDefault(t => string.Equals(t, label.Trim(), StringComparison.OrdinalIgnoreCase));
        return new(TradeOrigin.Bot, technique, Timeframes.TryParse(comment)?.Label);
    }

    /// <summary>
    /// Timeframes whose candles are stored: only the trade's own for a qualified trade, all six otherwise,
    /// so the trader can qualify it later and the chart switches without asking cTrader again.
    /// </summary>
    public IReadOnlyList<Timeframe> TimeframesToStore => IsQualified ? [Timeframes.Get(Timeframe!)] : Timeframes.All;
}
