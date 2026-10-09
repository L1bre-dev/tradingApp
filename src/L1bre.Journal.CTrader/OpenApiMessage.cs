using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace L1bre.Journal.CTrader;

/// <summary>
/// One Open API message in its JSON form, as sent over the WebSocket on port 5036:
/// <c>{"clientMsgId": "...", "payloadType": 2100, "payload": { ... }}</c>.
/// </summary>
public sealed record OpenApiMessage(int PayloadType, JsonObject Payload, string? ClientMsgId = null)
{
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["payloadType"] = PayloadType,
            ["payload"] = Payload.DeepClone(),
        };
        if (ClientMsgId is not null) root["clientMsgId"] = ClientMsgId;
        return root.ToJsonString();
    }

    public static OpenApiMessage Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("Open API message is not a JSON object.");
        var payloadType = ReadInt64(root["payloadType"]) ?? throw new FormatException("Open API message has no payloadType.");
        var payload = root["payload"] as JsonObject ?? new JsonObject();
        return new OpenApiMessage((int)payloadType, (JsonObject)payload.DeepClone(), root["clientMsgId"]?.GetValue<string>());
    }

    public bool IsError => PayloadType is PayloadTypes.OaErrorRes or PayloadTypes.ErrorRes;

    public CTraderException ToException() =>
        new(GetString("errorCode") ?? "UNKNOWN_ERROR", GetString("description"));

    public string? GetString(string name) => Payload[name] is JsonValue v ? v.ToString() : null;

    public long? GetInt64(string name) => ReadInt64(Payload[name]);

    /// <summary>
    /// 64-bit numbers may come as JSON numbers or as strings (the protobuf JSON convention),
    /// so both are accepted.
    /// </summary>
    public static long? ReadInt64(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<double>(out var d)) return (long)d;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    public static bool? ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
