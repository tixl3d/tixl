#nullable enable
using T3.Core.Audio;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderAudioInfo
{
    /// <summary>
    /// Returns the channel count for audio export.
    /// This always returns 2 (stereo) since the mixer is configured for stereo output.
    /// </summary>
    public static int SoundtrackChannels()
    {
        // The mixer is always stereo (2 channels) - see AudioMixerManager.Initialize()
        // GetFullMixDownBuffer also uses AudioEngine.GetClipChannelCount(null) which defaults to 2
        return 2;
    }

    /// <summary>
    /// Returns the sample rate for audio export.
    /// This always returns the mixer sample rate since GetFullMixDownBuffer renders audio at the mixer frequency.
    /// </summary>
    public static int SoundtrackSampleRate()
    {
        // Audio is always rendered at the mixer sample rate during export
        // (GetFullMixDownBuffer uses AudioConfig.MixerFrequency for all mixing)
        return AudioConfig.MixerFrequency;
    }
}