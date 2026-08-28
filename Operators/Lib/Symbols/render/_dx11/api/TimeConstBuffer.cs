using T3.Core.Animation;

namespace Lib.render._dx11.api;

[Guid("de8bc97a-8ef0-4d4a-9ffa-88046a2daf40")]
internal sealed class TimeConstBuffer : Instance<TimeConstBuffer>
{
    [Output(Guid = "{6C118567-8827-4422-86CC-4D4D00762D87}", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer> Buffer = new();

    public TimeConstBuffer()
    {
        Buffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //Log.Debug("LastFrame duration:" + Playback.LastFrameDuration);
        var bufferContent = new TimeBufferLayout(
                                                 (float)Playback.Current.TimeInBars, 
                                                 (float)context.LocalTime, 
                                                 (float)Playback.RunTimeInSecs,
                                                 (float)context.Playback.FxTimeInBars,
                                                 (float)Playback.LastFrameDuration);
        ResourceManager.SetupConstBuffer(bufferContent, ref Buffer.Value);
        Buffer.Value.DebugName = nameof(TimeConstBuffer);
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct TimeBufferLayout
    {
        public TimeBufferLayout(float globalTime, float time, float runTime, float beatTime, float lastFrameDuration)
        {
            GlobalTime = globalTime;
            Time = time;
            RunTime = runTime;
            BeatTime = beatTime;
            LastFrameDuration = lastFrameDuration;
        }

        [FieldOffset(0)]
        public float GlobalTime;
        [FieldOffset(4)]
        public float Time;
        [FieldOffset(8)]
        public float RunTime;
        [FieldOffset(12)]
        public float BeatTime;
        [FieldOffset(16)]
        public float LastFrameDuration;

    }       
}