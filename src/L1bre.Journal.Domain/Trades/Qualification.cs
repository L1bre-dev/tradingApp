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
    /// Timeframes whose candles are stored: only its own for a qualified bot trade; all six for a manual trade
    /// (even one the trader qualified before its charts were built) or a bot trade without a timeframe,
    /// so the trader can qualify or change it later and the chart switches without asking cTrader again.
    /// </summary>
    public IReadOnlyList<Timeframe> TimeframesToStore =>
        Origin == TradeOrigin.Bot && IsQualified ? [Timeframes.Get(Timeframe!)] : Timeframes.All;

    /// <summary>
    /// Checks the trader's choice on the trade page: a known technique, and a timeframe whose candles are stored
    /// (the chart must be able to show it). Returns why it is refused, or null when it is fine.
    /// </summary>
    public static string? CheckChoice(string? technique, string? timeframe, IEnumerable<string> storedTimeframes)
    {
        if (string.IsNullOrWhiteSpace(technique) || !Techniques.Contains(technique)) return "Pick a technique.";
        if (Timeframes.TryParse(timeframe) is not { } tf) return "Pick a timeframe.";
        if (!storedTimeframes.Contains(tf.Label)) return $"The {tf.Label} candles of this trade are not stored, so it can't be qualified on {tf.Label}.";
        return null;
    }
}
