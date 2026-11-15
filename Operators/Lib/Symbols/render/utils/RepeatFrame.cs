using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.render.utils;

/// <summary>
/// Evaluates its subtree as several subframes in time order, the last at the frame time, for accumulating motion blur.
/// Ops that integrate over time (damping, springs) advance by each subframe's share of the frame, so their motion is
/// sampled like any other and their speed doesn't depend on the pass count.
/// </summary>
[Guid("33ab62b0-0517-4953-915d-8e43355f5b4c")]
internal sealed class RepeatFrame : Instance<RepeatFrame>
{
    [Output(Guid = "c950596a-eff7-4ed4-9705-55f6a092cb06")]
    public readonly Slot<Command> Output = new();

    public RepeatFrame()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var passCount = Passes.GetValue(context).Clamp(1, MaxPasses);

        // Limited to one frame: state from before the previous frame is gone, so damped values couldn't follow further.
        var shutter = Shutter.GetValue(context).Clamp(0f, 1f);
        var frameSec = Playback.LastFrameDuration;
        var windowSec = frameSec * shutter;
        var windowBars = context.Playback.BarsFromSeconds(windowSec);

        var keepLocalTime = context.LocalTime;
        var keepLocalFxTime = context.LocalFxTime;
        var keepRenderPass = context.RenderPass;

        for (var passIndex = 0; passIndex < passCount; passIndex++)
        {
            // 0 at the start of the window, 1 at the frame time.
            var progress = passCount == 1 ? 1.0 : passIndex / (double)(passCount - 1);
            var offsetBars = windowBars * (1 - progress);
            context.LocalTime = keepLocalTime - offsetBars;
            context.LocalFxTime = keepLocalFxTime - offsetBars;

            // The first subframe covers the time since the previous frame up to the window; the steps add up to one frame.
            context.RenderPass.RepeatPass = keepRenderPass.RepeatPass + passIndex;
            context.RenderPass.IsSubframe = true;
            context.RenderPass.SubframeStepSec = passCount == 1
                                                     ? frameSec
                                                     : passIndex == 0
                                                         ? frameSec - windowSec
                                                         : windowSec / (passCount - 1);

            // Without invalidation the subtree would return its cached result after the first subframe.
            DirtyFlag.GlobalInvalidationTick++;
            Command.InvalidateGraph();
            Command.GetValue(context);
        }

        context.LocalTime = keepLocalTime;
        context.LocalFxTime = keepLocalFxTime;
        context.RenderPass = keepRenderPass;
    }

    private const int MaxPasses = 64;

    [Input(Guid = "3e1de341-2c26-442f-b5d2-365b6a6e8d01")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "f299f6ae-9cf9-406a-84fc-77f9540d74d6")]
    public readonly InputSlot<int> Passes = new();

    [Input(Guid = "84fc2ace-2cd3-4d2f-82f5-1b5dfacc6b3f")]
    public readonly InputSlot<float> Shutter = new();
}
