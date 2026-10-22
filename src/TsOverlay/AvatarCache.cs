using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TsOverlay;

/// <summary>
/// Baixa as fotos do myTeamSpeak em segundo plano e guarda em %LocalAppData%\TsOverlay\avatars.
/// O endereço muda quando a pessoa troca de foto, então o arquivo de um endereço nunca fica velho.
/// </summary>
public sealed class AvatarCache
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int DecodeSize = 64; // o círculo tem 24 px; sobra para telas com escala alta

    public static AvatarCache Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, ImageSource?> _images = new();
    private readonly HashSet<string> _loading = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TsOverlay", "avatars");

    /// <summary>Uma foto terminou de carregar (ou falhou). Vem de qualquer thread.</summary>
    public event Action? Changed;

    /// <summary>A foto, se já estiver pronta. Senão começa a baixar e devolve null.</summary>
    public ImageSource? Get(string url)
    {
        lock (_lock)
        {
            if (_images.TryGetValue(url, out var image)) return image;
            if (!_loading.Add(url)) return null;
        }
        _ = LoadAsync(url);
        return null;
    }

    private async Task LoadAsync(string url)
    {
        ImageSource? image = null;
        try
        {
            var path = Path.Combine(_dir, FileName(url));
            var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path) : await DownloadAsync(url);
            if (bytes is not null)
            {
                image = Decode(bytes);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(_dir);
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }
        }
        catch (Exception)
        {
            // Sem rede, foto apagada ou arquivo estranho: fica a silhueta padrão
        }

        lock (_lock)
        {
            _images[url] = image;
            _loading.Remove(url);
        }
        Changed?.Invoke();
    }

    private async Task<byte[]?> DownloadAsync(string url)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) return null;
        }
        return buffer.Length > 0 ? buffer.ToArray() : null;
    }

    // Lança se não for uma imagem, e aí nada vai para o disco
    private static ImageSource Decode(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = DecodeSize;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze(); // permite usar no thread de UI depois de criar aqui
        return bitmap;
    }

    private static string FileName(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".img";
}

/// <summary>AvatarUrl → foto pronta, ou null (o template mostra a silhueta).</summary>
public sealed class AvatarConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string url ? AvatarCache.Instance.Get(url) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
