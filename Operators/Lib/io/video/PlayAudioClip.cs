#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;

namespace Lib.io.video;

[Guid("c2b2758a-5b3e-465a-87b7-c6a13d3fba48")]
internal sealed class PlayAudioClip : Instance<PlayAudioClip>, IStatusProvider
{
    [Output(Guid = "93B2B489-A522-439E-AC9E-8D47C073D721", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Result = new();
        
        
    public PlayAudioClip()
    {
        Result.UpdateAction += Update;
        _audioClipResource = new Resource<SoundtrackClipDefinition>(Path, TryCreateClip);
        _audioClipResource.AddDependentSlots(Result);
    }

    private bool TryCreateClip(FileResource file, SoundtrackClipDefinition? currentValue, [NotNullWhen(true)] out SoundtrackClipDefinition? newClip, out string failureReason)
    {
        var fileInfo = file.FileInfo;
        if (fileInfo is { Exists: true })
        {
            newClip = new SoundtrackClipDefinition
                           {
                               FilePath = Path.GetCurrentValue(),
                               StartTime = 0,
                               EndTime = 0,
                               IsSoundtrack = true,
                               LengthInSeconds = 0,
                               Volume = Volume.GetCurrentValue(),
                               Id = Guid.NewGuid(),
                           };
                
            failureReason = string.Empty;
            _errorMessageForStatus = string.Empty;
            return true;
        }

        newClip = null;
        failureReason = $"File not found: {Path.GetCurrentValue()}";
        _errorMessageForStatus = failureReason;
        return false;
    }

    private void Update(EvaluationContext context)
    {
        var audioClip = _audioClipResource.GetValue(context);
            
        var isTimeDirty = TimeInSecs.DirtyFlag.IsDirty;
        var timeParameter = TimeInSecs.GetValue(context);
            
        if (!TimeInSecs.HasInputConnections && isTimeDirty)
        {
            _startRunTimeInSecs = Playback.RunTimeInSecs - timeParameter;
        }

        if (audioClip != null && IsPlaying.GetValue(context))
        {
            var targetTime = TimeInSecs.HasInputConnections
                                 ? timeParameter
                                 : Playback.RunTimeInSecs - _startRunTimeInSecs;
                
            if(IsLooping.GetValue(context) && targetTime > audioClip.LengthInSeconds)
            {
                targetTime %= audioClip.LengthInSeconds;
            }
                
            //Log.Debug($" Playing at {targetTime:0.0}", this);
            AudioEngine.UseSoundtrackClip(new(audioClip, this),  targetTime);
            audioClip.Volume =  Volume.GetValue(context);
        }
    }

    private double _startRunTimeInSecs;
    private readonly Resource<SoundtrackClipDefinition> _audioClipResource;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return string.IsNullOrEmpty(_errorMessageForStatus) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _errorMessageForStatus;
    }
        
    private string _errorMessageForStatus = string.Empty;

        [Input(Guid = "e0c927f9-5528-49c5-b457-5aea51f51628")]
        public readonly InputSlot<string> Path = new InputSlot<string>();

        [Input(Guid = "7ABB80B8-5EA3-48F3-9F28-19DACA9B8648")]
        public readonly InputSlot<float> TimeInSecs = new InputSlot<float>();

        [Input(Guid = "B8610A84-EA18-486C-8DBE-208C19A9DB28")]
        public readonly InputSlot<float> Volume = new InputSlot<float>();

        [Input(Guid = "989C84C4-F085-4839-89FD-BCF0F33A04E8")]
        public readonly InputSlot<bool> IsLooping = new InputSlot<bool>();

        [Input(Guid = "86CF4C08-242D-4491-871E-EACCDB628238")]
        public readonly InputSlot<bool> IsPlaying = new InputSlot<bool>();
}