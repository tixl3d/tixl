#nullable enable
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Audio;

namespace Lib.io.audio
{
    /// <summary>
    /// THROWAWAY SPIKE (G4) — a leaf audio source that plays an audio file (.wav / .mp3 / .ogg) as a looping
    /// BASS decode stream and exposes it on its <see cref="AudioGraphNode"/> for an [AudioBus] to route (or,
    /// left loose, auto-plays via the implicit collector). Proves real files through the audio graph. Superseded
    /// by the canonical [AudioClip] once it emits an AudioReference — delete then.
    /// </summary>
    [Guid("c3f10a50-1e42-4c8a-9f31-0ab1cd2e0100")]
    internal sealed class _AudioFileSpike : Instance<_AudioFileSpike>, IAudioSource
    {
        [Output(Guid = "c3f10a50-0001-4c8a-9f31-0ab1cd2e0100")]
        public readonly Slot<AudioGraphNode> Result = new();

        public _AudioFileSpike()
        {
            _node = new AudioGraphNode(this);
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            EnsureStream(Path.GetValue(context));
            _node.SourceChannel = _stream;
            _node.Gain = Volume.GetValue(context);
            _node.Update(context);
        }

        // IAudioSource — context-free path used by the implicit collector for loose (unwired) auto-play.
        Slot<AudioGraphNode> IAudioSource.AudioReferenceOutput => Result;

        void IAudioSource.EnsureChannelFromStaticInputs()
        {
            EnsureStream(Path.TypedInputValue.Value);
            _node.SourceChannel = _stream;
            _node.Gain = Volume.TypedInputValue.Value;
        }

        private void EnsureStream(string? path)
        {
            if (path == _loadedPath && _stream != 0)
                return;

            // Path changed (or first load) — drop the old stream.
            if (_stream != 0)
            {
                BassMix.MixerRemoveChannel(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            _loadedPath = path;
            _node.SourceLabel = string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrEmpty(path))
                return;

            if (!AudioMixerManager.IsInitialized)
            {
                AudioMixerManager.Initialize();
                if (!AudioMixerManager.IsInitialized)
                    return;
            }

            // Resolves project-relative (e.g. "pixtur.Playground:audio/x.wav") or absolute paths.
            if (!T3.Core.Resource.FileResource.TryGetFileResource(path, this, out var file) || file.FileInfo is not { Exists: true })
            {
                Log.Warning($"[_AudioFileSpike] audio file not found: '{path}'", this);
                return;
            }

            _stream = Bass.CreateStream(file.FileInfo.FullName, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Loop);
            if (_stream == 0)
                Log.Warning($"[_AudioFileSpike] failed to load '{path}': {Bass.LastError}", this);
        }

        ~_AudioFileSpike()
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream);
                _stream = 0;
            }
        }

        private readonly AudioGraphNode _node;
        private int _stream;
        private string? _loadedPath;

        [Input(Guid = "c3f10a50-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<string> Path = new();

        [Input(Guid = "c3f10a50-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();
    }
}
