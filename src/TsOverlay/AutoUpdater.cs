using System.Reflection;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace TsOverlay;

/// <summary>
/// Atualização automática pelas releases do GitHub (RF14). Checa ao abrir e a cada 6 h,
/// baixa em segundo plano e aplica ao sair, ou na hora se o usuário pedir pela bandeja.
/// Também dá para checar na mão pelo menu da bandeja.
/// Rodando fora da instalação (dotnet run) não faz nada.
/// </summary>
internal sealed class AutoUpdater : IDisposable
{
    public const string RepoUrl = "https://github.com/CaioLima22/TS6-Overlay";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false, null), null, null);
    private readonly DispatcherTimer _timer = new();
    private VelopackAsset? _pending;
    private bool _checking;

    public enum CheckResult { NotInstalled, AlreadyChecking, UpToDate, Downloaded, AlreadyPending, Failed }

    public AutoUpdater()
    {
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync();
        };
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.IsInstalled
        ? _manager.CurrentVersion?.ToString() ?? "0.0.0"
        : Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Versão já baixada esperando para ser aplicada.</summary>
    public string? PendingVersion => _pending?.Version?.ToString();

    /// <summary>Tem uma checagem ou download rolando agora.</summary>
    public bool IsChecking => _checking;

    /// <summary>Disparado no thread de UI quando uma atualização termina de baixar.</summary>
    public event Action<string>? UpdateReady;

    public void Start()
    {
        if (!IsInstalled) return;
        _timer.Interval = FirstCheckDelay;
        _timer.Start();
    }

    /// <param name="onDownloading">Chamado com a versão nova quando acha uma e começa a baixar.</param>
    public async Task<CheckResult> CheckAsync(Action<string>? onDownloading = null)
    {
        if (!IsInstalled) return CheckResult.NotInstalled;
        if (_pending is not null) return CheckResult.AlreadyPending;
        if (_checking) return CheckResult.AlreadyChecking;
        _checking = true;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return CheckResult.UpToDate;
            onDownloading?.Invoke(info.TargetFullRelease.Version?.ToString() ?? "");
            await _manager.DownloadUpdatesAsync(info, null, CancellationToken.None);
            _pending = info.TargetFullRelease;
            UpdateReady?.Invoke(PendingVersion ?? "");
            return CheckResult.Downloaded;
        }
        catch (Exception)
        {
            // Sem internet ou GitHub fora do ar: tenta de novo na próxima checagem
            return CheckResult.Failed;
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Fecha o app, aplica a atualização e abre de novo.</summary>
    public void ApplyNowAndRestart()
    {
        if (_pending is not null) _manager.ApplyUpdatesAndRestart(_pending, Array.Empty<string>());
    }

    public void Dispose()
    {
        _timer.Stop();
        // Atualização baixada e não aplicada: aplica em silêncio quando o processo terminar
        if (_pending is not null)
        {
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false, Array.Empty<string>());
        }
    }
}
