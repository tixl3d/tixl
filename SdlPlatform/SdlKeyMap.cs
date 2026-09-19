using SDL;
using T3.SystemUi;

namespace T3.SdlPlatform;

/// <summary>
/// Translates SDL keycodes to the Win32 virtual-key codes that <see cref="KeyHandler"/> and saved projects use.
/// </summary>
/// <remarks>
/// Letters and digits follow the keyboard layout on both sides, so German QWERTZ's Z stays Z. Windows assigns
/// punctuation keys an "OEM" code by the character they type, not by their position, so those are mapped by
/// character for the US and German layouts.
/// </remarks>
public static class SdlKeyMap
{
    /// <summary>Returns <see cref="Key.Undefined"/> (0) for keys without a Win32 equivalent.</summary>
    public static int ToVirtualKey(SDL_Keycode keycode)
    {
        var code = (uint)keycode;
        if (code is >= 'a' and <= 'z')
            return (int)(code - 'a' + 'A');

        if (code is >= '0' and <= '9')
            return (int)code;

        if (code >= (uint)SDL_Keycode.SDLK_F1 && code <= (uint)SDL_Keycode.SDLK_F1 + 11)
            return VkF1 + (int)(code - (uint)SDL_Keycode.SDLK_F1);

        if (code >= (uint)SDL_Keycode.SDLK_F13 && code <= (uint)SDL_Keycode.SDLK_F24)
            return VkF13 + (int)(code - (uint)SDL_Keycode.SDLK_F13);

        if (code >= (uint)SDL_Keycode.SDLK_KP_1 && code <= (uint)SDL_Keycode.SDLK_KP_9)
            return VkNumpad1 + (int)(code - (uint)SDL_Keycode.SDLK_KP_1);

        return _otherKeys.TryGetValue(code, out var virtualKey) ? virtualKey : (int)Key.Undefined;
    }

    private const int VkF1 = 0x70;
    private const int VkF13 = 0x7C;
    private const int VkNumpad1 = 0x61;

    private static readonly Dictionary<uint, int> _otherKeys = new()
                                                                   {
                                                                       { (uint)SDL_Keycode.SDLK_BACKSPACE, 0x08 },
                                                                       { (uint)SDL_Keycode.SDLK_TAB, 0x09 },
                                                                       { (uint)SDL_Keycode.SDLK_RETURN, 0x0D },
                                                                       { (uint)SDL_Keycode.SDLK_KP_ENTER, 0x0D },
                                                                       { (uint)SDL_Keycode.SDLK_LSHIFT, 0x10 },
                                                                       { (uint)SDL_Keycode.SDLK_RSHIFT, 0x10 },
                                                                       { (uint)SDL_Keycode.SDLK_LCTRL, 0x11 },
                                                                       { (uint)SDL_Keycode.SDLK_RCTRL, 0x11 },
                                                                       { (uint)SDL_Keycode.SDLK_LALT, 0x12 },
                                                                       { (uint)SDL_Keycode.SDLK_RALT, 0x12 },
                                                                       { (uint)SDL_Keycode.SDLK_PAUSE, 0x13 },
                                                                       { (uint)SDL_Keycode.SDLK_CAPSLOCK, 0x14 },
                                                                       { (uint)SDL_Keycode.SDLK_ESCAPE, 0x1B },
                                                                       { (uint)SDL_Keycode.SDLK_SPACE, 0x20 },
                                                                       { (uint)SDL_Keycode.SDLK_PAGEUP, 0x21 },
                                                                       { (uint)SDL_Keycode.SDLK_PAGEDOWN, 0x22 },
                                                                       { (uint)SDL_Keycode.SDLK_END, 0x23 },
                                                                       { (uint)SDL_Keycode.SDLK_HOME, 0x24 },
                                                                       { (uint)SDL_Keycode.SDLK_LEFT, 0x25 },
                                                                       { (uint)SDL_Keycode.SDLK_UP, 0x26 },
                                                                       { (uint)SDL_Keycode.SDLK_RIGHT, 0x27 },
                                                                       { (uint)SDL_Keycode.SDLK_DOWN, 0x28 },
                                                                       { (uint)SDL_Keycode.SDLK_PRINTSCREEN, 0x2C },
                                                                       { (uint)SDL_Keycode.SDLK_INSERT, 0x2D },
                                                                       { (uint)SDL_Keycode.SDLK_DELETE, 0x2E },
                                                                       { (uint)SDL_Keycode.SDLK_LGUI, 0x5B },
                                                                       { (uint)SDL_Keycode.SDLK_RGUI, 0x5C },
                                                                       { (uint)SDL_Keycode.SDLK_APPLICATION, 0x5D },
                                                                       { (uint)SDL_Keycode.SDLK_KP_0, 0x60 },
                                                                       { (uint)SDL_Keycode.SDLK_KP_MULTIPLY, 0x6A },
                                                                       { (uint)SDL_Keycode.SDLK_KP_PLUS, 0x6B },
                                                                       { (uint)SDL_Keycode.SDLK_KP_MINUS, 0x6D },
                                                                       { (uint)SDL_Keycode.SDLK_KP_PERIOD, 0x6E },
                                                                       { (uint)SDL_Keycode.SDLK_KP_DIVIDE, 0x6F },
                                                                       { (uint)SDL_Keycode.SDLK_NUMLOCKCLEAR, 0x90 },
                                                                       { (uint)SDL_Keycode.SDLK_SCROLLLOCK, 0x91 },

                                                                       // US layout punctuation
                                                                       { ';', 0xBA },
                                                                       { '=', 0xBB },
                                                                       { ',', 0xBC },
                                                                       { '-', 0xBD },
                                                                       { '.', 0xBE },
                                                                       { '/', 0xBF },
                                                                       { '`', 0xC0 },
                                                                       { '[', 0xDB },
                                                                       { '\\', 0xDC },
                                                                       { ']', 0xDD },
                                                                       { '\'', 0xDE },

                                                                       // German layout keys that type a different character
                                                                       { 'ü', 0xBA },
                                                                       { '+', 0xBB },
                                                                       { '#', 0xBF },
                                                                       { 'ö', 0xC0 },
                                                                       { 'ß', 0xDB },
                                                                       { '^', 0xDC },
                                                                       { '´', 0xDD },
                                                                       { 'ä', 0xDE },
                                                                       { '<', 0xE2 },
                                                                   };
}
