using L1bre.Journal.Domain.Accounts;

namespace L1bre.Journal.Domain.Tests;

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tokens_are_unique_and_url_safe()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => InvitationToken.NewToken()).ToList();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]{43}$", t));
    }

    [Fact]
    public void Hash_is_stable_and_differs_from_the_token()
    {
        var token = InvitationToken.NewToken();

        Assert.Equal(InvitationToken.Hash(token), InvitationToken.Hash(token));
        Assert.NotEqual(token, InvitationToken.Hash(token));
        Assert.NotEqual(InvitationToken.Hash(token), InvitationToken.Hash(InvitationToken.NewToken()));
    }

    [Fact]
    public void Pending_until_expiry()
    {
        var expires = Now + InvitationRules.Lifetime;

        Assert.Equal(InvitationState.Pending, InvitationRules.StateAt(Now, expires, null, null));
        Assert.Equal(InvitationState.Pending, InvitationRules.StateAt(expires.AddSeconds(-1), expires, null, null));
        Assert.Equal(InvitationState.Expired, InvitationRules.StateAt(expires, expires, null, null));
    }

    [Fact]
    public void Used_or_revoked_links_never_work_again()
    {
        var expires = Now.AddDays(7);

        Assert.Equal(InvitationState.Used, InvitationRules.StateAt(Now, expires, Now.AddMinutes(-5), null));
        Assert.Equal(InvitationState.Revoked, InvitationRules.StateAt(Now, expires, null, Now.AddMinutes(-5)));
        // Used then expired: still reported as used.
        Assert.Equal(InvitationState.Used, InvitationRules.StateAt(Now.AddDays(30), expires, Now, null));
    }

    [Fact]
    public void No_role_means_trader()
    {
        Assert.Equal([Roles.Trader], InvitationRules.NormalizeRoles([]));
        Assert.Equal([Roles.Trader, Roles.Admin], InvitationRules.NormalizeRoles([Roles.Admin]));
    }

    [Fact]
    public void Roles_are_cleaned_and_ordered()
    {
        Assert.Equal([Roles.Trader, Roles.Coach], InvitationRules.NormalizeRoles(["coach", " TRADER ", "unknown", "Coach"]));
        Assert.Equal([Roles.Coach], InvitationRules.NormalizeRoles([Roles.Coach]));
    }
}
