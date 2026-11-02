#nullable enable
using T3.Core.Audio;
using T3.Core.Audio.Native;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds an echo/delay insert to the audio flowing through it: the routing [AudioBus] realises this node as
    /// its own bus with an echo in it, so everything wired into <see cref="Input"/> shares the effect (and the
    /// <see cref="Volume"/> fold, like a [CombineAudio]). Unwiring lets the echo tail ring out and fade.
    /// </summary>
    [Guid("a7c54fb0-1e42-4c8a-9f31-0ab1cd2e0500")]
    internal sealed class AudioEcho : Instance<AudioEcho>
    {
        [Output(Guid = "a7c54fb0-0001-4c8a-9f31-0ab1cd2e0500")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioEcho()
        {
            _node = new AudioGraphNode(this, Input)
                        {
                            FxInsert = new AudioGraphNode.AudioFxInsert
                                           {
                                               Effect = AudioEffectKind.Echo,
                                               Apply = _ => { },
                                               UpdateParams = ApplyParameters,
                                               Remove = _ => { },
                                           }
                        };
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context);
            _delaySecs = Math.Clamp(Delay.GetValue(context), 0.001f, 2f);
            _mix = Math.Clamp(Mix.GetValue(context), 0f, 1f);
            _feedback = Math.Clamp(Feedback.GetValue(context), 0f, 0.99f);
            _pingPong = PingPong.GetValue(context);
            _node.Update(context);
        }

        private void ApplyParameters(AudioHandle bus) => AudioMixerManager.SetEcho(bus, _delaySecs, _feedback, _mix, _pingPong);

        private readonly AudioGraphNode _node;
        private float _delaySecs;
        private float _mix;
        private float _feedback;
        private bool _pingPong;

        [Input(Guid = "a7c54fb0-0002-4c8a-9f31-0ab1cd2e0500")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "a7c54fb0-0003-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "a7c54fb0-0004-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Mix = new();

        [Input(Guid = "a7c54fb0-0005-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Delay = new();

        [Input(Guid = "a7c54fb0-0006-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Feedback = new();

        [Input(Guid = "a7c54fb0-0007-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<bool> PingPong = new();
    }
}
