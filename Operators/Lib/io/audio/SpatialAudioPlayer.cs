#nullable enable
using ManagedBass;
using T3.Core.Audio;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Local

namespace Lib.io.audio
{
    /// <summary>
    /// A spatial audio player operator that provides 3D positional audio playback capabilities.
    /// Supports sound positioning, listener orientation, distance-based attenuation, and directional sound cones.
    /// </summary>
    [Guid("8a3c9f2e-4b7d-4e1a-9c5f-7d2e8b1a6c3f")]
    internal sealed class SpatialAudioPlayer : Instance<SpatialAudioPlayer>, ITransformable, ISpatialAudioPropertiesProvider
    {
        #region ITransformable Implementation
        
        IInputSlot ITransformable.TranslationInput => SourcePosition;
        IInputSlot ITransformable.RotationInput => SourceRotation;
        IInputSlot? ITransformable.ScaleInput => null;
        
        public Action<Instance, EvaluationContext>? TransformCallback { get; set; }
        
        #endregion

        #region ISpatialAudioPropertiesProvider Implementation
        
        Vector3 ISpatialAudioPropertiesProvider.SourcePosition => SourcePosition.Value;
        Vector3 ISpatialAudioPropertiesProvider.SourceRotation => SourceRotation.Value;
        Vector3 ISpatialAudioPropertiesProvider.ListenerPosition => ListenerPosition.Value;
        Vector3 ISpatialAudioPropertiesProvider.ListenerRotation => ListenerRotation.Value;
        float ISpatialAudioPropertiesProvider.MinDistance => MinDistance.Value;
        float ISpatialAudioPropertiesProvider.MaxDistance => MaxDistance.Value;
        float ISpatialAudioPropertiesProvider.InnerConeAngle => InnerConeAngle.Value;
        float ISpatialAudioPropertiesProvider.OuterConeAngle => OuterConeAngle.Value;
        GizmoVisibility ISpatialAudioPropertiesProvider.GizmoVisibility => Visibility.Value;
        
        #endregion

        [Input(Guid = "2aa976e3-a63c-4c7e-80a5-a459b3240388")]
        public readonly InputSlot<string> AudioFile = new();

        [Input(Guid = "454a65fa-c0a3-4055-85ec-ac3771b2524c")]
        public readonly InputSlot<bool> PlayAudio = new();

        [Input(Guid = "1800cdcb-8863-423e-a2ec-901cf54e014c")]
        public readonly InputSlot<bool> StopAudio = new();

        [Input(Guid = "d0c660d7-f90b-4e61-95ff-0ce7c76d9d67")]
        public readonly InputSlot<bool> PauseAudio = new();

