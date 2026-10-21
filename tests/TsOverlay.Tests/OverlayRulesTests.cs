using TsOverlay.Core.Overlay;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;

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
}
