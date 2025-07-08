namespace T3.SystemUi;

/// <summary>
/// Note: The actually handling is implemented differently in Editor and Player.
///
/// Its values are set directly in the windows forms WndProc message handling
/// using the <see cref="Key"/>-code provided by the Windows event.
/// </summary>
public static class KeyHandler
{
    private static readonly bool[] PressedKeysPrivate = new bool[512];
    public static readonly IReadOnlyList<bool> PressedKeys = PressedKeysPrivate;

    public static bool IsKeyPressed(Key keyCode) => IsKeyPressed((int)keyCode);

    public static void SetKeyDown(Key keyCode) => SetKeyPressed((int)keyCode, true);
    public static void SetKeyDown(int keyCode) => SetKeyPressed(keyCode, true);
    
    public static void SetKeyUp(Key keyCode) => SetKeyPressed((int)keyCode, false);
    public static void SetKeyUp(int keyCode) => SetKeyPressed(keyCode, false);

    public static Key GetPressedKey()
    {
        for (var index = 0; index < PressedKeysPrivate.Length; index++)
        {
            if (PressedKeysPrivate[index])
                return (Key)index;
        }

        return Key.Undefined;
    }
        
    public static bool IsKeyPressed(int keyCode)
    {
        if (keyCode < 0 || keyCode >= PressedKeysPrivate.Length)
        {
            Console.WriteLine($"KeyHandler: Invalid key code {keyCode}");
            return false;
        }
            
        return PressedKeysPrivate[keyCode];
    }

    private static void SetKeyPressed(int keyCode, bool value)
    {
        if (keyCode < 0 || keyCode >= PressedKeysPrivate.Length)
        {
            Console.WriteLine($"KeyHandler: Invalid key code {keyCode}");
            return;
        }
            
        PressedKeysPrivate[keyCode] = value;
    }
}

/// <summary>
/// This enumeration is directly derived from <see cref="System.Windows.Forms.Keys"/>.
/// Make sure to not confuse these with ImGuiKey enumeration.
/// </summary>
public enum Key // Todo: can we make this frontend-agnostic?
{
    Undefined = 0,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,

    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    Backspace = 8,
    Delete = 46,

    // LeftShift,    
    // RightShift,
    // LeftCtrl,
    ShiftKey = 16,
    CtrlKey = 17,
    End = 0x23,
    Home = 0x24,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    Plus = 187,
    Tab = 9,
    CursorUp = 38,
    CursorDown = 40,
    CursorLeft = 37,
    CursorRight = 39,
    Return = 13,
    CapsLock = 20,
    Esc = 27,
    Equal = 187,
    Minus = 189,
    Space = 32,
    SquareBracketLeft = 219,
    SquareBracketRight = 221,
    Comma = 188,
    Period = 190,
    Slash = 191,
    Pipe = 192,
    Alt = 18,
    Ins = 45,
    PageUp = 33,
    PageDown = 34,
    Semicolon = 186,
    Apostrophe = 226,
    HashTag = 220,
}