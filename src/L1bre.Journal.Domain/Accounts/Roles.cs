namespace L1bre.Journal.Domain.Accounts;

/// <summary>
/// The three roles of the app. A person can hold several (e.g. Trader + Coach).
/// </summary>
public static class Roles
{
    /// <summary>Links cTrader accounts, sees and analyses their own trades.</summary>
    public const string Trader = "Trader";

    /// <summary>Analyses the trades of the traders who invited them.</summary>
    public const string Coach = "Coach";

    /// <summary>Invites people and manages accounts.</summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Trader, Coach, Admin];
}
