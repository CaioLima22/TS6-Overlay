using System.Runtime.InteropServices;

namespace TsOverlay.Native;

/// <summary>Estilos de janela e atalho global via Win32.</summary>
internal static class Win32
{
    public const int GWL_EXSTYLE = -20;

    /// <summary>Fora do Alt+Tab e da barra de tarefas.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Cliques passam para a janela de baixo (o jogo).</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Nunca rouba o foco do jogo.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Liga ou desliga bits do estilo estendido da janela.</summary>
    public static void SetExStyle(IntPtr hwnd, int flags, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = enabled ? style | (long)flags : style & ~(long)flags;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }
}
