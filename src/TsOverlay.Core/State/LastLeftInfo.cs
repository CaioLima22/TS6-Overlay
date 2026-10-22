namespace TsOverlay.Core.State;

/// <summary>Quem saiu por último do meu canal e até quando ainda aparece no overlay.</summary>
public sealed record LastLeftInfo(string Nickname, DateTimeOffset ExpiresAt);
