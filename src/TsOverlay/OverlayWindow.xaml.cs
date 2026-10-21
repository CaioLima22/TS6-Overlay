using System.Windows;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;

namespace TsOverlay;

/// <summary>Só desenha: lê o ChannelState e nunca fala com o TS.</summary>
public partial class OverlayWindow : Window
{
    private readonly ChannelState _state;
    private readonly TsRemoteClient _client;
    private int _refreshQueued;

    public OverlayWindow(ChannelState state, TsRemoteClient client)
    {
        InitializeComponent();
        _state = state;
        _client = client;

        _state.Changed += QueueRefresh;
        _client.StatusChanged += _ => QueueRefresh();
        Refresh();
    }

    // Eventos chegam do thread do WebSocket; junta rajadas num único redesenho no thread de UI
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        var clients = _state.GetVisibleClients();
        ClientList.ItemsSource = clients;

        StatusText.Text = _client.Status switch
        {
            TsConnectionStatus.Connecting => "Procurando o TeamSpeak... Confira se Remote Apps está ativado nas configurações.",
            TsConnectionStatus.WaitingForApproval => "Aprove o TS Overlay na notificação do TeamSpeak.",
            _ when clients.Count == 0 => "Não conectado a nenhum servidor.",
            _ => "",
        };
        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
