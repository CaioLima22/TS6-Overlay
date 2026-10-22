using System.Text.Json;

namespace TsOverlay.Core.Settings;

/// <summary>
/// Configurações em %AppData%\TsOverlay\settings.json. Ficam fora da pasta do app
/// para sobreviver a atualizações, e a apiKey nunca vai para o repositório.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object SaveLock = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsOverlay", "settings.json");

    /// <summary>Chave devolvida pelo TS depois da aprovação. Vazia = pedir aprovação de novo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Porta da Remote Apps API (Configurações → Remote Apps no TS6).</summary>
    public int Port { get; set; } = 5899;

    /// <summary>Posição da janela. Null = posição padrão (canto superior esquerdo).</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>Opacidade da janela, de 0.2 a 1.</summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>Travado = cliques passam para o jogo. Destravado = dá para arrastar.</summary>
    public bool Locked { get; set; }

    /// <summary>Atalho global que trava e destrava, no formato "Ctrl+Shift+O".</summary>
    public string LockHotkey { get; set; } = Hotkey.Default.ToString();

    /// <summary>Atalho global que oculta e mostra o overlay.</summary>
    public string HideHotkey { get; set; } = Hotkey.DefaultHide.ToString();

    /// <summary>Mostra só quem está falando em vez do canal inteiro.</summary>
    public bool OnlyTalking { get; set; }

    /// <summary>Some sozinho enquanto o TS não estiver conectado a nenhum servidor.</summary>
    public bool AutoHideWhenNoServer { get; set; } = true;

    /// <summary>O atalho salvo, ou o padrão se o texto for inválido.</summary>
    public Hotkey GetLockHotkey() => Hotkey.TryParse(LockHotkey, out var hotkey) ? hotkey : Hotkey.Default;

    public Hotkey GetHideHotkey() => Hotkey.TryParse(HideHotkey, out var hotkey) ? hotkey : Hotkey.DefaultHide;

    public const double MinOpacity = 0.2;

    public static double ClampOpacity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinOpacity, 1.0) : 0.9;

    /// <summary>
    /// Confere se a posição salva ainda cai dentro da área de telas (um monitor pode ter sido
    /// desconectado). Exige que pelo menos um pedaço do topo da janela esteja visível.
    /// </summary>
    public static bool IsOnScreen(double left, double top, double width,
        double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        const double minVisible = 40;
        return left + width - minVisible >= screenLeft
            && left + minVisible <= screenLeft + screenWidth
            && top >= screenTop
            && top + minVisible <= screenTop + screenHeight;
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
                settings.Opacity = ClampOpacity(settings.Opacity);
                return settings;
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
        // A apiKey é salva pelo thread do WebSocket e a posição pelo thread de UI
        lock (SaveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
