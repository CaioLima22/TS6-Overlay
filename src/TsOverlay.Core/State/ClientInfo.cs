namespace TsOverlay.Core.State;

/// <summary>Um usuário como aparece no overlay. Imutável, para a UI poder ler sem lock.</summary>
public sealed record ClientInfo(
    string ConnectionId,
    string ClientId,
    string Nickname,
    bool IsTalking,
    bool InputMuted,
    bool OutputMuted,
    bool IsMe,
    string? AvatarUrl = null);
