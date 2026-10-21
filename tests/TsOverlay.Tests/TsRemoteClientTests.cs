using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TsOverlay.Core.Ts;

namespace TsOverlay.Tests;

/// <summary>Testa o cliente contra um servidor WebSocket local que imita o TS6.</summary>
public class TsRemoteClientTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Sends_auth_saves_api_key_and_forwards_events()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var authReceived = new TaskCompletionSource<JsonElement>();
        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsCtx.WebSocket;

            var buffer = new byte[8192];
            var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
            authReceived.SetResult(JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, r.Count)).RootElement.Clone());

            async Task Send(string s) =>
                await ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None);
            await Send("""{ "type": "auth", "payload": { "apiKey": "nova-chave", "connections": [] } }""");
            await Send("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 2, "status": 1 } }""");
            await Task.Delay(500);
        });

        var types = new List<string>();
        var gotTalk = new TaskCompletionSource();
        string? savedKey = null;

        using var client = new TsRemoteClient(port, apiKey: null);
        client.ApiKeyChanged += k => savedKey = k;
        client.MessageReceived += (type, _) =>
        {
            lock (types) types.Add(type);
            if (type == "talkStatusChanged") gotTalk.TrySetResult();
        };
        client.Start();

        var auth = await authReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("auth", auth.GetProperty("type").GetString());
        Assert.Equal(TsRemoteClient.Identifier, auth.GetProperty("payload").GetProperty("identifier").GetString());
        Assert.Equal("", auth.GetProperty("payload").GetProperty("content").GetProperty("apiKey").GetString());

        await gotTalk.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("nova-chave", savedKey);
        Assert.Equal(TsConnectionStatus.Connected, client.Status);
        lock (types) Assert.Equal(new[] { "auth", "talkStatusChanged" }, types);

        await server;
    }

    [Fact]
    public async Task Clears_saved_key_when_closed_before_auth()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            await ws.ReceiveAsync(new byte[8192], CancellationToken.None);
            // TS recusa a chave revogada fechando a conexão
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        });

        var cleared = new TaskCompletionSource<string?>();
        using var client = new TsRemoteClient(port, apiKey: "chave-revogada");
        client.ApiKeyChanged += k => cleared.TrySetResult(k);
        client.Start();

        Assert.Null(await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(client.ApiKey);
    }
}
