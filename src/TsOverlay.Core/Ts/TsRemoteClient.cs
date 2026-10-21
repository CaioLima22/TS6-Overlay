using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TsOverlay.Core.Ts;

public enum TsConnectionStatus
{
    /// <summary>TS fechado ou Remote Apps desligado; tentando de novo a cada 2 s.</summary>
    Connecting,
    /// <summary>WebSocket aberto, esperando o usuário aprovar no TS.</summary>
    WaitingForApproval,
    /// <summary>Autenticado e recebendo eventos.</summary>
    Connected,
}

/// <summary>
/// Conexão com a Remote Apps API do TS5/TS6 em ws://localhost:{porta}.
/// Autentica uma vez, repassa cada mensagem recebida e reconecta sozinha.
/// </summary>
public sealed class TsRemoteClient : IDisposable
{
    public const string Identifier = "com.caio.tsoverlay";
    public const string AppName = "TS Overlay";
    public const string AppDescription = "Overlay in-game do canal atual";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly int _port;
    private readonly string _version;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public TsRemoteClient(int port, string? apiKey, string version = "0.1.0")
    {
        _port = port;
        _version = version;
        ApiKey = apiKey;
    }

    public string? ApiKey { get; private set; }

    public TsConnectionStatus Status { get; private set; } = TsConnectionStatus.Connecting;

    /// <summary>Cada mensagem da API: tipo e payload. Vem do thread do WebSocket.</summary>
    public event Action<string, JsonElement>? MessageReceived;

    /// <summary>Nova apiKey (ou null quando a antiga foi revogada). Salvar nas configurações.</summary>
    public event Action<string?>? ApiKeyChanged;

    public event Action<TsConnectionStatus>? StatusChanged;

    /// <summary>Disparado quando o WebSocket cai, para o estado ser limpo.</summary>
    public event Action? Disconnected;

    /// <summary>Mensagens brutas recebidas, para depurar mudanças da API entre betas.</summary>
    public event Action<string>? RawMessage;

    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var authenticated = false;
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri($"ws://localhost:{_port}"), ct);

                SetStatus(TsConnectionStatus.WaitingForApproval);
                var auth = TsMessages.BuildAuth(Identifier, _version, AppName, AppDescription, ApiKey);
                await ws.SendAsync(Encoding.UTF8.GetBytes(auth), WebSocketMessageType.Text, true, ct);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var text = await ReceiveTextAsync(ws, ct);
                    if (text is null) break;
                    if (Dispatch(text)) authenticated = true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException)
            {
                // TS fechado ou porta errada: tenta de novo abaixo
            }

            // Se caiu antes de autenticar com uma chave salva, ela provavelmente foi revogada
            if (!authenticated && !string.IsNullOrEmpty(ApiKey) && Status == TsConnectionStatus.WaitingForApproval)
            {
                ApiKey = null;
                ApiKeyChanged?.Invoke(null);
            }

            SetStatus(TsConnectionStatus.Connecting);
            Disconnected?.Invoke();

            try
            {
                await Task.Delay(ReconnectDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Repassa uma mensagem. Retorna true se foi a resposta de auth.</summary>
    private bool Dispatch(string text)
    {
        RawMessage?.Invoke(text);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.GetString("type") is not { } type || root.Get("payload") is not { } payload) return false;

            var isAuth = type == "auth";
            if (isAuth)
            {
                var key = payload.GetString("apiKey");
                if (!string.IsNullOrEmpty(key) && key != ApiKey)
                {
                    ApiKey = key;
                    ApiKeyChanged?.Invoke(key);
                }
                SetStatus(TsConnectionStatus.Connected);
            }

            MessageReceived?.Invoke(type, payload);
            return isAuth;
        }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
    }

    private void SetStatus(TsConnectionStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _cts.Dispose();
    }
}
