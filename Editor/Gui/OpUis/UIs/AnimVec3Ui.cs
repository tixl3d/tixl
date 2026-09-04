#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

internal static class AnimVec3Ui
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindField("_normalizedTimeX")]
        private readonly FieldInfo? _normalizedTimeXField = null!;

        internal double NormalizedTimeX => (double)(_normalizedTimeXField?.GetValue(_instance) ?? 0);

        [BindField("_normalizedTimeY")]
        private readonly FieldInfo? _normalizedTimeYField = null!;

        internal double NormalizedTimeY => (double)(_normalizedTimeYField?.GetValue(_instance) ?? 0);

        [BindField("_normalizedTimeZ")]
        private readonly FieldInfo? _normalizedTimeZField = null!;

        internal double NormalizedTimeZ => (double)(_normalizedTimeZField?.GetValue(_instance) ?? 0);

        [BindField("_shape")]
        private readonly FieldInfo? _shapeField = null!;

        internal AnimMath.Shapes Shape => (AnimMath.Shapes)(_shapeField?.GetValue(_instance) ?? 0);

        [BindInput("754456d0-1ac8-4e31-8d0b-bf1b45db48de")]
        internal readonly InputSlot<float> RateFactor = null!;

        [BindInput("6086F33D-E8AA-4F66-830E-8624E19E186C")]
        internal readonly InputSlot<float> AmplitudeFactor = null!;

        [BindInput("42ABBD5C-5AE3-41C1-BEBD-CE19D2CE0E25")]
        internal readonly InputSlot<float> Bias = null!;

        [BindInput("FF71A701-802F-44EC-966D-4E04557CDBE6")]
        internal readonly InputSlot<float> Ratio = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var data = (Binding)data1;

        if (!data.IsValid)
            return OpUi.CustomUiResult.None;

        var isNodeActivated = false;
        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        if (WidgetElements.DrawRateLabelWithTitle(data.RateFactor,
                                                  screenRect,
                                                  drawList,
                                                  "Anim3 " + data.Shape,
                                                  canvas.Scale))
        {
            isNodeActivated = true;
        }

        var h = screenRect.GetHeight();
        var graphRect = screenRect;

        const float relativeGraphWidth = 0.75f;

        graphRect.Expand(-3);
        graphRect.Min.X = graphRect.Max.X - graphRect.GetWidth() * relativeGraphWidth;

        var highlightEditable = ImGui.GetIO().KeyCtrl;

        if (h > 14)
        {
            isNodeActivated |= ValueLabel.Draw(drawList, graphRect, new Vector2(1, 0), data.AmplitudeFactor);
        }

        // Graph dragging to edit Bias and Ratio
        var isGraphActive = false;
        ImGui.SetCursorScreenPos(graphRect.Min);
        if (ImGui.GetIO().KeyCtrl)
        {
            ImGui.InvisibleButton("dragMicroGraph", graphRect.GetSize());

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsItemActive())
            {
                isGraphActive = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            }
        }

        if (isGraphActive)
        {
            isNodeActivated = true;
            var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 1);

            if (ImGui.IsItemActivated())
            {
                _dragStartBias = data.Bias.TypedInputValue.Value;
                _dragStartRatio = data.Ratio.TypedInputValue.Value;
            }

            if (Math.Abs(dragDelta.X) > 0.5f)
            {
                data.Ratio.SetTypedInputValue((_dragStartRatio + dragDelta.X / 100f).Clamp(0.001f, 1f));
            }

            if (Math.Abs(dragDelta.Y) > 0.5f)
            {
                data.Bias.SetTypedInputValue((_dragStartBias - dragDelta.Y / 100f).Clamp(0.01f, 0.99f));
            }
        }

        DrawCurve(drawList, graphRect, data, highlightEditable);

        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isNodeActivated ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static void DrawCurve(ImDrawListPtr drawList, ImRect graphRect, Binding data, bool highlightEditable)
    {
        var graphWidth = graphRect.GetWidth();
        var h = graphRect.GetHeight();

        // Draw Graph
        {
            const float previousCycleFragment = 0.25f;
            const float relativeX = previousCycleFragment / (1 + previousCycleFragment);

            // Horizontal line
            var lh1 = graphRect.Min + Vector2.UnitY * h / 2;
            var lh2 = new Vector2(graphRect.Max.X, lh1.Y + 1);
            drawList.AddRectFilled(lh1, lh2, UiColors.WidgetAxis);

            // Vertical start line 
            var lv1 = graphRect.Min + Vector2.UnitX * (int)(graphWidth * relativeX);
            var lv2 = new Vector2(lv1.X + 1, graphRect.Max.Y);
            drawList.AddRectFilled(lv1, lv2, UiColors.WidgetAxis);

            // Fragment lines 
            {
                var cycleWidth = graphWidth * (1 - relativeX);
                var dx = new Vector2((float)MathUtils.Fmod(data.NormalizedTimeX, 1f) * cycleWidth - 1, 0);
                drawList.AddRectFilled(lv1 + dx, lv2 + dx, UiColors.WidgetActiveLine);
            }

            {
                var cycleWidth = graphWidth * (1 - relativeX);
                var dx = new Vector2((float)MathUtils.Fmod(data.NormalizedTimeY, 1f) * cycleWidth - 1, 0);
                drawList.AddRectFilled(lv1 + dx, lv2 + dx, UiColors.WidgetActiveLine);
            }

            {
                var cycleWidth = graphWidth * (1 - relativeX);
                var dx = new Vector2((float)MathUtils.Fmod(data.NormalizedTimeZ, 1f) * cycleWidth - 1, 0);
                drawList.AddRectFilled(lv1 + dx, lv2 + dx, UiColors.WidgetActiveLine);
            }

            // Draw graph
            //        lv
            //        |  2-------3    y
            //        | /
            //  0-----1 - - - - - -   lh
            //        |
            //        |

            for (var i = 0; i < GraphListSteps; i++)
            {
                var f = (float)i / GraphListSteps;
                var fragment = f * (1 + previousCycleFragment) - previousCycleFragment + Math.Floor(data.NormalizedTimeX);

                var v = AnimMath.CalcValueForNormalizedTime(data.Shape,
                                                            fragment,
                                                            0,
                                                            data.Bias.TypedInputValue.Value,
                                                            data.Ratio.TypedInputValue.Value).Clamp(-1, 1);
                var vv = (0.5f - v / 2) * h;

                _graphLinePoints[i] = new Vector2(f * graphWidth,
                                                  vv
                                                 ) + graphRect.Min;
            }

            var curveLineColor = highlightEditable ? UiColors.WidgetLineHover : UiColors.WidgetLine;
            drawList.AddPolyline(ref _graphLinePoints[0], GraphListSteps, curveLineColor, ImDrawFlags.None, 1.5f);
        }
    }

    private static float _dragStartBias;
    private static float _dragStartRatio;

    private static readonly Vector2[] _graphLinePoints = new Vector2[GraphListSteps];
    private const int GraphListSteps = 80;
}