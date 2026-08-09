#nullable enable
using ManagedBass;

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
            _parameters.WetDryMix = Math.Clamp(Mix.GetValue(context), 0f, 1f) * 100f;
            _parameters.Feedback = Math.Clamp(Feedback.GetValue(context), 0f, 0.99f) * 100f;
            _parameters.LeftDelay = delayMs;
            _parameters.RightDelay = delayMs;
            _parameters.PanDelay = PingPong.GetValue(context) ? 1 : 0;

            _node.Update(context);
        }

        // ManagedBass's DXEchoParameters class fails FXSetParameters with an "illegal parameter" error even
        // for spec-default values (verified by probing), so the parameters are pushed as a raw native struct
        // matching the DirectX DSFXEcho layout instead.
        [StructLayout(LayoutKind.Sequential)]
        private struct EchoParamsNative
        {
            public float WetDryMix;   // 0..100
            public float Feedback;    // 0..100
            public float LeftDelay;   // 1..2000 ms
            public float RightDelay;  // 1..2000 ms
            public int PanDelay;      // BOOL: swap repeats between channels
        }

        private bool PushParams(int fxHandle)
        {
            if (_nativeParamsPtr == IntPtr.Zero)
                _nativeParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<EchoParamsNative>());

            Marshal.StructureToPtr(_parameters, _nativeParamsPtr, false);
            return Bass.FXSetParameters(fxHandle, _nativeParamsPtr);
        }

        // Bus-side callbacks — invoked per realised submix (a node can be realised by more than one bus).
        private void ApplyFx(int submix)
        {
            var fxHandle = Bass.ChannelSetFX(submix, NativeDx8Echo, 0);
            if (fxHandle == 0)
            {
                Log.Warning($"[AudioEcho] failed to set echo: {Bass.LastError}", this);
                return;
            }

            _fxHandles[submix] = fxHandle;
            if (!PushParams(fxHandle))
                Log.Warning($"[AudioEcho] failed to set echo parameters: {Bass.LastError}", this);
        }

        private void UpdateFxParams(int submix)
        {
            if (_fxHandles.TryGetValue(submix, out var fxHandle))
                PushParams(fxHandle);
        }

        private void RemoveFx(int submix)
        {
            _fxHandles.Remove(submix); // freeing the submix frees the effect with it
        }

        ~AudioEcho()
        {
            if (_nativeParamsPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(_nativeParamsPtr);
        }

        // ManagedBass's EffectType misnumbers the DX8 effects (its DXEcho=2 is natively DISTORTION —
        // verified by reading back distortion defaults from the created effect). BASS_FX_DX8_ECHO is 3.
        private const EffectType NativeDx8Echo = (EffectType)3;

        private readonly AudioGraphNode _node;
        private EchoParamsNative _parameters;
        private IntPtr _nativeParamsPtr;
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
