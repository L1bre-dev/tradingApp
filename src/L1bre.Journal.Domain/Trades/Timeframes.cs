namespace L1bre.Journal.Domain.Trades;

/// <summary>A chart timeframe the journal stores candles for.</summary>
public sealed record Timeframe(string Label, TimeSpan Duration);

/// <summary>The six timeframes of the strategies, M1 to H4 (design v3).</summary>
public static class Timeframes
{
    public static readonly Timeframe M1 = new("M1", TimeSpan.FromMinutes(1));
    public static readonly Timeframe M5 = new("M5", TimeSpan.FromMinutes(5));
    public static readonly Timeframe M15 = new("M15", TimeSpan.FromMinutes(15));
    public static readonly Timeframe M30 = new("M30", TimeSpan.FromMinutes(30));
    public static readonly Timeframe H1 = new("H1", TimeSpan.FromHours(1));
    public static readonly Timeframe H4 = new("H4", TimeSpan.FromHours(4));

    public static readonly IReadOnlyList<Timeframe> All = [M1, M5, M15, M30, H1, H4];

    /// <summary>Reads a timeframe label such as the Frame cBot's order comment ("M15"). Case and spaces are ignored.</summary>
    public static Timeframe? TryParse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : All.FirstOrDefault(t => string.Equals(t.Label, text.Trim(), StringComparison.OrdinalIgnoreCase));

    public static Timeframe Get(string label) => TryParse(label) ?? throw new ArgumentException($"Unknown timeframe '{label}'.", nameof(label));
}
