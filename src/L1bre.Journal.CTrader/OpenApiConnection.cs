using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace L1bre.Journal.CTrader;

/// <summary>
/// One WebSocket connection to the Open API (JSON on port 5036).
/// Requests carry a clientMsgId; cTrader echoes it in the answer, which is how answers are matched.
/// A heartbeat is sent every 10 seconds, as the Open API asks, to keep the connection open.
/// </summary>
public sealed class OpenApiConnection : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>cTrader allows 5 historical requests per second; one every 250 ms keeps a margin.</summary>
    private static readonly TimeSpan HistoricalSpacing = TimeSpan.FromMilliseconds(250);

    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim historicalLock = new(1, 1);
    private DateTimeOffset lastHistorical = DateTimeOffset.MinValue;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OpenApiMessage>> pending = new();
    private readonly CancellationTokenSource stop = new();
    private readonly ILogger logger;
    private Task? receiveLoop;
    private Task? heartbeatLoop;
    private long nextId;

    private OpenApiConnection(ILogger logger) => this.logger = logger;

    public static async Task<OpenApiConnection> OpenAsync(Uri endpoint, ILogger logger, CancellationToken ct)
    {
        var connection = new OpenApiConnection(logger);
        await connection.socket.ConnectAsync(endpoint, ct);
        connection.receiveLoop = Task.Run(connection.ReceiveLoopAsync);
        connection.heartbeatLoop = Task.Run(connection.HeartbeatLoopAsync);
        return connection;
    }

    /// <summary>Sends a request and waits for its answer. An error answer becomes a <see cref="CTraderException"/>.</summary>
    public async Task<OpenApiMessage> RequestAsync(int payloadType, JsonObject payload, CancellationToken ct)
    {
        var id = $"l1bre-{Interlocked.Increment(ref nextId)}";
        var answer = new TaskCompletionSource<OpenApiMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = answer;
        try
        {
            if (PayloadTypes.IsHistorical(payloadType)) await WaitHistoricalTurnAsync(ct);
            await SendAsync(new OpenApiMessage(payloadType, payload, id), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
            timeout.CancelAfter(RequestTimeout);
            var message = await answer.Task.WaitAsync(timeout.Token);
            if (message.IsError) throw message.ToException();
            return message;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CTraderException("TIMEOUT", $"No answer from cTrader to message {payloadType} within {RequestTimeout.TotalSeconds:0} s.");
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    private async Task WaitHistoricalTurnAsync(CancellationToken ct)
    {
        await historicalLock.WaitAsync(ct);
        try
        {
            var wait = lastHistorical + HistoricalSpacing - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            lastHistorical = DateTimeOffset.UtcNow;
        }
        finally
        {
            historicalLock.Release();
        }
    }

    private async Task SendAsync(OpenApiMessage message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJson());
        await sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();
        try
        {
            while (!stop.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var json = text.ToString();
                text.Clear();
                Dispatch(json);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // Closing, or the connection dropped: waiting requests fail below.
        }
        finally
        {
            foreach (var waiting in pending.Values)
                waiting.TrySetException(new CTraderException("DISCONNECTED", "The connection to cTrader was closed."));
        }
    }

    private void Dispatch(string json)
    {
        OpenApiMessage message;
        try
        {
            message = OpenApiMessage.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unreadable message from cTrader");
            return;
        }

        if (message.ClientMsgId is not null && pending.TryGetValue(message.ClientMsgId, out var waiting))
        {
            waiting.TrySetResult(message);
            return;
        }

        if (message.PayloadType == PayloadTypes.HeartbeatEvent) return;
        logger.LogInformation("cTrader event {PayloadType} ignored", message.PayloadType);
    }

    private async Task HeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
                await SendAsync(new OpenApiMessage(PayloadTypes.HeartbeatEvent, []), stop.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", closeTimeout.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        foreach (var loop in new[] { receiveLoop, heartbeatLoop })
        {
            if (loop is null) continue;
            try { await loop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { /* already stopping */ }
        }
        socket.Dispose();
        stop.Dispose();
        sendLock.Dispose();
        historicalLock.Dispose();
    }
}
