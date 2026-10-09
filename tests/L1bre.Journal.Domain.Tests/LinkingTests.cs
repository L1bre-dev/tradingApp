using L1bre.Journal.Domain.Linking;

namespace L1bre.Journal.Domain.Tests;

public class LinkingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_renewed_one_week_before_expiry()
    {
        Assert.False(TokenRules.NeedsRefresh(Now.AddDays(30), Now));
        Assert.False(TokenRules.NeedsRefresh(Now.AddDays(7).AddMinutes(1), Now));
        Assert.True(TokenRules.NeedsRefresh(Now.AddDays(6), Now));
        Assert.True(TokenRules.NeedsRefresh(Now.AddDays(-1), Now));
    }

    [Fact]
    public void Pending_link_survives_the_cookie_round_trip()
    {
        var pending = PendingLink.Create("user-1", "abc", Now);
        Assert.Equal(pending, PendingLink.Deserialize(pending.Serialize()));
        Assert.Null(PendingLink.Deserialize("garbage"));
        Assert.Null(PendingLink.Deserialize(null));
    }

    [Fact]
    public void Callback_accepted_only_for_same_user_in_time_and_matching_state()
    {
        var pending = PendingLink.Create("user-1", "abc", Now);

        Assert.True(pending.Accepts("user-1", "abc", Now.AddMinutes(1)));
        Assert.True(pending.Accepts("user-1", null, Now.AddMinutes(1)));     // cTrader may not send state back
        Assert.False(pending.Accepts("user-2", "abc", Now.AddMinutes(1)));   // someone else
        Assert.False(pending.Accepts("user-1", "xyz", Now.AddMinutes(1)));   // forged state
        Assert.False(pending.Accepts("user-1", "abc", Now.AddMinutes(11)));  // too late
    }
}
