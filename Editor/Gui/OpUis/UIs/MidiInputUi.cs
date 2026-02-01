#nullable enable
using ImGuiNET;
using System.Reflection;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis.UIs;

// ReSharper disable once UnusedType.Global
internal static class MidiInputUi
{
    private sealed class Binding : OpUiBinding
    {
        internal Binding(Instance instance)
        {
            IsValid = AutoBind(instance);
            _instance = instance;
        }

        private readonly Instance _instance;

        [BindField("LastMessageTime")]
        private readonly FieldInfo? _lastMessageTimeFieldField = null!;

        internal double LastMessageTime => (double)(_lastMessageTimeFieldField?.GetValue(_instance) ?? 0);

        [BindInput("DF81B7B3-F39E-4E5D-8B97-F29DD576A76D")]
        internal readonly InputSlot<int> Control = null!;

        [BindInput("9B0D32DE-C53C-4DF6-8B29-5E68A5A9C5F9")]
        internal readonly InputSlot<int> Channel = null!;

        [BindInput("23C34F4C-4BA3-4834-8D51-3E3909751F84")]
        internal readonly InputSlot<string> Device = null!;

        [BindInput("AAD1E576-F144-423F-83B5-5694B1119C23")]
        internal readonly InputSlot<Vector2> OutputRange = null!;

        [BindOutput("01706780-D25B-4C30-A741-8B7B81E04D82")]
        internal readonly Slot<float> Result = null!;
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

        var dragWidth = WidgetElements.DrawOperatorDragHandle(screenRect, drawList, canvas.Scale);
        screenRect.Min.X += dragWidth;
        // Draw label and current value
        ImGui.SetCursorScreenPos(screenRect.Min);
        ImGui.BeginGroup();
        ImGui.PushClipRect(screenRect.Min, screenRect.Max, true);

        const float flashDuration = 0.6f;
        // Flash on changes
        var flashProgress = (float)(Playback.RunTimeInSecs - data.LastMessageTime).Clamp(0, flashDuration) / flashDuration;
        if (flashProgress < 1)
        {
            drawList.AddRectFilled(screenRect.Min, screenRect.Max,
                                   Color.Mix(UiColors.StatusAnimated.Fade(0.8f),
                                             Color.Transparent,
                                             MathF.Pow(flashProgress * flashProgress, 0.5f)));
        }

        ImGui.PushFont(Fonts.FontSmall);
        var h = screenRect.GetHeight();
        var renamedTitle = instance.SymbolChild.Name;
        var deviceAndChannel = "Midi Device?";
        if (!string.IsNullOrEmpty(data.Device.TypedInputValue.Value))
        {
            var _displayControlValue = data.Control.TypedInputValue.Value.ToString();
            var _displayChannelValue = data.Channel.TypedInputValue.Value.ToString();
            var _displayDeviceValue = data.Device.TypedInputValue.Value;
            if (data.Control.HasInputConnections)
                _displayControlValue = data.Control.DirtyFlag.IsDirty ? "??" : data.Control.Value.ToString();
            if (data.Channel.HasInputConnections)
                _displayChannelValue = data.Channel.DirtyFlag.IsDirty ? "??" : data.Channel.Value.ToString();
            if (data.Device.HasInputConnections)
                _displayDeviceValue = data.Device.DirtyFlag.IsDirty ? "??" : data.Device.Value;
            if (!string.IsNullOrEmpty(renamedTitle))
            {
                _displayDeviceValue = renamedTitle;
            }
            if(h > 35 * T3Ui.UiScaleFactor)
                deviceAndChannel = $"{_displayDeviceValue} CH{_displayChannelValue}:{_displayControlValue}\n{data.Result.Value:0.00}";
            else
                deviceAndChannel = $"{_displayDeviceValue} CH{_displayChannelValue}:{_displayControlValue}|{data.Result.Value:0.00}";
        }

        WidgetElements.DrawPrimaryTitle(drawList, screenRect, deviceAndChannel, canvas.Scale);

        ImGui.PopClipRect();
        ImGui.EndGroup();

        // Drag mini graph
        var graphRect = screenRect;
        const int padding = -3;

        graphRect.Expand(padding);
        if (graphRect.GetHeight() > 0 && graphRect.GetWidth() > 0)
        {
            var minRange = data.OutputRange.TypedInputValue.Value.X;
            var maxRange = data.OutputRange.TypedInputValue.Value.Y;
            var currentValue = data.Result.Value;

            var xPos = MathUtils.RemapAndClamp((double)currentValue, minRange, maxRange, graphRect.Min.X, graphRect.Max.X);
            var topLeftPos = new Vector2((float)xPos, graphRect.Min.Y);
            drawList.AddRectFilled(topLeftPos, topLeftPos + new Vector2(1, graphRect.GetHeight()), Color.Mix(UiColors.BackgroundFull,
                                             UiColors.StatusAnimated,
                                             MathF.Pow(flashProgress * flashProgress, 0.5f)));
        }

        ImGui.PopFont();

        return OpUi.CustomUiResult.Rendered
               | OpUi.CustomUiResult.PreventOpenSubGraph
               | OpUi.CustomUiResult.PreventInputLabels
               | OpUi.CustomUiResult.PreventTooltip;
    }
}