using System.Text.Json.Serialization;

namespace RunCatDashboard.App.Windowing;

public enum OverlayHotKeyKey
{
    Tab = 0x09,
    Escape = 0x1B,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B
}

public sealed record OverlayHotKeyGesture(
    bool Control,
    bool Alt,
    bool Shift,
    bool Windows,
    OverlayHotKeyKey Key)
{
    public const string DuplicateGestureMessage =
        "Dashboard 顯示／隱藏快捷鍵不可與 Overlay 模式快捷鍵相同，請選擇不同組合。";
    public const string CommonApplicationGestureWarning =
        "此組合常用於其他程式，RunCatDashboard 執行期間可能使原功能無法使用。";

    public static OverlayHotKeyGesture Default { get; } = new(
        Control: true,
        Alt: true,
        Shift: true,
        Windows: false,
        OverlayHotKeyKey.R);

    public static OverlayHotKeyGesture DashboardVisibilityDefault { get; } = new(
        Control: true,
        Alt: true,
        Shift: true,
        Windows: false,
        OverlayHotKeyKey.D);

    public static IReadOnlyList<OverlayHotKeyKey> SupportedKeys { get; } =
        Enum.GetValues<OverlayHotKeyKey>()
            .Where(IsSupportedPrimaryKey)
            .ToArray();

    [JsonIgnore]
    public string DisplayText => string.Join(" + ", GetDisplayParts());

    [JsonIgnore]
    public string? UsageWarning => IsCommonApplicationGesture()
        ? CommonApplicationGestureWarning
        : null;

    public bool TryValidate(out string? error)
    {
        if (IsBlockedSystemGesture())
        {
            error = $"系統快捷鍵 {DisplayText} 不可用於 RunCatDashboard。";
            return false;
        }

        if (!IsSupportedPrimaryKey(Key))
        {
            error = "請選擇 A-Z、0-9 或 F1-F12 作為主要按鍵。";
            return false;
        }

        if (!Control && !Alt && !Shift && !Windows)
        {
            error = "快捷鍵至少需要一個 modifier。";
            return false;
        }

        error = null;
        return true;
    }

    public static string FormatKey(OverlayHotKeyKey key)
    {
        string name = key.ToString();
        return name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])
            ? name[1].ToString()
            : name;
    }

    private static bool IsSupportedPrimaryKey(OverlayHotKeyKey key)
    {
        int value = (int)key;
        return value is >= (int)OverlayHotKeyKey.D0 and <= (int)OverlayHotKeyKey.D9 or
            >= (int)OverlayHotKeyKey.A and <= (int)OverlayHotKeyKey.Z or
            >= (int)OverlayHotKeyKey.F1 and <= (int)OverlayHotKeyKey.F12;
    }

    private IEnumerable<string> GetDisplayParts()
    {
        if (Control) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        if (Windows) yield return "Win";
        yield return FormatKey(Key);
    }

    private bool IsBlockedSystemGesture()
    {
        return this switch
        {
            { Alt: true, Control: false, Shift: false, Windows: false,
                Key: OverlayHotKeyKey.F4 or OverlayHotKeyKey.Tab } => true,
            { Control: true, Alt: false, Shift: false, Windows: false,
                Key: OverlayHotKeyKey.Escape } => true,
            { Windows: true, Control: false, Alt: false, Shift: false,
                Key: OverlayHotKeyKey.D or OverlayHotKeyKey.E or OverlayHotKeyKey.I or
                    OverlayHotKeyKey.L or OverlayHotKeyKey.R or OverlayHotKeyKey.Tab } => true,
            _ => false
        };
    }

    private bool IsCommonApplicationGesture() =>
        Control && !Alt && !Shift && !Windows &&
        Key is OverlayHotKeyKey.S or OverlayHotKeyKey.C or OverlayHotKeyKey.V or
            OverlayHotKeyKey.Z or OverlayHotKeyKey.F or OverlayHotKeyKey.P or
            OverlayHotKeyKey.W;
}
