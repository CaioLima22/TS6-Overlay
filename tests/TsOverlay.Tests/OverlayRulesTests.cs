using TsOverlay.Core.Overlay;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;

namespace TsOverlay.Tests;

public class OverlayRulesTests
{
    private const string Own = "TsOverlay";

    private static AppSettings LockedInApps(params string[] apps)
    {
        var settings = new AppSettings { Locked = true, ShowOnlyInApps = true };
        foreach (var app in apps) settings.ToggleApp(app);
        return settings;
    }

    [Fact]
    public void Shows_everywhere_when_the_app_list_is_off()
    {
        var settings = new AppSettings { Locked = true };
        Assert.True(OverlayRules.ShouldShow(false, settings, "chrome", Own));
    }

    [Fact]
    public void Shows_only_over_enabled_apps_when_locked()
    {
        var settings = LockedInApps("eldenring", "Discord");

        Assert.True(OverlayRules.ShouldShow(false, settings, "eldenring", Own));
        Assert.True(OverlayRules.ShouldShow(false, settings, "discord", Own)); // sem diferenciar maiúsculas
        Assert.False(OverlayRules.ShouldShow(false, settings, "chrome", Own));
        Assert.False(OverlayRules.ShouldShow(false, settings, null, Own));
    }

    [Fact]
    public void Disabled_app_stays_in_the_list_but_hides_the_overlay()
    {
        var settings = LockedInApps("eldenring");
        settings.ToggleApp("ELDENRING");

        Assert.Single(settings.Apps);
        Assert.False(settings.Apps[0].Enabled);
        Assert.False(OverlayRules.ShouldShow(false, settings, "eldenring", Own));

        settings.ToggleApp("eldenring");
        Assert.True(OverlayRules.ShouldShow(false, settings, "eldenring", Own));
    }

    [Fact]
    public void Unlocked_always_shows_so_it_can_be_positioned()
    {
        var settings = LockedInApps("eldenring");
        settings.Locked = false;
        Assert.True(OverlayRules.ShouldShow(false, settings, "chrome", Own));
    }

    [Fact]
    public void Own_window_in_foreground_keeps_it_visible()
    {
        Assert.True(OverlayRules.ShouldShow(false, LockedInApps(), Own, Own));
    }

    [Fact]
    public void Hidden_by_user_wins_over_everything()
    {
        Assert.False(OverlayRules.ShouldShow(true, new AppSettings(), "eldenring", Own));
        Assert.False(OverlayRules.ShouldShow(true, LockedInApps("eldenring"), "eldenring", Own));
    }

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
    public void Phase_3_options_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "TsOverlayTests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var settings = new AppSettings { OnlyTalking = true, ShowOnlyInApps = true };
            settings.ToggleApp("eldenring");
            settings.ToggleApp("chrome");
            settings.ToggleApp("chrome");
            settings.Save(path);

            var loaded = AppSettings.Load(path);
            Assert.True(loaded.OnlyTalking);
            Assert.True(loaded.ShowOnlyInApps);
            Assert.True(loaded.IsAppEnabled("eldenring"));
            Assert.False(loaded.IsAppEnabled("chrome"));
            Assert.Equal(2, loaded.Apps.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Null_app_list_in_file_loads_as_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "TsOverlayTests-" + Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """{ "Apps": null, "ShowOnlyInApps": true }""");
            var loaded = AppSettings.Load(path);
            Assert.NotNull(loaded.Apps);
            Assert.False(loaded.IsAppEnabled("eldenring"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
