using TsOverlay.Core.Ts;

namespace TsOverlay.Tests;

public class TsMessagesTests
{
    [Theory]
    [InlineData("2,https://storage.googleapis.com/ts-sys-myts-avatars/abc/17", "https://storage.googleapis.com/ts-sys-myts-avatars/abc/17")]
    [InlineData("https://storage.googleapis.com/ts-sys-myts-avatars/abc/17", "https://storage.googleapis.com/ts-sys-myts-avatars/abc/17")]
    [InlineData(" 2,https://example.com/a.png ", "https://example.com/a.png")]
    public void MyTeamSpeak_avatar_keeps_only_the_https_address(string raw, string expected)
    {
        Assert.Equal(expected, TsMessages.ParseMyTeamSpeakAvatar(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2,")]
    [InlineData("2,http://example.com/a.png")]
    [InlineData("2,file:///C:/Windows/win.ini")]
    [InlineData("lixo")]
    public void MyTeamSpeak_avatar_rejects_anything_else(string? raw)
    {
        Assert.Null(TsMessages.ParseMyTeamSpeakAvatar(raw));
    }

    [Theory]
    [InlineData("Lobby", "Lobby")]
    [InlineData("[cspacer]Geral", "Geral")]
    [InlineData("[*spacer1]---", "---")]
    [InlineData("[rspacer3] AFK ", "AFK")]
    [InlineData("[Time A] Ranked", "[Time A] Ranked")]
    public void Channel_name_hides_spacer_markup(string raw, string expected)
    {
        Assert.Equal(expected, TsMessages.CleanChannelName(raw));
    }
}
