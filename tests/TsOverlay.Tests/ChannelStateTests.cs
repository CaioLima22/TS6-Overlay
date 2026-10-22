using TsOverlay.Core.State;

namespace TsOverlay.Tests;

public class ChannelStateTests
{
    // Estado inicial: conexão 1, eu sou o cliente 10 no canal 5, com Ana (11) no mesmo canal
    // e Beto (12) no canal 6. Ids misturam número e string de propósito.
    private const string Auth = """
        {
          "type": "auth",
          "payload": {
            "apiKey": "abc",
            "connections": [{
              "id": 1,
              "clientId": 10,
              "clientInfos": [
                { "id": 10, "channelId": 5, "properties": { "nickname": "Caio", "inputMuted": false, "outputMuted": false } },
                { "id": 11, "channelId": "5", "properties": { "nickname": "Ana", "inputMuted": true } },
                { "id": "12", "channelId": 6, "properties": { "nickname": "Beto" } }
              ]
            }]
          }
        }
        """;

    private static ChannelState CreateState()
    {
        var state = new ChannelState();
        state.ApplyJson(Auth);
        return state;
    }

    private static string[] Names(ChannelState state) =>
        state.GetVisibleClients().Select(c => c.Nickname).ToArray();

    [Fact]
    public void Auth_lists_only_my_channel_including_me()
    {
        var state = CreateState();

        Assert.Equal(new[] { "Ana", "Caio" }, Names(state));
        var clients = state.GetVisibleClients();
        Assert.True(clients.Single(c => c.Nickname == "Caio").IsMe);
        Assert.True(clients.Single(c => c.Nickname == "Ana").InputMuted);
    }

    [Fact]
    public void Server_query_clients_are_hidden()
    {
        var state = CreateState();

        // O TS não mostra clientes ServerQuery, mas a API manda (type 1)
        state.ApplyJson("""{ "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 13, "newChannelId": 5,
              "properties": { "nickname": "serveradmin", "type": 1 } } }""");
        Assert.Equal(new[] { "Ana", "Caio" }, Names(state));

        // E quando sai não vira "último que saiu"
        state.ApplyJson("""{ "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 13, "newChannelId": 6 } }""");
        Assert.Null(state.GetLastLeft());

        // type 0 é cliente normal
        state.ApplyJson("""{ "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 14, "newChannelId": 5,
              "properties": { "nickname": "Duda", "type": 0 } } }""");
        Assert.Equal(new[] { "Ana", "Caio", "Duda" }, Names(state));
    }

    [Fact]
    public void Server_query_clients_from_auth_are_hidden()
    {
        var state = new ChannelState();
        state.ApplyJson(Auth.Replace("""{ "id": "12", "channelId": 6, "properties": { "nickname": "Beto" } }""",
            """{ "id": "12", "channelId": 5, "properties": { "nickname": "serveradmin", "type": "1" } }"""));

        Assert.Equal(new[] { "Ana", "Caio" }, Names(state));
    }

    [Fact]
    public void Talk_status_lights_and_clears_the_name()
    {
        var state = CreateState();

        state.ApplyJson("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 11, "status": 1 } }""");
        Assert.True(state.GetVisibleClients().Single(c => c.Nickname == "Ana").IsTalking);

        state.ApplyJson("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 11, "status": 0 } }""");
        Assert.False(state.GetVisibleClients().Single(c => c.Nickname == "Ana").IsTalking);
    }

