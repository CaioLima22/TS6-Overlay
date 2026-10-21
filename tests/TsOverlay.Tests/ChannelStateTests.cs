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
}
