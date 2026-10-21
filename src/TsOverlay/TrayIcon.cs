using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TsOverlay.Core.Settings;
using TsOverlay.Native;

namespace TsOverlay;

/// <summary>Ícone na bandeja: mostrar/ocultar, travar, opções e sair.</summary>
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
        _icon.BalloonTipClicked += (_, _) => _restartToUpdate();
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _window.HiddenByUser = !_window.HiddenByUser;
        };
    }

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        menu.Items.Add(_window.HiddenByUser ? "Mostrar overlay" : "Ocultar overlay", null,
            (_, _) => _window.HiddenByUser = !_window.HiddenByUser);
        menu.Items.Add($"{(_settings.Locked ? "Destravar" : "Travar")} ({_window.LockHotkey})", null,
            (_, _) => _window.ToggleLocked());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Check("Mostrar só quem está falando", _settings.OnlyTalking, () =>
        {
            _settings.OnlyTalking = !_settings.OnlyTalking;
            _window.ApplySettings();
        }));
        menu.Items.Add(Check("Mostrar só nos apps escolhidos", _settings.ShowOnlyInApps, () =>
        {
            _settings.ShowOnlyInApps = !_settings.ShowOnlyInApps;
            _window.ApplySettings();
        }));
        menu.Items.Add(BuildAppsMenu());
        menu.Items.Add(Check("Iniciar com o Windows", StartupRegistration.IsEnabled, () =>
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled)));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(BuildUpdateItem());
        menu.Items.Add("Sair", null, (_, _) => _exit());
    }

    private ToolStripMenuItem BuildUpdateItem()
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

    /// <summary>Apps abertos agora e os já salvos; marcado = o overlay aparece nele.</summary>
    private ToolStripMenuItem BuildAppsMenu()
    {
        var item = new ToolStripMenuItem("Apps e jogos");
        item.DropDownItems.Add(new ToolStripMenuItem("Marque onde o overlay aparece") { Enabled = false });
        item.DropDownItems.Add(new ToolStripSeparator());

        var names = RunningWindowedProcesses()
            .Concat(_settings.Apps.Select(a => a.ProcessName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            item.DropDownItems.Add(Check(name, _settings.IsAppEnabled(name), () =>
            {
                _settings.ToggleApp(name);
                _window.ApplySettings();
            }));
        }
        return item;
    }

    private static IEnumerable<string> RunningWindowedProcesses()
    {
        var own = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string? name = null;
                try
                {
                    if (process.Id != own && process.MainWindowHandle != IntPtr.Zero
                        && !string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        name = process.ProcessName;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // processo fechou ou é protegido
                }
                if (name is not null) yield return name;
            }
        }
    }

    private static ToolStripMenuItem Check(string text, bool isChecked, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>Bolinha azul com um ponto verde, desenhada na hora para não depender de .ico.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var blue = new SolidBrush(Color.FromArgb(0x58, 0x65, 0xF2));
            using var green = new SolidBrush(Color.FromArgb(0x23, 0xA5, 0x5A));
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(blue, 1, 1, 30, 30);
            g.FillRectangle(white, 8, 10, 16, 3);
            g.FillRectangle(white, 8, 16, 11, 3);
            g.FillEllipse(green, 19, 19, 11, 11);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
