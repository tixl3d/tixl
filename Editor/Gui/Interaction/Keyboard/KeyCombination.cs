#nullable enable
using ImGuiNET;
using T3.SystemUi;

namespace T3.Editor.Gui.Interaction.Keyboard;
/// <summary>
/// Defines a combination that can be saved with a keymap
/// </summary>
internal  struct KeyCombination(Key key, bool ctrl = false, bool alt = false, bool shift = false)
{
    public Key Key = key;
    public bool Ctrl = ctrl;
    public bool Alt = alt;
    public bool Shift = shift;

    /// <summary>What the Ctrl modifier is called on this platform: Cmd on macOS, where the Cmd key acts as Ctrl.</summary>
    internal static readonly string CtrlLabel = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    /// <summary>What the Alt key is labeled on this platform.</summary>
    internal static readonly string AltLabel = OperatingSystem.IsMacOS() ? "Option" : "Alt";
    
    internal bool ModifiersMatch(ImGuiIOPtr io)
    {
        return (!Alt || io.KeyAlt) && (Alt || !io.KeyAlt)
                                               && (!Ctrl || io.KeyCtrl) && (Ctrl || !io.KeyCtrl)
                                               && (!Shift || io.KeyShift) && (Shift || !io.KeyShift);
    }    
    
    // TODO: Refactor into try pattern
    internal static KeyCombination? ParseShortcutString(string shortcut)
    {
        try
        {
            var parts = shortcut.Split('+');
            
            var key = Enum.Parse<Key>(parts.Last());
            var ctrl = parts.Contains("Ctrl");
            var alt = parts.Contains("Alt");
            var shift = parts.Contains("Shift");

            return new KeyCombination(key, ctrl, alt, shift);
        }
        catch
        {
            return null;
        }
    }

    public  bool Matches(ref  KeyCombination  other)
    {
        return Key == other.Key &&
           Ctrl == other.Ctrl &&
           Shift == other.Shift &&
           Alt == other.Alt;
    }
    
    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add(CtrlLabel);
        if (Alt) parts.Add(AltLabel);
        if (Shift) parts.Add("Shift");
        parts.Add(Key.ToString());

        return string.Join("+", parts);
    }

    internal static KeyCombination None => new(Key.Undefined);

}