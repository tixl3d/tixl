#nullable enable
using T3.Core.Audio;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderAudioInfo
{
    public static int SoundtrackChannels()
    {
        var composition = ProjectView.Focused?.CompositionInstance;
        if (composition == null)
            return AudioEngine.GetClipChannelCount(null);

        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var settings);
        if (settings.TryGetMainSoundtrack(instanceWithSettings, out var soundtrack))
            return AudioEngine.GetClipChannelCount(soundtrack);

        return AudioEngine.GetClipChannelCount(null);
    }

    public static int SoundtrackSampleRate()
    {
        var composition = ProjectView.Focused?.CompositionInstance;
        if (composition == null)
            return AudioEngine.GetClipSampleRate(null);

        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var settings);
        return AudioEngine.GetClipSampleRate(settings.TryGetMainSoundtrack(instanceWithSettings, out var soundtrack)
                                                 ? soundtrack
                                                 : null);
    }
}