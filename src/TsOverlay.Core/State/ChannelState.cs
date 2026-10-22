using System.Text.Json;
using TsOverlay.Core.Ts;

namespace TsOverlay.Core.State;

/// <summary>
/// Modelo do canal atual. Recebe os eventos da Remote Apps API e decide quem aparece:
/// os clientes da conexão ativa cujo canal é igual ao meu.
/// Não depende de WebSocket nem de UI, para poder ser testado sozinho.
/// </summary>
public sealed class ChannelState
{
    private sealed class Client
    {
        public required string Id { get; init; }
        public string? ChannelId { get; set; }
        public string Nickname { get; set; } = "";
        public bool Talking { get; set; }
        public bool InputMuted { get; set; }
        public bool OutputMuted { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private sealed class Connection
    {
        public required string Id { get; init; }
        public string? MyClientId { get; set; }
        public Dictionary<string, Client> Clients { get; } = new();
        public string? LastLeftClientId { get; set; }
        public string? LastLeftNickname { get; set; }

        public string? MyChannelId =>
            MyClientId is not null && Clients.TryGetValue(MyClientId, out var me) ? me.ChannelId : null;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Connection> _connections = new();
    private string? _activeConnectionId;

    /// <summary>Disparado depois de qualquer mudança visível. Pode vir de qualquer thread.</summary>
    public event Action? Changed;

    public string? ActiveConnectionId
    {
        get { lock (_lock) return _activeConnectionId; }
    }

    /// <summary>Usuários visíveis no overlay, em ordem alfabética, com "eu" incluído.</summary>
    public IReadOnlyList<ClientInfo> GetVisibleClients()
    {
        lock (_lock)
        {
            if (_activeConnectionId is null || !_connections.TryGetValue(_activeConnectionId, out var conn)
                || conn.MyClientId is null || !conn.Clients.TryGetValue(conn.MyClientId, out var me)
                || me.ChannelId is null)
            {
                return Array.Empty<ClientInfo>();
            }

            return conn.Clients.Values
                .Where(c => c.ChannelId == me.ChannelId)
                .OrderBy(c => c.Nickname, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new ClientInfo(conn.Id, c.Id, c.Nickname, c.Talking, c.InputMuted, c.OutputMuted, c.Id == conn.MyClientId, c.AvatarUrl))
                .ToList();
        }
    }

    /// <summary>Apelido do último que saiu do meu canal, ou null. Zera quando eu troco de canal.</summary>
    public string? GetLastLeft()
    {
        lock (_lock)
        {
            if (_activeConnectionId is null || !_connections.TryGetValue(_activeConnectionId, out var conn)
                || conn.MyChannelId is null)
            {
                return null;
            }
            return conn.LastLeftNickname;
        }
    }

    /// <summary>Limpa tudo (por exemplo quando o WebSocket cai).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _connections.Clear();
            _activeConnectionId = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Aplica uma mensagem da API. Tipos desconhecidos são ignorados.</summary>
    public void Apply(string type, JsonElement payload)
    {
        bool changed;
        lock (_lock)
        {
            changed = type switch
            {
                "auth" => ApplyAuth(payload),
                "talkStatusChanged" => ApplyTalkStatus(payload),
                "clientMoved" => ApplyClientMoved(payload),
                "clientPropertiesUpdated" => ApplyClientProperties(payload),
                "connectStatusChanged" => ApplyConnectStatus(payload),
                "clientSelfPropertyUpdated" => ApplySelfProperty(payload),
                _ => false,
            };
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Atalho para testes e logs: aplica uma mensagem completa em JSON.</summary>
    public void ApplyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetString("type") is { } type && root.Get("payload") is { } payload)
        {
            Apply(type, payload);
        }
    }

    private bool ApplyAuth(JsonElement payload)
    {
        _connections.Clear();
        _activeConnectionId = null;

        foreach (var c in payload.GetArray("connections"))
        {
            if (c.GetId("id") is not { } connId) continue;
            var conn = new Connection { Id = connId, MyClientId = c.GetId("clientId") };
            _connections[connId] = conn;

            foreach (var info in c.GetArray("clientInfos"))
            {
                if (info.GetId("id") is not { } clientId) continue;
                var client = new Client { Id = clientId, ChannelId = info.GetId("channelId") };
                if (info.Get("properties") is { } props) ApplyProperties(client, props);
                conn.Clients[clientId] = client;
            }
        }

        EnsureActiveConnection();
        return true;
    }

    private bool ApplyTalkStatus(JsonElement payload)
    {
        if (FindClient(payload) is not { } client) return false;
        var talking = payload.GetInt("status") is { } s && s != 0;
        if (client.Talking == talking) return false;
        client.Talking = talking;
        return true;
    }

    private bool ApplyClientMoved(JsonElement payload)
    {
        if (payload.GetId("connectionId") is not { } connId || payload.GetId("clientId") is not { } clientId)
            return false;

        var conn = GetOrAddConnection(connId);
        var newChannelId = payload.GetId("newChannelId");
        var myChannelId = conn.MyChannelId;
        conn.Clients.TryGetValue(clientId, out var client);

        if (clientId == conn.MyClientId)
        {
            // Canal novo, lista nova: quem saiu do canal antigo não interessa mais
            if (newChannelId != myChannelId) ClearLastLeft(conn);
        }
        else if (client is not null && myChannelId is not null)
        {
            if (client.ChannelId == myChannelId && newChannelId != myChannelId)
            {
                conn.LastLeftClientId = clientId;
                conn.LastLeftNickname = client.Nickname;
            }
            else if (newChannelId == myChannelId && conn.LastLeftClientId == clientId)
            {
                ClearLastLeft(conn); // voltou
            }
        }

        // newChannelId 0 = saiu do servidor
        if (newChannelId is null)
        {
            return conn.Clients.Remove(clientId);
        }

        if (client is null)
        {
            client = new Client { Id = clientId };
            conn.Clients[clientId] = client;
        }
        client.ChannelId = newChannelId;
        // Quem troca de canal não está mais falando no canal antigo
        client.Talking = false;
        if (payload.Get("properties") is { } props) ApplyProperties(client, props);
        return true;
    }

    private bool ApplyClientProperties(JsonElement payload)
    {
        if (FindClient(payload) is not { } client || payload.Get("properties") is not { } props) return false;
        ApplyProperties(client, props);
        return true;
    }

    private bool ApplyConnectStatus(JsonElement payload)
    {
        if (payload.GetId("connectionId") is not { } connId) return false;

        switch (payload.GetInt("status"))
        {
            case 0: // desconectou
                _connections.Remove(connId);
                if (_activeConnectionId == connId) _activeConnectionId = null;
                EnsureActiveConnection();
                return true;
            case 4: // conectou
                var conn = GetOrAddConnection(connId);
                if (payload.Get("info") is { } info && info.GetId("clientId") is { } me) conn.MyClientId = me;
                EnsureActiveConnection();
                return true;
            default: // 1 a 3 são etapas da conexão
                return false;
        }
    }

    private bool ApplySelfProperty(JsonElement payload)
    {
        // A API não diz qual aba está ativa. As abas inativas desligam o microfone,
        // então a conexão que ganhou inputHardware é a que está em foco.
        if (payload.GetId("connectionId") is not { } connId) return false;
        if (payload.GetString("flag") != "inputHardware") return false;
        if (payload.GetBool("newValue") == false) return false;
        if (_activeConnectionId == connId) return false;

        GetOrAddConnection(connId);
        _activeConnectionId = connId;
        return true;
    }

    private Client? FindClient(JsonElement payload)
    {
        if (payload.GetId("connectionId") is { } connId && payload.GetId("clientId") is { } clientId
            && _connections.TryGetValue(connId, out var conn) && conn.Clients.TryGetValue(clientId, out var client))
        {
            return client;
        }
        return null;
    }

    private static void ClearLastLeft(Connection conn)
    {
        conn.LastLeftClientId = null;
        conn.LastLeftNickname = null;
    }

    private Connection GetOrAddConnection(string connId)
    {
        if (!_connections.TryGetValue(connId, out var conn))
        {
            conn = new Connection { Id = connId };
            _connections[connId] = conn;
        }
        return conn;
    }

    private void EnsureActiveConnection()
    {
        if (_activeConnectionId is not null && _connections.ContainsKey(_activeConnectionId)) return;
        _activeConnectionId = _connections.Keys.FirstOrDefault();
    }

    private static void ApplyProperties(Client client, JsonElement props)
    {
        if (props.GetString("nickname") is { } nick) client.Nickname = nick;
        if (props.GetBool("inputMuted") is { } inMuted) client.InputMuted = inMuted;
        if (props.GetBool("outputMuted") is { } outMuted) client.OutputMuted = outMuted;
        // Campo presente mas vazio = tirou a foto; ausente = não mudou
        if (props.TryGetProperty("myteamspeakAvatar", out _))
            client.AvatarUrl = TsMessages.ParseMyTeamSpeakAvatar(props.GetString("myteamspeakAvatar"));
    }
}
