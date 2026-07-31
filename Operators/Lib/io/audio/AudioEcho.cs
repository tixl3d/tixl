#nullable enable
using ManagedBass;
using ManagedBass.DirectX8;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds an echo/delay insert to the audio flowing through it: the routing [AudioBus] realises this node as
    /// its own submix and applies a DX8 echo there, so everything wired into <see cref="Input"/> shares the
    /// effect (and the <see cref="Volume"/> fold, like a [CombineAudio]). Unwiring fades the echo tail out.
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
                                               Apply = ApplyFx,
                                               UpdateParams = UpdateFxParams,
                                               Remove = RemoveFx,
                                           }
                        };
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context);

            var delayMs = Math.Clamp(Delay.GetValue(context), 0.001f, 2f) * 1000f;
            _parameters.fWetDryMix = Math.Clamp(Mix.GetValue(context), 0f, 1f) * 100f;
            _parameters.fFeedback = Math.Clamp(Feedback.GetValue(context), 0f, 0.99f) * 100f;
            _parameters.fLeftDelay = delayMs;
            _parameters.fRightDelay = delayMs;
            _parameters.lPanDelay = PingPong.GetValue(context);

            _node.Update(context);
        }

        // Bus-side callbacks — invoked per realised submix (a node can be realised by more than one bus).
        private void ApplyFx(int submix)
        {
            var fxHandle = Bass.ChannelSetFX(submix, EffectType.DXEcho, 0);
            if (fxHandle == 0)
            {
                Log.Warning($"[AudioEcho] failed to set echo: {Bass.LastError}", this);
                return;
            }

            _fxHandles[submix] = fxHandle;
            Bass.FXSetParameters(fxHandle, _parameters);
        }

        private void UpdateFxParams(int submix)
        {
            if (_fxHandles.TryGetValue(submix, out var fxHandle))
                Bass.FXSetParameters(fxHandle, _parameters);
        }

        private void RemoveFx(int submix)
        {
            _fxHandles.Remove(submix); // freeing the submix frees the effect with it
        }

        private readonly AudioGraphNode _node;
        private readonly DXEchoParameters _parameters = new();
        private readonly Dictionary<int, int> _fxHandles = new(); // submix → fx handle

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
