using System.Diagnostics;

namespace TsOverlay.Native;

/// <summary>
/// Avisa quando outra janela vem para o primeiro plano, com o nome do processo dela.
/// Usa um hook de evento do Windows, então não gasta CPU esperando.
/// Precisa ser criado no thread de UI, que é onde o Windows entrega os eventos.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Win32.WinEventProc _callback; // mantém o delegate vivo para o GC não coletar
    private readonly IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnForegroundChanged;
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        CurrentProcess = GetProcessName(Win32.GetForegroundWindow());
    }

    /// <summary>Processo em primeiro plano agora, sem a extensão (ex.: "eldenring").</summary>
    public string? CurrentProcess { get; private set; }

    public event Action<string?>? Changed;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        var name = GetProcessName(hwnd);
        if (name == CurrentProcess) return;
        CurrentProcess = name;
        Changed?.Invoke(name);
    }

    public static string? GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // o processo já fechou
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWinEvent(_hook);
    }
}
