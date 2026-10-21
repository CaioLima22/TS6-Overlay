using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TsOverlay.Core.Settings;
using TsOverlay.Core.State;
using TsOverlay.Core.Ts;
using TsOverlay.Native;

namespace TsOverlay;

/// <summary>Só desenha: lê o ChannelState e nunca fala com o TS.</summary>
public partial class OverlayWindow : Window
{
    private const int LockHotkeyId = 1;

    private static readonly Brush UnlockedBorder = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));

    private readonly ChannelState _state;
    private readonly TsRemoteClient _client;
    private readonly AppSettings _settings;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private Hotkey _hotkey;
    private bool _hotkeyRegistered;
    private bool _capturingHotkey;
    private int _refreshQueued;

    public OverlayWindow(ChannelState state, TsRemoteClient client, AppSettings settings)
    {
        InitializeComponent();
        _state = state;
        _client = client;
        _settings = settings;
        _hotkey = settings.GetLockHotkey();

        Opacity = AppSettings.ClampOpacity(settings.Opacity);
        RestorePosition();

        _state.Changed += QueueRefresh;
        _client.StatusChanged += _ => QueueRefresh();
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

        _hotkeyRegistered = RegisterHotkey(_hotkey);

        // Sem atalho não teria como destravar, então começa destravado
        ApplyLocked(_settings.Locked && _hotkeyRegistered);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered) Win32.UnregisterHotKey(_hwnd, LockHotkeyId);
        _source?.RemoveHook(WndProc);
        SaveSettings();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == LockHotkeyId)
        {
            ApplyLocked(!_settings.Locked);
            SaveSettings();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private bool RegisterHotkey(Hotkey hotkey) =>
        Win32.RegisterHotKey(_hwnd, LockHotkeyId, (uint)hotkey.Modifiers | Win32.MOD_NOREPEAT, hotkey.VirtualKey);

    /// <summary>Travado: cliques passam para o jogo. Destravado: dá para arrastar e fechar.</summary>
    private void ApplyLocked(bool locked)
    {
        _settings.Locked = locked;
        // AllowsTransparency já deixa a janela layered; só o WS_EX_TRANSPARENT liga e desliga
        Win32.SetExStyle(_hwnd, Win32.WS_EX_TRANSPARENT, locked);
        if (locked) EndHotkeyCapture();
        UnlockedHeader.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        Card.BorderBrush = locked ? Brushes.Transparent : UnlockedBorder;
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // Para capturar o novo atalho a janela precisa receber o teclado, então o
    // WS_EX_NOACTIVATE sai só enquanto a captura durar
    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            EndHotkeyCapture();
            return;
        }
        _capturingHotkey = true;
        HotkeyButton.Content = "Pressione o novo atalho… (Esc cancela)";
        Win32.SetExStyle(_hwnd, Win32.WS_EX_NOACTIVATE, false);
        Activate();
        Focus();
    }

    private void EndHotkeyCapture()
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
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
        if (!_capturingHotkey) return;
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
            HotkeyButton.Content = "Use Ctrl, Alt ou Shift + uma tecla (ou uma tecla F)";
            return;
        }

        if (_hotkeyRegistered) Win32.UnregisterHotKey(_hwnd, LockHotkeyId);
        if (RegisterHotkey(candidate))
        {
            _hotkey = candidate;
            _hotkeyRegistered = true;
            _settings.LockHotkey = candidate.ToString();
            SaveSettings();
            EndHotkeyCapture();
        }
        else
        {
            // Volta para o atalho anterior, se ele funcionava
            _hotkeyRegistered = _hotkeyRegistered && RegisterHotkey(_hotkey);
            HotkeyButton.Content = $"{candidate} está em uso, tente outro";
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
        var clients = _state.GetVisibleClients();
        ClientList.ItemsSource = clients;

        StatusText.Text = _client.Status switch
        {
            TsConnectionStatus.Connecting => "Procurando o TeamSpeak... Confira se Remote Apps está ativado nas configurações.",
            TsConnectionStatus.WaitingForApproval => "Aprove o TS Overlay na notificação do TeamSpeak.",
            _ when clients.Count == 0 => "Não conectado a nenhum servidor.",
            _ => "",
        };
        if (!_hotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            StatusText.Text = (StatusText.Text + $"\nO atalho {_hotkey} está em uso por outro programa. Clique nele acima para escolher outro.").Trim();
        }
        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        HelpText.Text = $"Arraste para mover · Ctrl+roda muda a opacidade · {_hotkey} trava";
        if (!_capturingHotkey) HotkeyButton.Content = _hotkey.ToString();
    }
}
