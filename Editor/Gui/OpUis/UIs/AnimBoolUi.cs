#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class AnimBoolUi
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

        [BindInput("a8e49df7-3388-4532-8efe-766ea3a47108")]
        internal readonly InputSlot<float> Rate = null!;
    }

    public static OpUi.CustomUiResult DrawChildUi(Instance instance,
                                                  ImDrawListPtr drawList,
                                                  ImRect screenRect,
                                                  Vector2 canvasScale,
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
                                                  "Anim Boolean", canvasScale))
        {
            isNodeActivated = true;
        }

        // Graph dragging to edit Bias and Ratio
        var h = screenRect.GetHeight();
        var graphRect = screenRect;

        const float relativeGraphWidth = 0.75f;
        
        graphRect.Expand(-3);
        graphRect.Min.X = graphRect.Max.X - graphRect.GetWidth() * relativeGraphWidth;

        var rectHeight = h / 4 - 1;
        var top = new Vector2(graphRect.Max.X - h,
                              graphRect.Min.Y);

        var shadeBg = UiColors.BackgroundFull.Fade(0.2f);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var f1 = y * 4f + x;
                var highlightFactor = 1-((float)(data.NormalizedTime /16 - f1/16) % 1 * 16).Clamp(0,1);
                var c = Color.Mix(shadeBg, UiColors.StatusAnimated, highlightFactor);
                
                drawList.AddRectFilled(new Vector2(top.X + x * rectHeight, top.Y + y * rectHeight),
                                       new Vector2(top.X + (x+1) * rectHeight-1, top.Y + (y+1) * rectHeight-1),
                                       c);
            }
        }
        
        //var highlightEditable = ImGui.GetIO().KeyCtrl;
        //
        // ImGui.SetCursorScreenPos(graphRect.Min);
        // if (ImGui.GetIO().KeyCtrl)
        // {
        //     ImGui.InvisibleButton("dragMicroGraph", graphRect.GetSize());
        //
        //     if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)
        //         && ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsItemActive())
        //     {
        //         //isGraphActive = true;
        //     }
        //
        //     if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
        //     {
        //         ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        //     }
        // }
        
        ImGui.PopID();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip
               | (isNodeActivated ? OpUi.CustomUiResult.IsActive : OpUi.CustomUiResult.None);
    }

    // private static float _dragStartBias;
    // private static float _dragStartRatio;

    private static readonly Vector2[] _graphLinePoints = new Vector2[GraphListSteps];
    private const int GraphListSteps = 80;
}