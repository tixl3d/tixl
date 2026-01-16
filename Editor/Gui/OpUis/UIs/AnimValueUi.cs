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
internal static class AnimValueUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindField("_normalizedTime")]
        private readonly FieldInfo? _normalizedTimeField = null!;

        internal double NormalizedTime => (double)(_normalizedTimeField?.GetValue(_instance) ?? 0);

        [BindInput("48005727-0158-4795-ad70-8410c27fd01d")]
        internal readonly InputSlot<float> Rate = null!;

        [BindInput("4cf5d20b-7335-4584-b246-c260ac5cdf4f")]
        internal readonly InputSlot<int> Shape = null!;

        [BindInput("79917ef7-64ca-4825-9c6a-c9b2a7f6ff86")]
        internal readonly InputSlot<float> Amplitude = null!;

        [BindInput("ddd93b06-118e-43e0-85f6-c150faf91d04")]
        internal readonly InputSlot<float> Offset = null!;

        [BindInput("f12fee9a-dd91-40c2-9aa5-ea34804a858d")]
        internal readonly InputSlot<float> Bias = null!;

        [BindInput("8327e7ec-4370-4a3e-bd69-db3f4aa4b1d7")]
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
        if (WidgetElements.DrawRateLabelWithTitle(data.Rate,
                                                  screenRect,
                                                  drawList,
                                                  "Anim " + (AnimMath.Shapes)data.Shape.TypedInputValue.Value, canvas.Scale))
        {
            isNodeActivated = true;
            // animValue.Rate.Input.IsDefault = false;
            // animValue.Rate.DirtyFlag.Invalidate();
        }

        // Graph dragging to edit Bias and Ratio
        var h = screenRect.GetHeight();
        var graphRect = screenRect;

        const float relativeGraphWidth = 0.75f;

        graphRect.Expand(-3);
        graphRect.Min.X = graphRect.Max.X - graphRect.GetWidth() * relativeGraphWidth;

        var highlightEditable = ImGui.GetIO().KeyCtrl;

        if (h > 30 * T3Ui.UiScaleFactor)
        {
            isNodeActivated |= ValueLabel.Draw(drawList, graphRect, new Vector2(1, 0), data.Amplitude);
            isNodeActivated |= ValueLabel.Draw(drawList, graphRect, new Vector2(1, 1), data.Offset);
        }

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

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
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
                //_dragStartPosition = ImGui.GetMousePos();
                _dragStartBias = data.Bias.TypedInputValue.Value;
                _dragStartRatio = data.Ratio.TypedInputValue.Value;
            }

            if (Math.Abs(dragDelta.X) > 0.5f)
            {
                data.Ratio.TypedInputValue.Value = (_dragStartRatio + dragDelta.X / 100f).Clamp(0.001f, 1f);
                data.Ratio.DirtyFlag.Invalidate();
                data.Ratio.Input.IsDefault = false;
            }

            if (Math.Abs(dragDelta.Y) > 0.5f)
            {
                data.Bias.TypedInputValue.Value = (_dragStartBias - dragDelta.Y / 100f).Clamp(0.01f, 0.99f);
                data.Bias.DirtyFlag.Invalidate();
                data.Bias.Input.IsDefault = false;
            }
        }

        // Draw Curve
        {
            var graphWidth = graphRect.GetWidth();
            var h1 = graphRect.GetHeight();

            var shapeIndex = data.Shape.HasInputConnections // Todo check for animated 
                                 ? data.Shape.Value
                                 : data.Shape.TypedInputValue.Value;

            var shape1 = (AnimMath.Shapes)shapeIndex.Clamp(0, Enum.GetNames(typeof(AnimMath.Shapes)).Length);

            // Draw Graph
            {
                const float previousCycleFragment = 0.25f;
                const float relativeX = previousCycleFragment / (1 + previousCycleFragment);

                // Horizontal line
                var lh1 = graphRect.Min + Vector2.UnitY * h1 / 2;
                var lh2 = new Vector2(graphRect.Max.X, lh1.Y + 1);
                drawList.AddRectFilled(lh1, lh2, UiColors.WidgetAxis);

                // Vertical start line 
                var lv1 = graphRect.Min + Vector2.UnitX * (int)(graphWidth * relativeX);
                var lv2 = new Vector2(lv1.X + 1, graphRect.Max.Y);
                drawList.AddRectFilled(lv1, lv2, UiColors.WidgetAxis);

                // Fragment line 
                var cycleWidth = graphWidth * (1 - relativeX);
                var normalizedTime = data.NormalizedTime;
                var dx = new Vector2(MathUtils.Fmod((float)normalizedTime, 1f) * cycleWidth - 1, 0);

                drawList.AddRectFilled(lv1 + dx, lv2 + dx, UiColors.WidgetActiveLine);

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
                    var fragment = f * (1 + previousCycleFragment) - previousCycleFragment + Math.Floor(normalizedTime);

                    var v = AnimMath.CalcValueForNormalizedTime(shape1,
                                                                fragment,
                                                                0,
                                                                data.Bias.TypedInputValue.Value,
                                                                data.Ratio.TypedInputValue.Value).Clamp(-1, 1);
                    var vv = (0.5f - v / 2) * h1;

                    _graphLinePoints[i] = new Vector2(f * graphWidth,
                                                      vv
                                                     ) + graphRect.Min;
                }

                var curveLineColor = highlightEditable ? UiColors.WidgetLineHover : UiColors.WidgetLine;
                drawList.AddPolyline(ref _graphLinePoints[0], GraphListSteps, curveLineColor, ImDrawFlags.None, 1.5f);
            }
        }

        ImGui.PopID();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isNodeActivated ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    private static float _dragStartBias;
    private static float _dragStartRatio;

    private static readonly Vector2[] _graphLinePoints = new Vector2[GraphListSteps];
    private const int GraphListSteps = 80;
}