    [Fact]
    public void Client_joining_my_channel_appears()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 12, "oldChannelId": 6, "newChannelId": 5 } }
            """);

        Assert.Equal(new[] { "Ana", "Beto", "Caio" }, Names(state));
    }

    [Fact]
    public void New_client_connecting_to_server_appears_with_properties()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 13, "oldChannelId": 0, "newChannelId": 5,
              "properties": { "nickname": "Duda", "outputMuted": true } } }
            """);

        var duda = state.GetVisibleClients().Single(c => c.Nickname == "Duda");
        Assert.True(duda.OutputMuted);
    }

    [Fact]
    public void Client_leaving_the_server_disappears()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 0 } }
            """);

        Assert.Equal(new[] { "Caio" }, Names(state));
    }

    [Fact]
    public void When_I_change_channel_the_list_follows_me()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 10, "oldChannelId": 5, "newChannelId": 6 } }
            """);

        Assert.Equal(new[] { "Beto", "Caio" }, Names(state));
    }

    [Fact]
    public void Client_leaving_my_channel_becomes_last_left()
    {
        var state = CreateState();
        Assert.Null(state.GetLastLeft());

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 6 } }
            """);

        Assert.Equal("Ana", state.GetLastLeft()?.Nickname);
        Assert.Equal(new[] { "Caio" }, Names(state));
    }

    [Fact]
    public void Client_leaving_the_server_from_my_channel_becomes_last_left()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 0 } }
            """);

        Assert.Equal("Ana", state.GetLastLeft()?.Nickname);
    }

    [Fact]
    public void Moves_outside_my_channel_do_not_touch_last_left()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 12, "oldChannelId": 6, "newChannelId": 0 } }
            """);

        Assert.Null(state.GetLastLeft());
    }

    [Fact]
    public void Last_left_clears_when_the_person_comes_back()
    {
        var state = CreateState();
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 6 } }
            """);

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 6, "newChannelId": 5 } }
            """);

        Assert.Null(state.GetLastLeft());
    }

    [Fact]
    public void Last_left_clears_when_I_change_channel()
    {
        var state = CreateState();
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 0 } }
            """);

        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 10, "oldChannelId": 5, "newChannelId": 6 } }
            """);

        Assert.Null(state.GetLastLeft());
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Last_left_disappears_after_five_minutes()
    {
        var time = new ManualTime();
        var state = new ChannelState(time);
        state.ApplyJson(Auth);
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 1, "clientId": 11, "oldChannelId": 5, "newChannelId": 0 } }
            """);

        time.Now += TimeSpan.FromMinutes(4);
        Assert.Equal(time.Now + TimeSpan.FromMinutes(1), state.GetLastLeft()?.ExpiresAt);

        time.Now += TimeSpan.FromMinutes(1);
        Assert.Null(state.GetLastLeft());
    }

    [Fact]
    public void Properties_update_changes_nickname_and_mute()
    {
        var state = CreateState();

        state.ApplyJson("""
            { "type": "clientPropertiesUpdated", "payload": { "connectionId": 1, "clientId": 11,
              "properties": { "nickname": "Ana Paula", "inputMuted": false } } }
            """);

        var ana = state.GetVisibleClients().Single(c => c.ClientId == "11");
        Assert.Equal("Ana Paula", ana.Nickname);
        Assert.False(ana.InputMuted);
    }

    [Fact]
    public void Disconnecting_from_the_server_clears_the_list()
    {
        var state = CreateState();

        state.ApplyJson("""{ "type": "connectStatusChanged", "payload": { "connectionId": 1, "status": 0 } }""");

        Assert.Empty(state.GetVisibleClients());
        Assert.Null(state.ActiveConnectionId);
    }

    [Fact]
    public void Connecting_to_a_new_server_builds_the_list_from_moves()
    {
        var state = new ChannelState();
        state.ApplyJson("""{ "type": "auth", "payload": { "apiKey": "abc", "connections": [] } }""");

        state.ApplyJson("""{ "type": "connectStatusChanged", "payload": { "connectionId": 2, "status": 4, "info": { "clientId": 20 } } }""");
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 2, "clientId": 20, "oldChannelId": 0, "newChannelId": 1,
              "properties": { "nickname": "Caio" } } }
            """);
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 2, "clientId": 21, "oldChannelId": 0, "newChannelId": 1,
              "properties": { "nickname": "Ana" } } }
            """);

        Assert.Equal("2", state.ActiveConnectionId);
        Assert.Equal(new[] { "Ana", "Caio" }, Names(state));
    }

    [Fact]
    public void Tab_that_gains_input_hardware_becomes_active()
    {
        var state = CreateState();
        state.ApplyJson("""{ "type": "connectStatusChanged", "payload": { "connectionId": 2, "status": 4, "info": { "clientId": 20 } } }""");
        state.ApplyJson("""
            { "type": "clientMoved", "payload": { "connectionId": 2, "clientId": 20, "oldChannelId": 0, "newChannelId": 1,
              "properties": { "nickname": "Caio (outro servidor)" } } }
            """);
        Assert.Equal("1", state.ActiveConnectionId);

        // A aba antiga perde o microfone: não muda nada
        state.ApplyJson("""{ "type": "clientSelfPropertyUpdated", "payload": { "connectionId": 1, "flag": "inputHardware", "newValue": false } }""");
        Assert.Equal("1", state.ActiveConnectionId);

        state.ApplyJson("""{ "type": "clientSelfPropertyUpdated", "payload": { "connectionId": 2, "flag": "inputHardware", "newValue": true } }""");
        Assert.Equal("2", state.ActiveConnectionId);
        Assert.Equal(new[] { "Caio (outro servidor)" }, Names(state));
    }

    [Fact]
    public void Unknown_events_and_clients_are_ignored()
    {
        var state = CreateState();
        var changes = 0;
        state.Changed += () => changes++;

        state.ApplyJson("""{ "type": "algoNovoDaBeta6", "payload": { "x": 1 } }""");
        state.ApplyJson("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 999, "status": 1 } }""");

        Assert.Equal(0, changes);
        Assert.Equal(new[] { "Ana", "Caio" }, Names(state));
    }

    [Fact]
    public void Changed_fires_on_talk()
    {
        var state = CreateState();
        var changes = 0;
        state.Changed += () => changes++;

        state.ApplyJson("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 10, "status": 1 } }""");
        // Mesmo status de novo não redesenha
        state.ApplyJson("""{ "type": "talkStatusChanged", "payload": { "connectionId": 1, "clientId": 10, "status": 1 } }""");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void MyTeamSpeak_avatar_is_read_and_cleared()
    {
        var state = CreateState();
        Assert.Null(state.GetVisibleClients().Single(c => c.Nickname == "Ana").AvatarUrl);

        state.ApplyJson("""{ "type": "clientPropertiesUpdated", "payload": { "connectionId": 1, "clientId": 11, "properties": { "myteamspeakAvatar": "2,https://storage.googleapis.com/ts-sys-myts-avatars/abc-123/1700000000" } } }""");
        Assert.Equal("https://storage.googleapis.com/ts-sys-myts-avatars/abc-123/1700000000",
            state.GetVisibleClients().Single(c => c.Nickname == "Ana").AvatarUrl);

        // Atualização sem o campo não apaga a foto
        state.ApplyJson("""{ "type": "clientPropertiesUpdated", "payload": { "connectionId": 1, "clientId": 11, "properties": { "inputMuted": false } } }""");
        Assert.NotNull(state.GetVisibleClients().Single(c => c.Nickname == "Ana").AvatarUrl);

        state.ApplyJson("""{ "type": "clientPropertiesUpdated", "payload": { "connectionId": 1, "clientId": 11, "properties": { "myteamspeakAvatar": "" } } }""");
        Assert.Null(state.GetVisibleClients().Single(c => c.Nickname == "Ana").AvatarUrl);
    }
}
