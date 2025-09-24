#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.DataTypes;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class PickTextureUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("29e289be-e735-4dd4-8826-5e434cc995fa")]
        internal readonly InputSlot<int> Index = null!;

        [BindInput("6C935163-1729-4DF0-A981-610B4AA7C6A3")]
        internal readonly MultiInputSlot<Texture2D> Inputs = null!;
    }

    internal static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                    ImDrawListPtr drawList,
                                                    ImRect selectableScreenRect,
                                                    ScalableCanvas canvas,
                                                    ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;
        var canvasScale = canvas.Scale.X;
        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        ImGui.PushClipRect(selectableScreenRect.Min, selectableScreenRect.Max, true);

        var h = selectableScreenRect.GetHeight();
       /* var font = h > 40 ? Fonts.FontLarge
                          : (h > 25 ? Fonts.FontNormal : Fonts.FontSmall);*/

        ImGui.PushFont(Fonts.FontNormal);
        ImGui.SetCursorScreenPos(selectableScreenRect.Min + new Vector2(10, 0));
        ImGui.BeginGroup();

        // Show operator name
        if (!string.IsNullOrWhiteSpace(instance.SymbolChild.Name))
        {
            ImGui.TextUnformatted(instance.SymbolChild.Name);
        }

        // Current index
        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Index) ?? false;
        var currentValue = (isAnimated || data.Index.HasInputConnections)
                               ? data.Index.Value
                               : data.Index.TypedInputValue.Value;

        ImGui.TextUnformatted($"PickTexture: {currentValue}");

        // --- Buttons per input (vertical column, show source operator name) ---
        var connections = data.Inputs.GetCollectedTypedInputs();
        if (connections != null && connections.Count > 0)
        {
            // increase padding so buttons feel taller
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
            var inputlength = connections.Count;
            var buttonHeight = (h / inputlength)-5.0f; // increase or reduce as you like
            for (int i = 0; i < connections.Count; i++)
            {
                var srcSlot = connections[i];
                string label = $"#{i}";

                var srcInstance = srcSlot?.Parent;
                if (srcInstance != null)
                {
                    if (!string.IsNullOrWhiteSpace(srcInstance.SymbolChild.Name))
                    {
                        // Use instance name if it exists
                        label = srcInstance.SymbolChild.Name;
                    }
                    else if (!string.IsNullOrWhiteSpace(srcInstance.Symbol.Name))
                    {
                        // Fallback to operator (symbol) name
                        label = srcInstance.Symbol.Name;
                    }
                }


                var isActive = (i == currentValue);
                if (isActive)
                    ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Rgba);

                // full width (-1) and fixed height
                if (ImGui.Button(label, new Vector2(selectableScreenRect.GetWidth()-20, buttonHeight)))
                {
                    // update the int input's typed value and make the operator re-evaluate
                    data.Index.TypedInputValue.Value = i;
                    data.Index.DirtyFlag.ForceInvalidate();
                }

                if (isActive)
                    ImGui.PopStyleColor();
            }

            ImGui.PopStyleVar();
        }



        ImGui.EndGroup();
        ImGui.PopFont();

        ImGui.PopClipRect();

        return OpUi.CustomUiResult.Rendered
             | OpUi.CustomUiResult.PreventOpenSubGraph
             | OpUi.CustomUiResult.PreventInputLabels
             | OpUi.CustomUiResult.AllowThumbnail
             | OpUi.CustomUiResult.PreventTooltip;
    }
}
