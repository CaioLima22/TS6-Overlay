using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TsOverlay.Core.Overlay;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;
using TsOverlay.Native;

namespace TsOverlay;

/// <summary>Só desenha: lê o ChannelState e nunca fala com o TS.</summary>
public partial class OverlayWindow : Window
{
    private const int LockHotkeyId = 1;
    private const int HideHotkeyId = 2;

    /// <summary>Um atalho global: o que está salvo, se o Windows aceitou e o botão que o troca.</summary>
    private sealed class HotkeySlot(int id, Hotkey hotkey, System.Windows.Controls.Button button)
    {
        public int Id { get; } = id;
        public Hotkey Hotkey { get; set; } = hotkey;
        public bool Registered { get; set; }
        public System.Windows.Controls.Button Button { get; } = button;
    }

    private static readonly Brush UnlockedBorder = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
    private static readonly Brush UnlockedBackground = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1F, 0x22));

    private readonly ChannelState _state;
    private readonly TsRemoteClient _client;
    private readonly AppSettings _settings;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly HotkeySlot _lock;
    private readonly HotkeySlot _hide;
    private HotkeySlot? _capturing;
    private int _refreshQueued;
    private readonly DispatcherTimer _lastLeftTimer = new();
    private bool _hiddenByUser;

    public OverlayWindow(ChannelState state, TsRemoteClient client, AppSettings settings)
    {
        InitializeComponent();
        _state = state;
        _client = client;
        _settings = settings;
        _lock = new HotkeySlot(LockHotkeyId, settings.GetLockHotkey(), HotkeyButton);
        _hide = new HotkeySlot(HideHotkeyId, settings.GetHideHotkey(), HideHotkeyButton);

        Opacity = AppSettings.ClampOpacity(settings.Opacity);
        RestorePosition();

        _state.Changed += QueueRefresh;
        // O último que saiu some sozinho, sem esperar outro evento do TS
        _lastLeftTimer.Tick += (_, _) =>
        {
            _lastLeftTimer.Stop();
            Refresh();
        };
        _client.StatusChanged += _ => QueueRefresh();
        AvatarCache.Instance.Changed += QueueRefresh;
        Refresh();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);

        // Fora do Alt+Tab e nunca rouba o foco do jogo, travado ou não
        Win32.SetExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, true);

        _lock.Registered = RegisterHotkey(_lock, _lock.Hotkey);
        _hide.Registered = RegisterHotkey(_hide, _hide.Hotkey);

        // Sem atalho não teria como destravar, então começa destravado
        ApplyLocked(_settings.Locked && _lock.Registered);

        UpdateSide();
        LocationChanged += (_, _) => UpdateSide();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_lock.Registered) Win32.UnregisterHotKey(_hwnd, LockHotkeyId);
        if (_hide.Registered) Win32.UnregisterHotKey(_hwnd, HideHotkeyId);
        _source?.RemoveHook(WndProc);
        SaveSettings();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case LockHotkeyId:
                ToggleLocked();
                handled = true;
                break;
            case HideHotkeyId:
                HiddenByUser = !HiddenByUser;
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Ocultado pela bandeja ou pelo ✕. O app continua rodando.</summary>
    public bool HiddenByUser
    {
        get => _hiddenByUser;
        set
        {
            if (_hiddenByUser == value) return;
            _hiddenByUser = value;
            // Oculto não dá para terminar de escolher um atalho
            if (value) EndHotkeyCapture();
            Refresh();
        }
    }

    public string LockHotkey => _lock.Hotkey.ToString();

    public string HideHotkey => _hide.Hotkey.ToString();

    public void ToggleLocked()
    {
        // Sem atalho registrado, travar deixaria o overlay sem como destravar pelo teclado,
        // mas a bandeja ainda destrava, então aqui é permitido
        ApplyLocked(!_settings.Locked);
        SaveSettings();
    }

    /// <summary>Chamado pela bandeja depois de mudar uma opção.</summary>
    public void ApplySettings()
    {
        SaveSettings();
        Refresh();
    }

    private bool RegisterHotkey(HotkeySlot slot, Hotkey hotkey) =>
        Win32.RegisterHotKey(_hwnd, slot.Id, (uint)hotkey.Modifiers | Win32.MOD_NOREPEAT, hotkey.VirtualKey);

    /// <summary>Travado: cliques passam para o jogo. Destravado: dá para arrastar e fechar.</summary>
    private void ApplyLocked(bool locked)
    {
        _settings.Locked = locked;
        // AllowsTransparency já deixa a janela layered; só o WS_EX_TRANSPARENT liga e desliga
        Win32.SetExStyle(_hwnd, Win32.WS_EX_TRANSPARENT, locked);
        if (locked) EndHotkeyCapture();
        UnlockedHeader.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        Card.BorderBrush = locked ? Brushes.Transparent : UnlockedBorder;
        Card.Background = locked ? Brushes.Transparent : UnlockedBackground;
        Refresh();
    }

    private void RestorePosition()
    {
        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top
            && AppSettings.IsOnScreen(left, top, Width,
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Left + 20;
            Top = SystemParameters.WorkArea.Top + 20;
        }
    }

    /// <summary>Espelha a lista quando o overlay está na metade direita do monitor em que ele está.</summary>
    private void UpdateSide()
    {
        if (_hwnd == IntPtr.Zero || !Win32.GetWindowRect(_hwnd, out var rect)) return;
        var monitor = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;
        var right = OverlayRules.IsOnRightSide(rect.Left, rect.Right - rect.Left, monitor.Left, monitor.Width);
        ListPanel.FlowDirection = right ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void SaveSettings()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Opacity = Opacity;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Não vale derrubar o overlay por não conseguir salvar a posição
        }
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Locked || e.OriginalSource is System.Windows.Controls.Button) return;
        DragMove(); // volta quando o botão é solto
        SaveSettings();
    }

    private void Card_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_settings.Locked || Keyboard.Modifiers != ModifierKeys.Control) return;
        Opacity = AppSettings.ClampOpacity(Opacity + (e.Delta > 0 ? 0.05 : -0.05));
        SaveSettings();
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HiddenByUser = true;

    // Para capturar o novo atalho a janela precisa receber o teclado, então o
    // WS_EX_NOACTIVATE sai só enquanto a captura durar
    private void HotkeyButton_Click(object sender, RoutedEventArgs e) => StartHotkeyCapture(_lock);

    private void HideHotkeyButton_Click(object sender, RoutedEventArgs e) => StartHotkeyCapture(_hide);

    private void StartHotkeyCapture(HotkeySlot slot)
    {
        var wasCapturing = _capturing;
        EndHotkeyCapture();
        if (wasCapturing == slot) return; // clicar de novo cancela

        _capturing = slot;
        slot.Button.Content = "Pressione o novo atalho… (Esc cancela)";
        Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE, false);
        Activate();
        Focus();
    }

    private void EndHotkeyCapture()
    {
        if (_capturing is null) return;
        _capturing = null;
        Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE, true);
        Refresh();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        EndHotkeyCapture();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_capturing is not { } slot) return;
        e.Handled = true;

        // Com Alt pressionado o WPF manda Key.System e a tecla real vem em SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EndHotkeyCapture();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return; // espera a tecla principal
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        var candidate = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!candidate.IsValid)
        {
            slot.Button.Content = "Use Ctrl, Alt ou Shift + uma tecla (ou uma tecla F)";
            return;
        }
        var other = slot == _lock ? _hide : _lock;
        if (candidate == other.Hotkey)
        {
            slot.Button.Content = $"{candidate} já é o atalho de {(other == _lock ? "travar" : "ocultar")}, tente outro";
            return;
        }

        if (slot.Registered) Win32.UnregisterHotKey(_hwnd, slot.Id);
        if (RegisterHotkey(slot, candidate))
        {
            slot.Hotkey = candidate;
            slot.Registered = true;
            if (slot == _lock) _settings.LockHotkey = candidate.ToString();
            else _settings.HideHotkey = candidate.ToString();
            SaveSettings();
            EndHotkeyCapture();
        }
        else
        {
            // Volta para o atalho anterior, se ele funcionava
            slot.Registered = slot.Registered && RegisterHotkey(slot, slot.Hotkey);
            slot.Button.Content = $"{candidate} está em uso, tente outro";
        }
    }

    // Eventos chegam do thread do WebSocket; junta rajadas num único redesenho no thread de UI
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        var allClients = _state.GetVisibleClients();
        var clients = OverlayRules.FilterClients(allClients, _settings.OnlyTalking);
        ClientList.ItemsSource = clients;

        // No modo "só quem fala" a lista é só de quem fala, então quem saiu não entra
        var lastLeft = _settings.OnlyTalking || allClients.Count == 0 ? null : _state.GetLastLeft();
        LastLeftName.Text = lastLeft?.Nickname ?? "";
        LastLeftRow.Visibility = lastLeft is null ? Visibility.Collapsed : Visibility.Visible;
        _lastLeftTimer.Stop();
        if (lastLeft is not null)
        {
            var remaining = lastLeft.ExpiresAt - DateTimeOffset.UtcNow;
            _lastLeftTimer.Interval = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            _lastLeftTimer.Start();
        }

        var inServer = allClients.Count > 0;
        StatusText.Text = _client.Status switch
        {
            TsConnectionStatus.Connecting => "Procurando o TeamSpeak... Confira se Remote Apps está ativado nas configurações.",
            TsConnectionStatus.WaitingForApproval => "Aprove o TS Overlay na notificação do TeamSpeak.",
            _ when !inServer => "Não conectado a nenhum servidor.",
            _ when clients.Count == 0 && !_settings.Locked => "Ninguém falando agora.",
            _ => "",
        };
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var slot in new[] { _lock, _hide })
            {
                if (slot.Registered) continue;
                StatusText.Text = (StatusText.Text + $"\nO atalho {slot.Hotkey} está em uso por outro programa. Clique nele acima para escolher outro.").Trim();
            }
        }
        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        // No modo "só quem fala", travado e sem ninguém falando, o cartão some por inteiro
        var nothingToShow = _settings.Locked && clients.Count == 0 && StatusText.Text.Length == 0;
        // Fora de servidor some sozinho; o ocultar manual continua valendo quando voltar
        var autoHidden = OverlayRules.ShouldAutoHide(_settings.AutoHideWhenNoServer, _settings.Locked,
            _client.Status, inServer, !string.IsNullOrEmpty(_settings.ApiKey));
        var show = !nothingToShow && !_hiddenByUser && !autoHidden;
        Card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        HelpText.Text = $"Arraste para mover · Ctrl+roda muda a opacidade · {_lock.Hotkey} trava · {_hide.Hotkey} oculta";
        foreach (var slot in new[] { _lock, _hide })
        {
            if (_capturing != slot) slot.Button.Content = slot.Hotkey.ToString();
        }
    }
}
