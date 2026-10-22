using System.Drawing;
using System.Windows.Forms;
using TsOverlay.Core.Settings;
using TsOverlay.Native;

namespace TsOverlay;

/// <summary>Ícone na bandeja: mostrar/ocultar, travar, opções, atualizações e sair.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly OverlayWindow _window;
    private readonly AppSettings _settings;
    private readonly AutoUpdater _updater;
    private readonly Action _exit;
    private readonly Action _restartToUpdate;

    public TrayIcon(OverlayWindow window, AppSettings settings, AutoUpdater updater, Action exit, Action restartToUpdate)
    {
        _window = window;
        _settings = settings;
        _updater = updater;
        _exit = exit;
        _restartToUpdate = restartToUpdate;

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => BuildMenu(menu);
        BuildMenu(menu);

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "TS Overlay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _updater.UpdateReady += version =>
            _icon.ShowBalloonTip(8000, "Atualização do TS Overlay",
                $"A versão {version} foi baixada e entra quando você sair. Clique aqui para reiniciar agora.",
                ToolTipIcon.Info);
        // Os balões de checagem manual também caem aqui; só reinicia se tiver o que aplicar
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_updater.PendingVersion is not null) _restartToUpdate();
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _window.HiddenByUser = !_window.HiddenByUser;
        };
    }

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        menu.Items.Add($"{(_window.HiddenByUser ? "Mostrar" : "Ocultar")} overlay ({_window.HideHotkey})", null,
            (_, _) => _window.HiddenByUser = !_window.HiddenByUser);
        menu.Items.Add($"{(_settings.Locked ? "Destravar" : "Travar")} ({_window.LockHotkey})", null,
            (_, _) => _window.ToggleLocked());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Check("Mostrar só quem está falando", _settings.OnlyTalking, () =>
        {
            _settings.OnlyTalking = !_settings.OnlyTalking;
            _window.ApplySettings();
        }));
        menu.Items.Add(Check("Ocultar fora de servidor", _settings.AutoHideWhenNoServer, () =>
        {
            _settings.AutoHideWhenNoServer = !_settings.AutoHideWhenNoServer;
            _window.ApplySettings();
        }));
        menu.Items.Add(Check("Iniciar com o Windows", StartupRegistration.IsEnabled, () =>
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled)));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(BuildVersionItem());
        if (_updater.IsInstalled && _updater.PendingVersion is null)
        {
            menu.Items.Add(new ToolStripMenuItem(
                _updater.IsChecking ? "Verificando atualizações..." : "Verificar atualizações", null,
                async (_, _) => await CheckForUpdatesManuallyAsync()) { Enabled = !_updater.IsChecking });
        }
        menu.Items.Add("Sair", null, (_, _) => _exit());
    }

    private ToolStripMenuItem BuildVersionItem()
    {
        if (_updater.PendingVersion is { } pending)
        {
            return new ToolStripMenuItem($"Reiniciar e atualizar para a v{pending}", null, (_, _) => _restartToUpdate());
        }
        var text = _updater.IsInstalled
            ? $"TS Overlay v{_updater.CurrentVersion}"
            : $"TS Overlay v{_updater.CurrentVersion} (sem atualização automática fora do instalador)";
        return new ToolStripMenuItem(text) { Enabled = false };
    }

    private async Task CheckForUpdatesManuallyAsync()
    {
        var result = await _updater.CheckAsync(version =>
            Balloon($"Achei a versão {version}. Baixando em segundo plano..."));
        switch (result)
        {
            case AutoUpdater.CheckResult.UpToDate:
                Balloon($"Você já está na versão mais recente (v{_updater.CurrentVersion}).");
                break;
            case AutoUpdater.CheckResult.AlreadyChecking:
                Balloon("Já estou verificando, aguarde um instante.");
                break;
            case AutoUpdater.CheckResult.AlreadyPending:
                Balloon($"A versão {_updater.PendingVersion} já está baixada. Clique aqui para reiniciar agora.");
                break;
            case AutoUpdater.CheckResult.Failed:
                Balloon("Não consegui verificar agora. Confira a internet e tente de novo.", ToolTipIcon.Warning);
                break;
            // Downloaded: o aviso de "pronta para aplicar" já sai pelo UpdateReady
        }
    }

    private void Balloon(string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(5000, "Atualização do TS Overlay", text, icon);

    private static ToolStripMenuItem Check(string text, bool isChecked, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>O mesmo ícone do .exe, no tamanho que o Windows usa na bandeja.</summary>
    private static Icon CreateIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("TsOverlay.app.ico")!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
