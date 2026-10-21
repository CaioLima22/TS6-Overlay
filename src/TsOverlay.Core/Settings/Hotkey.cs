namespace TsOverlay.Core.Settings;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Ctrl = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// Atalho global no formato "Ctrl+Shift+O". Os modificadores usam os mesmos bits do
/// RegisterHotKey e a tecla é um virtual-key code do Windows.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static Hotkey Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'O');

    private static readonly Dictionary<string, uint> NamedKeys = BuildNamedKeys();
    private static readonly Dictionary<uint, string> KeyNames =
        NamedKeys.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>
    /// Sem nenhum modificador só as teclas F são aceitas, para não roubar uma letra
    /// de tudo que for digitado no Windows.
    /// </summary>
    public bool IsValid =>
        KeyNames.ContainsKey(VirtualKey)
        && (Modifiers != HotkeyModifiers.None || VirtualKey is >= 0x70 and <= 0x87);

    public static bool IsSupportedKey(uint virtualKey) => KeyNames.ContainsKey(virtualKey);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Ctrl; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "WIN" or "WINDOWS": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null || !NamedKeys.TryGetValue(raw.ToUpperInvariant(), out var vk)) return false;
                    key = vk;
                    break;
            }
        }

        if (key is null) return false;
        var parsed = new Hotkey(modifiers, key.Value);
        if (!parsed.IsValid) return false;
        hotkey = parsed;
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.TryGetValue(VirtualKey, out var name) ? name : $"0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }

    private static Dictionary<string, uint> BuildNamedKeys()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) keys[c.ToString()] = c;
        for (var d = '0'; d <= '9'; d++) keys[d.ToString()] = d;
        for (var f = 1; f <= 24; f++) keys[$"F{f}"] = (uint)(0x70 + f - 1);
        for (var n = 0; n <= 9; n++) keys[$"Num{n}"] = (uint)(0x60 + n);

        keys["Space"] = 0x20;
        keys["PageUp"] = 0x21;
        keys["PageDown"] = 0x22;
        keys["End"] = 0x23;
        keys["Home"] = 0x24;
        keys["Left"] = 0x25;
        keys["Up"] = 0x26;
        keys["Right"] = 0x27;
        keys["Down"] = 0x28;
        keys["Insert"] = 0x2D;
        keys["Delete"] = 0x2E;
        keys["Pause"] = 0x13;
        keys["ScrollLock"] = 0x91;
        keys["Num*"] = 0x6A;
        keys["Num-"] = 0x6D;
        keys["Num/"] = 0x6F;
        return keys;
    }
}