        [Input(Guid = "7110399e-fd85-4053-9a49-cc09760dceb6")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "ad8822bc-939b-4152-b253-bf01c5aa4d73")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "9c0b519b-45b1-4b28-8c0e-2eba7035a999")]
        public readonly InputSlot<float> Speed = new();

        [Input(Guid = "b921cbd4-4803-48e8-9eb9-f05901cc5802")]
        public readonly InputSlot<float> Seek = new();
        
        [Input(Guid = "f3730e3b-a335-4a13-9b2a-87b8e75cddeb")]
        public readonly InputSlot<Vector3> SourcePosition = new();
        
        [Input(Guid = "3cf1b579-a1f2-4e9f-a54a-0e9e3580a6bf")]
        public readonly InputSlot<Vector3> SourceRotation = new();
        
        [Input(Guid = "732be517-a58e-4f21-8395-7761a95905ea")]
        public readonly InputSlot<float> MinDistance = new();

        [Input(Guid = "7eb612cb-1dbf-4336-b91d-1042573bd7ff")]
        public readonly InputSlot<float> MaxDistance = new();
        
        [Input(Guid = "159c7342-a8c8-4ffb-8a69-da3ffa0d3b71")]
        public readonly InputSlot<Vector3> ListenerPosition = new();

        [Input(Guid = "db9a0aa1-95db-4da4-99fc-31c385773ca8")]
        public readonly InputSlot<Vector3> ListenerRotation = new();
        
        [Input(Guid = "785bb240-2020-4937-b132-b46f542e8986")]
        public readonly InputSlot<float> InnerConeAngle = new();
        
        [Input(Guid = "73971807-98d8-4b8b-b90f-41d673398b2b")]
        public readonly InputSlot<float> OuterConeAngle = new();
        
        [Input(Guid = "b4d639d4-d96b-42e1-a250-efe57d7e9c5e")]
        public readonly InputSlot<float> OuterConeVolume = new();
        
        [Input(Guid = "42d8337a-d419-4038-a132-0e3934d25a2a", MappedType = typeof(Audio3DModes))]
        public readonly InputSlot<int> Audio3DMode = new();

        /// <summary>
        /// Controls the visibility of the transform gizmo.
        /// </summary>
        [Input(Guid = "77d9309e-0c9d-407f-b9b1-dd4b734fa51e")]
        public readonly InputSlot<GizmoVisibility> Visibility = new();
        
        // Outputs

        /// <summary>
        /// Command output for chaining with other operators.
        /// </summary>
        [Output(Guid = "117c0d2e-c14b-471a-8a6d-a9f983e48908")]
        public readonly TransformCallbackSlot<Command> Result = new();


        /// <summary>
        /// Indicates whether the audio is currently playing.
        /// </summary>
        [Output(Guid = "022b0246-73d7-4a99-9c89-3e199171fd6c")]
        public readonly Slot<bool> IsPlaying = new();

        /// <summary>
        /// Indicates whether the audio is currently paused.
        /// </summary>
        [Output(Guid = "52e624ca-508c-44c1-8583-946260e9f660")]
        public readonly Slot<bool> IsPaused = new();

        /// <summary>
        /// Returns the current audio level/amplitude.
        /// </summary>
        [Output(Guid = "ccbff254-1090-4ba2-8341-08fa3cb1540c")]
        public readonly Slot<float> GetLevel = new();


        private Guid _operatorId;
        private bool _wasPausedLastFrame;

        /// <summary>
        /// Gets the current audio file path being played.
        /// </summary>
        public string CurrentFilePath { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialAudioPlayer"/> class.
        /// </summary>
        public SpatialAudioPlayer()
        {
            Result.TransformableOp = this;
            Result.UpdateAction += Update;
            IsPlaying.UpdateAction += Update;
            IsPaused.UpdateAction += Update;
            
            // Do not update on GetLevel - it overrides stale state when result is not evaluating
            //GetLevel.UpdateAction += Update;
        }

        /// <summary>
        /// Updates the spatial audio player state and processes playback.
        /// </summary>
        /// <param name="context">The evaluation context for the current frame.</param>
        private void Update(EvaluationContext context)
        {
            if (_operatorId == Guid.Empty)
            {
                _operatorId = AudioPlayerUtils.ComputeInstanceGuid(InstancePath);
                Log.Gated.Audio($"[SpatialAudioPlayer] Initialized: {_operatorId}");
            }

            string? filePath = AudioFile.GetValue(context);
            bool shouldPlay = PlayAudio.GetValue(context);

            var shouldStop = StopAudio.GetValue(context);
            var shouldPause = PauseAudio.GetValue(context);
            var volume = Volume.GetValue(context);
            var mute = Mute.GetValue(context);
            var sourcePosition = SourcePosition.GetValue(context);
            var listenerPosition = ListenerPosition.GetValue(context);
            var listenerRotation = ListenerRotation.GetValue(context);

            // Convert Euler angles (in degrees) to forward and up vectors
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                listenerRotation.Y * (MathF.PI / 180f),
                listenerRotation.X * (MathF.PI / 180f),
                listenerRotation.Z * (MathF.PI / 180f));
            var listenerForward = Vector3.Transform(new Vector3(0, 0, 1), rotationMatrix);
            var listenerUp = Vector3.Transform(new Vector3(0, 1, 0), rotationMatrix);

            var minDistance = MinDistance.GetValue(context);
            if (minDistance <= 0) minDistance = 1.0f;
            var maxDistance = MaxDistance.GetValue(context);
            if (maxDistance <= minDistance) maxDistance = minDistance + 10.0f;

            var speed = Speed.GetValue(context);
            var seek = Seek.GetValue(context);
            var sourceRotation = SourceRotation.GetValue(context);
            
            // Convert source rotation (Euler angles in degrees) to orientation vector
            var sourceRotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                sourceRotation.Y * (MathF.PI / 180f),
                sourceRotation.X * (MathF.PI / 180f),
                sourceRotation.Z * (MathF.PI / 180f));
            var sourceOrientation = Vector3.Transform(new Vector3(0, 0, 1), sourceRotationMatrix);
            
            var innerConeAngle = Math.Clamp(InnerConeAngle.GetValue(context), 0f, 360f);
            var outerConeAngle = Math.Clamp(OuterConeAngle.GetValue(context), 0f, 360f);
            var outerConeVolume = Math.Clamp(OuterConeVolume.GetValue(context), 0f, 1f);
            var audio3DMode = Math.Clamp(Audio3DMode.GetValue(context), 0, 2);

            AudioEngine.Set3DListenerPosition(listenerPosition, listenerForward, listenerUp);

            // Handle pause/resume transitions
            if (shouldPause != _wasPausedLastFrame)
            {
                if (shouldPause)
                    AudioEngine.PauseSpatialOperator(_operatorId);
                else
                    AudioEngine.ResumeSpatialOperator(_operatorId);
            }
            _wasPausedLastFrame = shouldPause;

            AudioEngine.UpdateSpatialOperatorPlayback(
                operatorId: _operatorId,
                filePath: filePath,
                shouldPlay: shouldPlay,
                shouldStop: shouldStop,
                volume: volume,
                mute: mute,
                position: sourcePosition,
                minDistance: minDistance,
                maxDistance: maxDistance,
                speed: speed,
                seek: seek,
                orientation: sourceOrientation,
                innerConeAngle: innerConeAngle,
                outerConeAngle: outerConeAngle,
                outerConeVolume: outerConeVolume,
                mode3D: audio3DMode);

            IsPlaying.Value = AudioEngine.IsSpatialOperatorStreamPlaying(_operatorId);
            IsPaused.Value = AudioEngine.IsSpatialOperatorPaused(_operatorId);
            GetLevel.Value = AudioEngine.GetSpatialOperatorLevel(_operatorId);
        }

        /// <summary>
        /// Render audio for export. This is called by AudioRendering during export.
        /// </summary>
        /// <param name="startTime">The start time in seconds.</param>
        /// <param name="duration">The duration to render in seconds.</param>
        /// <param name="buffer">The buffer to write audio samples to.</param>
        /// <returns>The number of samples written to the buffer.</returns>
        public int RenderAudio(double startTime, double duration, float[] buffer)
        {
            if (AudioEngine.TryGetSpatialOperatorStream(_operatorId, out var stream) && stream != null)
                return stream.RenderAudio(startTime, duration, buffer, AudioConfig.MixerFrequency, 2);

            Array.Clear(buffer, 0, buffer.Length);
            return buffer.Length;
        }

        /// <summary>
        /// Restores the volume level after an audio export operation.
        /// </summary>
        public void RestoreVolumeAfterExport()
        {
            if (AudioEngine.TryGetSpatialOperatorStream(_operatorId, out var stream) && stream != null)
                Bass.ChannelSetAttribute(stream.StreamHandle, ChannelAttribute.Volume, Volume.Value);
        }

        /// <summary>
        /// Finalizer that unregisters the operator from the audio engine.
        /// </summary>
        ~SpatialAudioPlayer()
        {
            if (_operatorId != Guid.Empty)
                AudioEngine.UnregisterOperator(_operatorId);
        }

        /// <summary>
        /// Defines the 3D audio processing modes.
        /// </summary>
        private enum Audio3DModes
        {
            /// <summary>
            /// Normal 3D audio processing with absolute positioning.
            /// </summary>
            Normal = 0,

            /// <summary>
            /// Relative 3D audio processing where the source position is relative to the listener.
            /// Example: footsteps, breathing, or equipment sounds attached to the listener's position.
            /// </summary>
            Relative = 1,

            /// <summary>
            /// Disables 3D audio processing.
            /// </summary>
            Off = 2
        }

    }
}
