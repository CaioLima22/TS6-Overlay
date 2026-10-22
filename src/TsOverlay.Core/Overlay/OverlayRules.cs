using TsOverlay.Core.State;
using TsOverlay.Core.Ts;

namespace TsOverlay.Core.Overlay;

/// <summary>Regras do que o overlay mostra, sem depender da UI.</summary>
public static class OverlayRules
{
    /// <summary>No modo "só quem fala" a lista fica só com quem está falando.</summary>
    public static IReadOnlyList<ClientInfo> FilterClients(IReadOnlyList<ClientInfo> clients, bool onlyTalking) =>
        onlyTalking ? clients.Where(c => c.IsTalking).ToList() : clients;

    /// <summary>
    /// Fora de qualquer servidor o overlay some sozinho e volta quando o TS conectar.
    /// Destravado continua visível para dar para mover e trocar os atalhos. Antes da
    /// primeira aprovação (sem apiKey) os avisos de configuração continuam aparecendo.
    /// </summary>
    public static bool ShouldAutoHide(bool enabled, bool locked, TsConnectionStatus status, bool inServer, bool hasApiKey) =>
        enabled && locked && !inServer && status switch
        {
            TsConnectionStatus.Connected => true,
            TsConnectionStatus.Connecting => hasApiKey, // TS fechado depois de já configurado
            _ => false, // esperando aprovação: o aviso precisa aparecer
        };

    /// <summary>
    /// Overlay na metade direita do monitor: a lista fica espelhada (foto à direita, nome
    /// encostado na borda) para não sobrar espaço vazio entre os nomes e a borda da tela.
    /// </summary>
    public static bool IsOnRightSide(double windowLeft, double windowWidth, double monitorLeft, double monitorWidth) =>
        windowLeft + windowWidth / 2 > monitorLeft + monitorWidth / 2;
}
