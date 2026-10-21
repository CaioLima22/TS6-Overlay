using TsOverlay.Core.Settings;

namespace TsOverlay.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+O", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x4F)]
    [InlineData("alt + f9", HotkeyModifiers.Alt, 0x78)]
    [InlineData("F10", HotkeyModifiers.None, 0x79)]
    [InlineData("Ctrl+Alt+Num5", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x65)]
    [InlineData("Win+Shift+Home", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x24)]
    [InlineData("Ctrl+1", HotkeyModifiers.Ctrl, 0x31)]
    public void Parses_valid_hotkeys(string text, HotkeyModifiers modifiers, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, vk), hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]        // falta a tecla
    [InlineData("O")]                 // letra sozinha roubaria a digitação
    [InlineData("Ctrl+O+P")]          // duas teclas
    [InlineData("Ctrl+Banana")]
    public void Rejects_invalid_hotkeys(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(Hotkey.Default, hotkey);
    }

    [Fact]
    public void Text_round_trips_in_a_fixed_order()
    {
        Assert.True(Hotkey.TryParse("shift+ctrl+alt+pageup", out var hotkey));
        Assert.Equal("Ctrl+Alt+Shift+PageUp", hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Fact]
    public void Default_is_ctrl_shift_o()
    {
        Assert.Equal("Ctrl+Shift+O", Hotkey.Default.ToString());
        Assert.Equal("Ctrl+Shift+O", new AppSettings().LockHotkey);
    }

    [Fact]
    public void Invalid_saved_hotkey_falls_back_to_default()
    {
        var settings = new AppSettings { LockHotkey = "isso não é atalho" };
        Assert.Equal(Hotkey.Default, settings.GetLockHotkey());

        settings.LockHotkey = "Ctrl+Alt+F8";
        Assert.Equal("Ctrl+Alt+F8", settings.GetLockHotkey().ToString());
    }
}
