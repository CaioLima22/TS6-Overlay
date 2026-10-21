using TsOverlay.Core.Settings;

namespace TsOverlay.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TsOverlayTests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = AppSettings.Load(SettingsPath);

        Assert.Null(settings.ApiKey);
        Assert.Null(settings.WindowLeft);
        Assert.Equal(0.9, settings.Opacity);
        Assert.False(settings.Locked);
    }

    [Fact]
    public void Position_opacity_and_lock_survive_a_restart()
    {
        new AppSettings { ApiKey = "k", WindowLeft = 100, WindowTop = -50, Opacity = 0.6, Locked = true }.Save(SettingsPath);

        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal("k", loaded.ApiKey);
        Assert.Equal(100, loaded.WindowLeft);
        Assert.Equal(-50, loaded.WindowTop);
        Assert.Equal(0.6, loaded.Opacity);
        Assert.True(loaded.Locked);
    }

    [Fact]
    public void Settings_from_phase_1_still_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "ApiKey": "antiga", "Port": 5899 }""");

        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal("antiga", loaded.ApiKey);
        Assert.Equal(0.9, loaded.Opacity);
        Assert.False(loaded.Locked);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ isso não é json");

        Assert.Null(AppSettings.Load(SettingsPath).ApiKey);
    }

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(5.0, 1.0)]
    [InlineData(0.55, 0.55)]
    [InlineData(double.NaN, 0.9)]
    public void Opacity_is_clamped(double value, double expected)
    {
        Assert.Equal(expected, AppSettings.ClampOpacity(value));
    }

    [Theory]
    // Tela única 1920x1080 começando em (0,0)
    [InlineData(20, 20, true)]
    [InlineData(1700, 900, true)]
    [InlineData(-200, 20, true)]     // metade para fora à esquerda ainda dá para pegar
    [InlineData(2500, 20, false)]    // estava num segundo monitor que sumiu
    [InlineData(20, -30, false)]     // topo acima da tela: não daria para arrastar
    [InlineData(20, 1070, false)]    // quase toda abaixo da tela
    public void Saved_position_must_be_on_screen(double left, double top, bool expected)
    {
        Assert.Equal(expected, AppSettings.IsOnScreen(left, top, 260, 0, 0, 1920, 1080));
    }
}
