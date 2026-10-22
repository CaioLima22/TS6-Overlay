using TsOverlay.Core.Overlay;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;

namespace TsOverlay.Tests;

public class OverlayRulesTests
{
    [Fact]
    public void Only_talking_filters_the_list()
    {
        var clients = new List<ClientInfo>
        {
            new("1", "10", "Ana", IsTalking: true, false, false, false),
            new("1", "11", "Beto", IsTalking: false, false, false, false),
        };

        Assert.Equal(2, OverlayRules.FilterClients(clients, onlyTalking: false).Count);
        Assert.Equal("Ana", Assert.Single(OverlayRules.FilterClients(clients, onlyTalking: true)).Nickname);
    }

    [Fact]
    public void Only_talking_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "TsOverlayTests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            new AppSettings { OnlyTalking = true }.Save(path);
            Assert.True(AppSettings.Load(path).OnlyTalking);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Settings_with_the_removed_app_list_still_load()
    {
        // Quem usou a v0.1.0 tem "ShowOnlyInApps" e "Apps" salvos; os campos são ignorados
        var path = Path.Combine(Path.GetTempPath(), "TsOverlayTests-" + Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """
                { "ApiKey": "k", "OnlyTalking": true, "ShowOnlyInApps": true,
                  "Apps": [ { "ProcessName": "League of Legends", "Enabled": true } ] }
                """);
            var loaded = AppSettings.Load(path);
            Assert.Equal("k", loaded.ApiKey);
            Assert.True(loaded.OnlyTalking);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    // Conectado ao TS mas fora de servidor: some
    [InlineData(TsConnectionStatus.Connected, false, true, true)]
    // Entrou num servidor: volta
    [InlineData(TsConnectionStatus.Connected, true, true, false)]
    // TS fechado depois de já aprovado: também está fora de servidor
    [InlineData(TsConnectionStatus.Connecting, false, true, true)]
    // Primeira vez, sem aprovação: o aviso de configuração precisa aparecer
    [InlineData(TsConnectionStatus.Connecting, false, false, false)]
    [InlineData(TsConnectionStatus.WaitingForApproval, false, true, false)]
    public void Auto_hides_only_outside_a_server(TsConnectionStatus status, bool inServer, bool hasApiKey, bool expected)
    {
        Assert.Equal(expected, OverlayRules.ShouldAutoHide(enabled: true, locked: true, status, inServer, hasApiKey));
    }

    [Fact]
    public void Auto_hide_respects_the_option_and_the_unlocked_mode()
    {
        Assert.False(OverlayRules.ShouldAutoHide(enabled: false, locked: true, TsConnectionStatus.Connected, inServer: false, hasApiKey: true));
        // Destravado continua visível para mover e trocar os atalhos
        Assert.False(OverlayRules.ShouldAutoHide(enabled: true, locked: false, TsConnectionStatus.Connected, inServer: false, hasApiKey: true));
    }
}
