#nullable enable
using ImGuiNET;
using Newtonsoft.Json;
using T3.Serialization;
using T3.SystemUi;

namespace T3.Editor.Gui.Interaction.Keyboard;

/// <summary>
/// Controls the assignment of <see cref="Keyboard.KeyCombination"/> to <see cref="UserActions"/>
/// </summary>
internal sealed class KeyBinding
{
    public KeyBinding(UserActions action, KeyCombination combination)
    {
        Action = action;
        KeyCombination = combination;
        _flags = KeyActionHandling.GetActionFlags(action);
    }

    [JsonConverter(typeof(SafeEnumConverter<UserActions>))]
    public UserActions Action;

    public KeyCombination KeyCombination;

    internal bool IsContextValid()
    {
        if ((_flags & KeyActionHandling.Flags.NeedsWindowFocus) != 0
            && !ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows))
            return false;

        if ((_flags & KeyActionHandling.Flags.NeedsWindowHover) != 0
            && !ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            return false;

        return true;
    }

    internal bool IsTriggered(ImGuiIOPtr io)
    {
        if (!KeyCombination.ModifiersMatch(io))
            return false;

        if ((_flags & KeyActionHandling.Flags.KeyHoldOnly) != 0)
            return ImGui.IsKeyDown((ImGuiKey)KeyCombination.Key);

        if ((_flags & KeyActionHandling.Flags.KeyPressOnly) != 0)
            return ImGui.IsKeyPressed((ImGuiKey)KeyCombination.Key, false);

        // Default behavior (works for both press and hold)
        return ImGui.IsKeyPressed((ImGuiKey)KeyCombination.Key, true);
    }

    public KeyBinding Clone()
    {
        return new KeyBinding(Action, KeyCombination)
                   {
                       _flags = KeyActionHandling.Flags.None
                   };
    }

    private KeyActionHandling.Flags _flags; // This is of caching to speed up
    public static readonly KeyBinding None = new(UserActions.None, new KeyCombination());
}