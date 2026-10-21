using System.Diagnostics;
using System.Windows;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;

namespace TsOverlay;

public partial class App : Application
{
    private const string SingleInstanceName = "TsOverlay.SingleInstance";

    private TsRemoteClient? _client;
    private TrayIcon? _tray;
    private AutoUpdater? _updater;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Iniciar com o Windows + abrir na mão não pode dar dois overlays
        _singleInstance = new Mutex(true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        var state = new ChannelState();
        _updater = new AutoUpdater();
        _client = new TsRemoteClient(settings.Port, settings.ApiKey, _updater.CurrentVersion);

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

        var window = new OverlayWindow(state, _client, settings);
        MainWindow = window;
        window.Show();
        _tray = new TrayIcon(window, settings, _updater,
            exit: () =>
            {
                window.Close();
                Shutdown();
            },
            restartToUpdate: () =>
            {
                window.Close(); // salva posição e opções antes de o Velopack fechar o processo
                _tray?.Dispose();
                _updater.ApplyNowAndRestart();
            });
        _updater.Start();

        _client.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _updater?.Dispose();
        _client?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
