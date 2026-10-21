using System.Text.Json;

namespace TsOverlay.Core.Settings;

/// <summary>
/// Configurações em %AppData%\TsOverlay\settings.json. Ficam fora da pasta do app
/// para sobreviver a atualizações, e a apiKey nunca vai para o repositório.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsOverlay", "settings.json");

    /// <summary>Chave devolvida pelo TS depois da aprovação. Vazia = pedir aprovação de novo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Porta da Remote Apps API (Configurações → Remote Apps no TS6).</summary>
    public int Port { get; set; } = 5899;

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo corrompido ou ilegível: começa do zero
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
