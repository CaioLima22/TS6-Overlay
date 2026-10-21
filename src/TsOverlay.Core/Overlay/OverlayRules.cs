using TsOverlay.Core.State;

namespace TsOverlay.Core.Overlay;

/// <summary>Regras do que o overlay mostra, sem depender da UI.</summary>
public static class OverlayRules
{
    /// <summary>No modo "só quem fala" a lista fica só com quem está falando.</summary>
    public static IReadOnlyList<ClientInfo> FilterClients(IReadOnlyList<ClientInfo> clients, bool onlyTalking) =>
        onlyTalking ? clients.Where(c => c.IsTalking).ToList() : clients;
}
