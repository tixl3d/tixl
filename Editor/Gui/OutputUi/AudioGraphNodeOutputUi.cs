#nullable enable

using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio.Graph;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.RenderExport;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Plays the shown audio node through a preview bus per view, so a [CombineAudio], an effect or a sampler can
/// be heard without wiring an [AudioBus]. Editor-only: nothing is previewed during a render export, and sources
/// a real bus already plays are left to it. When the view stops showing the node, the bus registry pauses
/// the preview and its sources fall back to their own playback.
/// </summary>
internal sealed class AudioGraphNodeOutputUi : OutputUi<AudioGraphNode>
{
    public override IOutputUi Clone()
    {
        return new AudioGraphNodeOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        base.Recompute(slot, context);
        _recomputedSlot = slot;
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<AudioGraphNode> { Value: { } node })
            return;

        CustomComponents.StylizedText(node.ToString(), Fonts.FontNormal, UiColors.Text);

        // Without a recompute the node is last frame's, and a view that doesn't update its value shouldn't sound.
        if (!ReferenceEquals(_recomputedSlot, slot) || RenderProcess.IsExporting)
            return;

        _recomputedSlot = null;

        var router = _previewRouters.GetValue(viewId, _ => new AudioBusRouter(yieldsToOtherBuses: true, logSource: null));
        if (!router.BeginFrame())
            return;

        router.Add(node);
        var transportStopped = (Playback.Current?.PlaybackSpeed ?? 0) == 0;
        router.Route(1f, transportStopped);

        DrawLevelMeter(router.Level);
        CustomComponents.StylizedText("Editor preview — not included in render exports", Fonts.FontSmall, UiColors.TextMuted);
    }

    private static void DrawLevelMeter(float level)
    {
        var height = ImGui.GetFrameHeight();
        var width = MathF.Round(MeterWidth * T3Ui.UiScaleFactor);
        var min = ImGui.GetCursorScreenPos();
        min = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y + height * 0.35f));
        var max = new Vector2(min.X + width, MathF.Floor(min.Y + height * 0.3f));

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.3f));

        var filledWidth = MathF.Round(width * Math.Clamp(level, 0f, 1f));
        if (filledWidth > 0)
            drawList.AddRectFilled(min, new Vector2(min.X + filledWidth, max.Y), UiColors.ForegroundFull.Fade(0.6f));

        ImGui.Dummy(new Vector2(width, height));
    }

    private const float MeterWidth = 120;

    private ISlot? _recomputedSlot;

    /** Shared across output UIs so a view keeps one preview bus while it switches between audio nodes. */
    private static readonly ConditionalWeakTable<string, AudioBusRouter> _previewRouters = [];
}
