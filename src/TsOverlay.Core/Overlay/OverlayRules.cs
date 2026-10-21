using TsOverlay.Core.Settings;
using TsOverlay.Core.State;

namespace TsOverlay.Core.Overlay;

/// <summary>Regras de quando e o que o overlay mostra, sem depender da UI.</summary>
public static class OverlayRules
{
    /// <summary>
    /// Destravado sempre aparece, para dar para posicionar e configurar. Travado, respeita a
    /// lista de apps quando ela está ligada. Ocultar pela bandeja vale em qualquer caso.
    /// </summary>
    public static bool ShouldShow(bool hiddenByUser, AppSettings settings, string? foregroundProcess, string ownProcess)
    {
        if (hiddenByUser) return false;
        if (!settings.Locked || !settings.ShowOnlyInApps) return true;
        if (string.Equals(foregroundProcess, ownProcess, StringComparison.OrdinalIgnoreCase)) return true;
        return settings.IsAppEnabled(foregroundProcess);
    }

    /// <summary>No modo "só quem fala" a lista fica só com quem está falando.</summary>
    public static IReadOnlyList<ClientInfo> FilterClients(IReadOnlyList<ClientInfo> clients, bool onlyTalking) =>
        onlyTalking ? clients.Where(c => c.IsTalking).ToList() : clients;
}
