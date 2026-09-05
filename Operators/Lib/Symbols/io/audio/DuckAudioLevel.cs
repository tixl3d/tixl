#nullable enable
using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.audio
{
    /// <summary>
    /// Converts a level signal (from an [AudioLevel] tap) into a smoothed ducking gain: while the level
    /// exceeds <see cref="Threshold"/>, the gain dips toward 1 − <see cref="Amount"/> with an attack ramp,
    /// and recovers to 1 with a release ramp when it falls below. Wire <see cref="Gain"/> into the Volume of
    /// a [CombineAudio] or [AudioBus] (multiply with an existing volume via a math op if needed) — e.g. duck
    /// music under a voice-over.
    /// </summary>
    [Guid("e8a32d90-1e42-4c8a-9f31-0ab1cd2e0300")]
    internal sealed class DuckAudioLevel :Instance<DuckAudioLevel>    {
        [Output(Guid = "e8a32d90-0001-4c8a-9f31-0ab1cd2e0300", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<float> Gain = new();

        public DuckAudioLevel()
        {
            Gain.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            var level = Level.GetValue(context);
            var threshold = Threshold.GetValue(context);
            var amount = Amount.GetValue(context).Clamp(0f, 1f);
            var attack = Math.Max(0.001f, Attack.GetValue(context));
            var release = Math.Max(0.001f, Release.GetValue(context));

            var gainAndBias = GainAndBias.GetValue(context).Clamp(Vector2.Zero, Vector2.One);

            // Ducking follows the realtime audio signal, not the timeline.
            var now = Playback.RunTimeInSecs;
            var dt = (float)(now - _lastEvalTime).Clamp(0, 0.2);
            _lastEvalTime = now;

            // Reduction scales with how far the level sits between the threshold and full scale;
            // GainAndBias shapes that response curve (the knee), not the smoothing.
            var over = level.RemapAndClamp(threshold, 1f, 0f, 1f).ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);
            var target = 1f - amount * over;

            // Exponential smoothing; ducking (down) uses Attack, recovering (up) uses Release.
            // Keep the coefficient pure: it's frame-rate dependent, so reshaping it would warp the
            // time constants unpredictably.
            var tau = target < _gain ? attack : release;
            var k = 1f - MathF.Exp(-dt / tau);

            _gain += (target - _gain) * k;
            MathUtils.ApplyDefaultIfInvalid(ref _gain, 1f);

            Gain.Value = _gain;
        }

        private float _gain = 1f;
        private double _lastEvalTime;

        [Input(Guid = "e8a32d90-0002-4c8a-9f31-0ab1cd2e0300")]
        public readonly InputSlot<float> Level = new();

        [Input(Guid = "e8a32d90-0003-4c8a-9f31-0ab1cd2e0300")]
        public readonly InputSlot<float> Threshold = new();

        [Input(Guid = "e8a32d90-0004-4c8a-9f31-0ab1cd2e0300")]
        public readonly InputSlot<float> Amount = new();

        [Input(Guid = "e8a32d90-0005-4c8a-9f31-0ab1cd2e0300")]
        public readonly InputSlot<float> Attack = new();

        [Input(Guid = "e8a32d90-0006-4c8a-9f31-0ab1cd2e0300")]
        public readonly InputSlot<float> Release = new();
        
        [Input(Guid = "1FA75271-73C7-47D3-B536-1BFCC134FF73")]
        public readonly InputSlot<Vector2> GainAndBias = new();
    }
}
