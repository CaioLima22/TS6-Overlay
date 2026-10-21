using System.Diagnostics;
using System.Windows;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;

namespace TsOverlay;

public partial class App : Application
{
    private TsRemoteClient? _client;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        var state = new ChannelState();
        _client = new TsRemoteClient(settings.Port, settings.ApiKey);

        _client.MessageReceived += state.Apply;
        _client.Disconnected += state.Clear;
        _client.ApiKeyChanged += key =>
        {
            settings.ApiKey = key;
            settings.Save();
        };
        if (e.Args.Contains("--debug"))
        {
            _client.RawMessage += text => Debug.WriteLine($"[TS] {text}");
        }

        var window = new OverlayWindow(state, _client);
        MainWindow = window;
        window.Show();

        _client.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _client?.Dispose();
        base.OnExit(e);
    }
}
