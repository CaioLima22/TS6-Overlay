using System.Text.Json;

namespace TsOverlay.Core.Ts;

/// <summary>
/// Leitura tolerante do JSON da Remote Apps API. A API não tem documentação oficial
/// e muda entre betas, então todo campo é opcional e ids podem vir como número ou string.
/// </summary>
public static class TsMessages
{
    public static JsonElement? Get(this JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return value;
        }
        return null;
    }

    /// <summary>Lê um id (número ou string) e normaliza para string. "0" e vazio viram null.</summary>
    public static string? GetId(this JsonElement element, string name)
    {
        var raw = element.GetString(name);
        return string.IsNullOrEmpty(raw) || raw == "0" ? null : raw;
    }

    public static string? GetString(this JsonElement element, string name)
    {
        return element.Get(name) switch
        {
            { ValueKind: JsonValueKind.String } v => v.GetString(),
            { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
            _ => null,
        };
    }

    public static bool? GetBool(this JsonElement element, string name)
    {
        return element.Get(name) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.Number } v => v.GetDouble() != 0,
            { ValueKind: JsonValueKind.String } v => v.GetString() is "1" or "true",
            _ => null,
        };
    }

    public static int? GetInt(this JsonElement element, string name)
    {
        return element.Get(name) switch
        {
            { ValueKind: JsonValueKind.Number } v when v.TryGetInt32(out var i) => i,
            { ValueKind: JsonValueKind.String } v when int.TryParse(v.GetString(), out var i) => i,
            _ => null,
        };
    }

    public static IEnumerable<JsonElement> GetArray(this JsonElement element, string name)
    {
        return element.Get(name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }

    /// <summary>
    /// Foto do myTeamSpeak, que a API manda como "2,https://storage.googleapis.com/ts-sys-myts-avatars/...".
    /// Devolve só o endereço, e só se for https, para nunca baixar de outro tipo de lugar.
    /// </summary>
    public static string? ParseMyTeamSpeakAvatar(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        var comma = value.IndexOf(',');
        if (comma >= 0 && value[..comma].All(char.IsDigit)) value = value[(comma + 1)..];
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Monta a mensagem de autenticação enviada ao abrir o WebSocket.</summary>
    public static string BuildAuth(string identifier, string version, string name, string description, string? apiKey)
    {
        return JsonSerializer.Serialize(new
        {
            type = "auth",
            payload = new
            {
                identifier,
                version,
                name,
                description,
                content = new { apiKey = apiKey ?? "" },
            },
        });
    }
}
