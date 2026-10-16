using System.Text.Json.Nodes;
using L1bre.Journal.CTrader;

namespace L1bre.Journal.CTrader.Tests;

public class OpenApiMessageTests
{
    [Fact]
    public void Message_round_trips_through_json()
    {
        var message = new OpenApiMessage(2100, new JsonObject { ["clientId"] = "abc" }, "m1");
        var parsed = OpenApiMessage.Parse(message.ToJson());

        Assert.Equal(2100, parsed.PayloadType);
        Assert.Equal("m1", parsed.ClientMsgId);
        Assert.Equal("abc", parsed.GetString("clientId"));
    }

    [Fact]
    public void Int64_accepted_as_number_or_string()
    {
        Assert.Equal(5551001L, OpenApiMessage.ReadInt64(JsonValue.Create("5551001")));
        Assert.Equal(5551001L, OpenApiMessage.ReadInt64(JsonValue.Create(5551001L)));
        Assert.Null(OpenApiMessage.ReadInt64(null));
    }

    [Fact]
    public void Error_message_becomes_exception()
    {
        var error = OpenApiMessage.Parse("""{"payloadType":2142,"payload":{"errorCode":"CH_ACCESS_TOKEN_INVALID","description":"Invalid access token"},"clientMsgId":"x"}""");

        Assert.True(error.IsError);
        var ex = error.ToException();
        Assert.Equal("CH_ACCESS_TOKEN_INVALID", ex.ErrorCode);
        Assert.True(ex.IsTokenProblem);
    }

    [Fact]
    public void Accounts_are_read_from_the_account_list()
    {
        var res = OpenApiMessage.Parse("""
            {"payloadType":2150,"payload":{"accessToken":"t","ctidTraderAccount":[
              {"ctidTraderAccountId":"1001","isLive":false,"traderLogin":"5551001","brokerTitleShort":"Fusion Markets"},
              {"ctidTraderAccountId":1002,"isLive":true}
            ]}}
            """);

        var accounts = OpenApiParsing.Accounts(res);

        Assert.Equal(2, accounts.Count);
        Assert.Equal(new CTraderAccountInfo(1001, false, 5551001, "Fusion Markets"), accounts[0]);
        Assert.Equal(new CTraderAccountInfo(1002, true, null, null), accounts[1]);
    }

    [Fact]
    public void Token_response_success_and_error()
    {
        var now = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
        var ok = CTraderApi.ParseTokenResponse(JsonNode.Parse("""
            {"accessToken":"a","tokenType":"bearer","expiresIn":2628000,"refreshToken":"r","errorCode":null,"description":null}
            """)!.AsObject(), now);

        Assert.Equal(new TokenSet("a", "r", now.AddSeconds(2628000)), ok);

        var ex = Assert.Throws<CTraderException>(() => CTraderApi.ParseTokenResponse(JsonNode.Parse("""
            {"errorCode":"ACCESS_DENIED","description":"Invalid code"}
            """)!.AsObject(), now));
        Assert.Equal("ACCESS_DENIED", ex.ErrorCode);
    }
}
