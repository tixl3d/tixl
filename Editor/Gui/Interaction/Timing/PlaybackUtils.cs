#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Operator;
using T3.Core.Settings;
using T3.Core.Resource;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction.Timing;

public static class PlaybackUtils
{
    public static BpmProvider BpmProvider = BpmProvider.Instance;
    public static TapProvider TapProvider = TapProvider.Instance;

    internal static void UpdatePlaybackAndSyncing()
    {
        var settings = FindCompositionSettings(out var audioComposition);

        WasapiAudioInput.StartFrame(settings);

        if (settings.Playback.AudioSource == CompositionSettings.AudioSources.ProjectSoundTrack)
        {
            if (settings.TryGetMainSoundtrack(audioComposition, out var soundtrack))
            {
                AudioEngine.UseSoundtrackClip(soundtrack, Playback.Current.TimeInSecs);
            }
        }

        if (settings.Playback.AudioSource == CompositionSettings.AudioSources.ExternalDevice
            && settings.Playback.Syncing == CompositionSettings.SyncModes.Tapping)
        {
            Playback.Current = T3Ui.DefaultBeatTimingPlayback;

            if (Playback.Current.Settings is { Playback.Syncing: CompositionSettings.SyncModes.Tapping })
            {
                if (TapProvider != null)
                {
                    if(TapProvider.BeatTapTriggered)
                        BeatTiming.TriggerSyncTap();

                    if (TapProvider.ResyncTriggered)
                        BeatTiming.TriggerResyncMeasure();

                }

                Playback.Current.Settings.Playback.Bpm = (float)Playback.Current.Bpm;

                // Process callback from [SetBpm] operator
                if (BpmProvider != null && BpmProvider.TryGetNewBpmRate(out var newBpmRate2))
                {
                    Log.Debug($" Setting new bpm rate {newBpmRate2}");
                    BeatTiming.SetBpmRate(newBpmRate2);
                }

                BeatTiming.Update();
            }
        }
        else
        {
            Playback.Current = T3Ui.DefaultTimelinePlayback;
        }

        // Process callback from [SetBpm] operator
        if (BpmProvider != null && BpmProvider.TryGetNewBpmRate(out var newBpmRate))
        {
            Log.Debug($" Applying {newBpmRate} BPM to settings");
            settings.Playback.Bpm = newBpmRate;
        }

        Playback.Current.Bpm = settings.Playback.Bpm;
        Playback.Current.Update(UserSettings.Config.EnableIdleMotion);
        Playback.Current.Settings = settings;
    }

    private static CompositionSettings FindCompositionSettings(out IResourceConsumer? owner)
    {
        var composition = ProjectView.Focused?.CompositionInstance;

        if (composition != null && FindCompositionSettingsForInstance(composition, out var instance, out var settings))
        {
            owner = instance;
            return settings;
        }

        owner = null;
        return _defaultCompositionSettings;
    }

    /// <summary>
    /// Scans the current composition path and its parents for a soundtrack
    /// </summary>
    internal static bool TryFindingSoundtrack([NotNullWhen(true)] out AudioClipResourceHandle? soundtrack,
                                              out IResourceConsumer? composition)
    {
        var settings = FindCompositionSettings(out composition);
        if (composition != null)
            return settings.TryGetMainSoundtrack(composition, out soundtrack);

        soundtrack = null;
        return false;
    }

    /// <summary>
    /// Try to find project settings for an instance by walking up the parent chain.
    /// </summary>
    /// <returns>false if falling back to default settings</returns>
    internal static bool FindCompositionSettingsForInstance(Instance startInstance, out Instance? instanceWithSettings, out CompositionSettings settings)
    {
        instanceWithSettings = startInstance;
        while (true)
        {
            if (instanceWithSettings == null)
            {
                settings = _defaultCompositionSettings;
                instanceWithSettings = null;
                return false;
            }

            settings = instanceWithSettings.Symbol.CompositionSettings;
            if (settings != null && settings.Enabled)
            {
                return true;
            }

            instanceWithSettings = instanceWithSettings.Parent;
        }
    }

    private static readonly CompositionSettings _defaultCompositionSettings = new()
                                                                      {
                                                                          Enabled = false,
                                                                          Playback = new CompositionSettings.PlaybackConfig
                                                                                     {
                                                                                         Bpm = 120,
                                                                                         AudioSource = CompositionSettings.AudioSources.ProjectSoundTrack,
                                                                                         Syncing = CompositionSettings.SyncModes.Timeline,
                                                                                         AudioInputDeviceName = string.Empty,
                                                                                         AudioGainFactor = 1,
                                                                                         AudioDecayFactor = 1,
                                                                                     }
                                                                      };
}
