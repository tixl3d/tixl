#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The setup-mode side of an output window: whether the window shows setup entities instead
/// of an operator output, the window-owned entity selection, the side panel, and the entity
/// view drawing. Owning the selection makes every setup-mode window implicitly "pinned";
/// no extra lock step. One instance per OutputWindow.
/// </summary>
internal sealed class OutputSetupModeView
{
    /// <summary>When active, the window shows the selected setup entity instead of the operator output.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Draws the setup outline side panel when enabled. Call first — it splits the window horizontally.</summary>
    public void DrawSidePanel()
    {
        if (!_showSetupPanel)
            return;

        ImGui.BeginChild("##setupPanel",
                         new Vector2(240 * T3Ui.UiScaleFactor, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.None);
        SetupPanel.Draw(_entitySelection);
        ImGui.EndChild();
        ImGui.SameLine();
    }

    /// <summary>Draws the selected entity (or a prompt) plus the minimal entity toolbar.</summary>
    public void DrawEntityView(bool hideToolbar)
    {
        if (TryGetShownEntity(out var entityKind, out var entityId))
        {
            SetupPanel.DrawEntityCard(entityKind, entityId);
        }
        else
        {
            CustomComponents.EmptyWindowMessage("Select an entity in the setup panel");
        }

        if (!hideToolbar)
        {
            ImGui.SetCursorPos(ImGui.GetCursorStartPos());
            CustomComponents.PushToolbarIconBackground();
            DrawViewModeMenu();
            CustomComponents.PopToolbarIconBackground();
        }
    }

    /// <summary>The Output Mode menu — also drawn by the operator toolbar.</summary>
    public void DrawViewModeMenu()
    {
        if (CustomComponents.StateButton("View", CustomComponents.ButtonStates.Default))
        {
            ImGui.OpenPopup(ViewModePopupId);
        }

        if (!ImGui.BeginPopup(ViewModePopupId))
            return;

        CustomComponents.MenuGroupHeader("Output Mode");
        if (CustomComponents.DrawMenuItem(1, "Operator", isChecked: !IsActive))
        {
            IsActive = false;
        }

        if (CustomComponents.DrawMenuItem(2, "Setup", isChecked: IsActive))
        {
            IsActive = true;
            _showSetupPanel = true;   // the panel is the entity picker; without it the mode is a dead end
        }
        CustomComponents.TooltipForLastItem("Shows setup entities picked in this window's panel",
                                            "Each window browses independently; switch back to Operator for the graph output.");

        CustomComponents.SeparatorLine();

        if (CustomComponents.DrawMenuItem(4, "Show Setup Panel", isChecked: _showSetupPanel))
        {
            _showSetupPanel = !_showSetupPanel;
        }

        ImGui.EndPopup();
    }

    private bool TryGetShownEntity(out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;

        if (!IsActive)
            return false;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        return _entitySelection.TryResolve(setup, out kind, out id);
    }

    private const string ViewModePopupId = "##outputViewMode";
    private bool _showSetupPanel;
    private readonly SetupEntitySelection _entitySelection = new();
}
