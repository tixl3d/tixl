
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class StringUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("ceeae47b-d792-471d-a825-49e22749b7b9")]
        internal readonly InputSlot<string> InputString = null!;
    }

    /// <summary>
    /// Draws a custom ui that allows direct editing of strings within the graph
    /// </summary>
    /// <remarks>
    /// The implementation is kind of ugly, mostly because I had a very had time to
    /// detect if the control loses focus. The normal candidates like IsItemDeactivated() didn't
    /// catch all cases. I'm pretty sure that detected all clicks is no ideal either.
    ///
    /// Using an invisibleButton interfered with the drag interaction of the node.
    /// </remarks>
    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var dragWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        var usableArea = screenRect;
        usableArea.Min.X += dragWidth;

        ImGui.PushID(instance.SymbolChildId.GetHashCode());

        ImGui.PushFont(canvas.Scale.X < 2
                           ? Fonts.FontSmall
                           : canvas.Scale.X < 4
                               ? Fonts.FontNormal
                               : Fonts.FontLarge);

        // Draw edit window
        if (instance.SymbolChildId == _focusedInstanceId)
        {
            usableArea.Max.X -= 10f; // Keep some padding for resize handle
            usableArea.Expand(-3);
            ImGui.SetKeyboardFocusHere();
            ImGui.SetCursorScreenPos(usableArea.Min);
            if (ImGui.InputTextMultiline("##str", ref data.InputString.TypedInputValue.Value, 16368, usableArea.GetSize(),
                                         ImGuiInputTextFlags.None))
            {
                data.InputString.Input.IsDefault = false;
                data.InputString.DirtyFlag.Invalidate();
            }

            var clickedOutside = ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !usableArea.Contains(ImGui.GetMousePos());
            if (ImGui.IsItemDeactivated() || clickedOutside)
            {
                _focusedInstanceId = Guid.Empty;
            }
        }
        // Draw viewer
        else
        {
            //Log.Debug("hovered " + ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.AllowWhenDisabled ) + " focus" + ImGui.IsWindowFocused(), stringInstance);
            usableArea.Expand(canvas.Scale.X < 0.75f ? 0 : -4);
            if (usableArea.Contains(ImGui.GetMousePos())
                && (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) || ImGui.IsWindowFocused())
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).Length() < UserSettings.Config.ClickThreshold)
            {
                _focusedInstanceId = instance.SymbolChildId;
            }

            var v = data.InputString.TypedInputValue.Value;
            if (!string.IsNullOrEmpty(v))
            {
                ImGui.PushClipRect(usableArea.Min, usableArea.Max, true);
                var color = TypeUiRegistry.GetPropertiesForType(typeof(string));
                ImGui.GetWindowDrawList().AddText(usableArea.Min, ImGui.ColorConvertFloat4ToU32(color.Color.Rgba), v);
                ImGui.PopClipRect();
            }
        }

        ImGui.PopFont();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventOpenSubGraph | OpUi.CustomUiResult.PreventTooltip |
               OpUi.CustomUiResult.PreventOpenParameterPopUp;
    }

    private static Guid _focusedInstanceId;
}