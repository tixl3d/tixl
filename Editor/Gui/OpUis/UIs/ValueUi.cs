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

// ReSharper disable once UnusedType.Global

internal static class ValueUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
        }

        [BindInput("7773837e-104a-4b3d-a41f-cadbd9249af2")]
        internal readonly InputSlot<float> Float = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect area,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var dragWidth = WidgetElements.DrawOperatorDragHandle(area, drawList, canvas.Scale);
        area.Min.X += dragWidth;

        drawList.AddRectFilled(area.Min, area.Max, UiColors.BackgroundFull.Fade(0.1f));

        var symbolChild = instance.SymbolChild;
        drawList.PushClipRect(area.Min, area.Max, true);

        double value = data.Float.GetCurrentValue();

        var isActive = false;
        // Draw slider
        const float rangeMin = 0f;
        const float rangeMax = 1f;

        if (MathF.Abs(rangeMax - rangeMin) > 0.0001f)
        {
            var f = MathUtils.NormalizeAndClamp((float)value, rangeMin, rangeMax);
            var w = (int)area.GetWidth() * f;
            drawList.AddRectFilled(area.Min,
                                   new Vector2(area.Min.X + w, area.Max.Y),
                                   UiColors.WidgetSlider);

            drawList.AddRectFilled(new Vector2(area.Min.X + w, area.Min.Y),
                                   new Vector2(area.Min.X + w + 1, area.Max.Y),
                                   UiColors.WidgetActiveLine);
        }

        // Interaction
        {
            ImGui.PushID(instance.GetHashCode());
            var editingUnlocked = ImGui.GetIO().KeyCtrl || _activeJogDialInputSlot != null;
            var inputSlot = data.Float;
            if (editingUnlocked)
            {
                isActive = true;
                ImGui.SetCursorScreenPos(area.Min);
                ImGui.InvisibleButton("button", area.GetSize());

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
                        inputSlot.SetTypedInputValue((float)value);
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

        // Label if instance has title
        if (!string.IsNullOrEmpty(symbolChild.Name))
        {
            WidgetElements.DrawPrimaryTitle(drawList, area, symbolChild.Name, canvas.Scale);
            WidgetElements.DrawSmallValue(drawList, area, $"{value:0.000}", canvas.Scale);
        }
        else
        {
            WidgetElements.DrawPrimaryValue(drawList, area, $"{value:0.000}", canvas.Scale);
        }

        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static Vector2 _jogDialCenter;
    private static InputSlot<float>? _activeJogDialInputSlot;
}