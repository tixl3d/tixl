#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class IntValueUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("4515C98E-05BC-4186-8773-4D2B31A8C323")]
        internal readonly InputSlot<int> Int = null!;

        // Cached display text, only rebuilt when the value changes
        internal int CachedValue;
        internal string CachedText = "0";
    }

    internal static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                    ImDrawListPtr drawList,
                                                    ImRect selectableScreenRect,
                                                    ScalableCanvas canvas,
                                                    ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;
        var dragWidth = WidgetElements.DrawOperatorDragHandle(selectableScreenRect, drawList, canvas.Scale);
        
        selectableScreenRect.Min.X += dragWidth;

        var symbolChild = instance.SymbolChild;
        ImGui.PushClipRect(selectableScreenRect.Min, selectableScreenRect.Max, true);

        var h = selectableScreenRect.GetHeight();
        var font = h > 40
                       ? Fonts.FontLarge
                       : (h > 25
                              ? Fonts.FontNormal
                              : Fonts.FontSmall);

        ImGui.PushFont(font);

        var lineHeight = ImGui.GetTextLineHeight();
        var centeredY = selectableScreenRect.Min.Y + (h - lineHeight) * 0.5f;
        ImGui.SetCursorScreenPos(MathUtils.Floor(new Vector2(selectableScreenRect.Min.X, centeredY)));
        ImGui.BeginGroup();
        if (!string.IsNullOrWhiteSpace(symbolChild.Name))
        {
            ImGui.TextUnformatted(symbolChild.Name);
        }

        var isAnimated = instance.Parent?.Symbol.Animator.IsInputSlotAnimated(data.Int) ?? false;

        double value = (isAnimated || data.Int.HasInputConnections)
                        ? data.Int.GetCurrentValue()
                        : data.Int.TypedInputValue.Value;
        // Interaction
        var isActive = false;
        {
            ImGui.PushID(instance.GetHashCode());
            var inputSlot = data.Int;
            var editingUnlocked = ImGui.GetIO().KeyCtrl || _activeJogDialInputSlot == inputSlot;
            if (editingUnlocked)
            {
                isActive = true;
                ImGui.SetCursorScreenPos(selectableScreenRect.Min);
                ImGui.InvisibleButton("button", selectableScreenRect.GetSize());

                if (ImGui.IsItemActivated() && ImGui.GetIO().KeyCtrl)
                {
                    _jogDialCenter = ImGui.GetIO().MousePos;
                    _activeJogDialInputSlot = inputSlot;
                    drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.WidgetHighlight);
                }

                if (_activeJogDialInputSlot == inputSlot)
                {
                    var restarted = ImGui.IsItemActivated();
                    if (ImGui.IsItemActive())
                    {
                        SingleValueEdit.DrawValueEditMethod(ref value,
                                                            restarted,
                                                            _jogDialCenter,
                                                            double.NegativeInfinity,
                                                            double.PositiveInfinity,
                                                            false,
                                                            false,
                                                            0.025f);
                        inputSlot.SetTypedInputValue((int)value);
                        inputSlot.DirtyFlag.ForceInvalidate();
                    }
                    else
                    {
                        _activeJogDialInputSlot = null;
                    }
                }
            }
          
            ImGui.PopID();
        }

        // Display Int Value
        var intValue = (int)value;
        if (intValue != data.CachedValue)
        {
            data.CachedValue = intValue;
            data.CachedText = intValue.ToString();
        }

        var text = data.CachedText;
        var labelSize = ImGui.CalcTextSize(text);

        var hasName = !string.IsNullOrWhiteSpace(symbolChild.Name);
        var nameWidth = hasName ? ImGui.CalcTextSize(symbolChild.Name).X : 0;
        var padding = 8 * T3Ui.UiScaleFactor;

        // Only draw the value if it doesn't collide with the name
        if (nameWidth + labelSize.X + padding <= selectableScreenRect.GetWidth())
        {
            var space = selectableScreenRect.GetSize() - labelSize - new Vector2(dragWidth, 0);
            var position = selectableScreenRect.Min + space * new Vector2(1, .5f);
            drawList.AddText(MathUtils.Floor(position), UiColors.WidgetValueText, text);
        }

        ImGui.EndGroup();
        ImGui.PopFont();

        ImGui.PopClipRect();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static Vector2 _jogDialCenter;
    private static InputSlot<int>? _activeJogDialInputSlot;
}