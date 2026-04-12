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

// ReSharper disable once UnusedType.Global
internal static class TriggerAnimUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;

            var methodInfo = instance.GetType().GetMethod("CalcNormalizedValueForFraction",
                                                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            CalcValueFunc = (Func<double, int, float>)Delegate.CreateDelegate(typeof(Func<double, int, float>), instance, methodInfo!);
        }

        private readonly Instance _instance;

        internal readonly Func<double, int, float> CalcValueFunc;

        [BindField("LastFraction")]
        private readonly FieldInfo? _lastFractionField = null!;

        internal double LastFraction => (double)(_lastFractionField?.GetValue(_instance) ?? 0);

        [BindInput("c0fa79d5-2c49-4d40-998f-4eb0101ae050")]
        internal readonly InputSlot<int> Shape = null!;

        [BindInput("0d56fc27-fa15-4f1e-aa09-f97af93d42c7")]
        internal readonly InputSlot<float> Duration = null!;

        [BindInput("3AD8E756-7720-4F43-85DA-EFE1AF364CFE")]
        internal readonly InputSlot<float> Base = null!;

        [BindInput("287fa06c-3e18-43f2-a4e1-0780c946dd84")]
        internal readonly InputSlot<float> Amplitude = null!;

        [BindInput("214e244a-9e95-4292-81f5-cd0199f05c66")]
        internal readonly InputSlot<float> Delay = null!;

        [BindInput("9bfd5ae3-9ca6-4f7b-b24b-f554ad4d0255")]
        internal readonly InputSlot<float> Bias = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  ScalableCanvas canvas,
                                                  ref OpUiBinding? data1)
    {
        data1 ??= new Binding(instance);
        var binding = (Binding)data1;

        if (!binding.IsValid)
            return OpUi.CustomUiResult.None;

        ImGui.PushID(instance.SymbolChildId.GetHashCode());
        // if (RateEditLabel.Draw(ref triggerAnimation.Rate.TypedInputValue.Value,
        //                        screenRect, drawList, nameof(triggerAnimation) + " " + (LFO.Shapes)triggerAnimation.Shape.TypedInputValue.Value))
        // {
        //     triggerAnimation.Rate.Input.IsDefault = false;
        //     triggerAnimation.Rate.DirtyFlag.Invalidate();
        // }

        var isEditActive = false;

        var h = screenRect.GetHeight();
        var graphRect = screenRect;

        if (h < 10)
        {
            ImGui.PopID();
            return OpUi.CustomUiResult.None;
        }

        graphRect.Expand(-3);

        //graphRect.Min.X = graphRect.Max.X - graphRect.GetWidth();
        var graphWidth = graphRect.GetWidth();
        drawList.PushClipRect(graphRect.Min, graphRect.Max, true);

        var highlightEditable = ImGui.GetIO().KeyCtrl;

        if (h > 14)
        {
            isEditActive |= ValueLabel.Draw(drawList, graphRect, new Vector2(1, 0), binding.Amplitude);
            isEditActive |= ValueLabel.Draw(drawList, graphRect, new Vector2(1, 1), binding.Base);
        }

        // Graph dragging to edit Bias and Ratio
        var isGraphActive = false;

        ImGui.SetCursorScreenPos(graphRect.Min);
        if (ImGui.GetIO().KeyCtrl)
        {
            ImGui.InvisibleButton("dragMicroGraph", graphRect.GetSize());
            isGraphActive = ImGui.IsItemActive();
        }

        var duration = binding.Duration.GetCurrentValue();

        if (isGraphActive)
        {
            isEditActive = true;
            var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 1);

            if (ImGui.IsItemActivated())
            {
                //_dragStartPosition = ImGui.GetMousePos();
                _dragStartBias = binding.Bias.GetCurrentValue(); // data.Bias.TypedInputValue.Value;
                _dragStartDuration = binding.Duration.GetCurrentValue();
            }

            if (MathF.Abs(dragDelta.X) > 0.5f)
            {
                binding.Duration.SetTypedInputValue((_dragStartDuration + dragDelta.X / 100f).Clamp(0.001f, 4f));
            }

            if (Math.Abs(dragDelta.Y) > 0.5f)
            {
                binding.Bias.SetTypedInputValue((_dragStartBias - dragDelta.Y / 100f).Clamp(0.01f, 0.99f));
            }
        }

        var delay = binding.Delay.GetCurrentValue(); //.Value;

        // Draw Graph
        {
            const float previousCycleFragment = 0.02f;
            const float relativeX = previousCycleFragment / (1 + previousCycleFragment);

            // Horizontal line
            var lh1 = graphRect.Min + Vector2.UnitY * h / 2;
            var lh2 = new Vector2(graphRect.Max.X, lh1.Y + 1);
            drawList.AddRectFilled(lh1, lh2, UiColors.WidgetAxis);

            // Vertical start line
            var lv1 = graphRect.Min + Vector2.UnitX * (int)(graphWidth * relativeX);
            var lv2 = new Vector2(lv1.X + 1, graphRect.Max.Y);
            drawList.AddRectFilled(lv1, lv2, UiColors.WidgetAxis);

            // Fragment line
            var cycleWidth = graphWidth * (1 - relativeX);
            var dx = new Vector2(((float)binding.LastFraction * duration + delay) * cycleWidth - 1, 0);
            drawList.AddRectFilled(lv1 + dx, lv2 + dx, UiColors.WidgetActiveLine);

            // Draw graph
            //        lv
            //        |  2-------3    y
            //        | /
            //  0-----1 - - - - - -   lh
            //        |
            //        |

            var shapeIndex = binding.Shape.GetCurrentValue().Clamp(0, 5);

            for (var i = 0; i < GraphListSteps; i++)
            {
                var f = (float)i / GraphListSteps;
                var fragment = f * (1 + previousCycleFragment) - previousCycleFragment;
                GraphLinePoints[i] = new Vector2((f * duration + delay) * graphWidth,
                                                 (0.5f -
                                                  //binding.CalcNormalizedValueForFraction(fragment, shapeIndex)
                                                  binding.CalcValueFunc(fragment, shapeIndex)
                                                  / 2) * h
                                                ) + graphRect.Min;
            }

            var curveLineColor = highlightEditable ? UiColors.WidgetLineHover : UiColors.WidgetLine;
            drawList.AddPolyline(ref GraphLinePoints[0], GraphListSteps, curveLineColor, ImDrawFlags.None, 1.5f);
        }
        drawList.PopClipRect();
        ImGui.PopID();
        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isEditActive ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static float _dragStartBias;
    private static float _dragStartDuration;

    private static readonly Vector2[] GraphLinePoints = new Vector2[GraphListSteps];

    private const int GraphListSteps = 80;
}