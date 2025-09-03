#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Operator;
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
        var settings = FindPlaybackSettings(out var audioComposition);

        // Should always be at least default settings
        // if (settings == null)
        //     return;

        WasapiAudioInput.StartFrame(settings);
            
        if (settings.AudioSource == PlaybackSettings.AudioSources.ProjectSoundTrack)
        {
            if (settings.TryGetMainSoundtrack(audioComposition, out var soundtrack))
            {
                AudioEngine.UseAudioClip(soundtrack, Playback.Current.TimeInSecs);
            }
        }

        if (settings.AudioSource == PlaybackSettings.AudioSources.ExternalDevice
            && settings.Syncing == PlaybackSettings.SyncModes.Tapping)
        {
            Playback.Current = T3Ui.DefaultBeatTimingPlayback;
                
            if (Playback.Current.Settings is { Syncing: PlaybackSettings.SyncModes.Tapping })
            {
                if (TapProvider != null)
                {
                    if(TapProvider.BeatTapTriggered)
                        BeatTiming.TriggerSyncTap();
                        
                    if (TapProvider.ResyncTriggered)
                        BeatTiming.TriggerResyncMeasure();
                        
                }
                    
                Playback.Current.Settings.Bpm = (float)Playback.Current.Bpm;
                
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
            settings.Bpm = newBpmRate;
        }
        
        Playback.Current.Bpm = settings.Bpm;
        Playback.Current.Update(UserSettings.Config.EnableIdleMotion);
        Playback.Current.Settings = settings;
    }

    private static PlaybackSettings FindPlaybackSettings(out IResourceConsumer? owner)
    {
        var composition = ProjectView.Focused?.CompositionInstance;

        if (composition != null && FindPlaybackSettingsForInstance(composition, out var instance, out var settings))
        {
            owner = instance;
            return settings;
        }
        
        owner = null;
        return _defaultPlaybackSettings;
            
        // var outputWindow = OutputWindow.GetPrimaryOutputWindow();
        //
        // if (outputWindow == null)
        // {
        //     owner = null;
        //     return null;
        // }
        //
        // if (outputWindow.Pinning.TryGetPinnedOrSelectedInstance(out var pinnedOutput, out _))
        // {
        //     if (FindPlaybackSettingsForInstance(pinnedOutput, out var instanceWithSettings, out var settingsFromPinned))
        //     {
        //         owner = instanceWithSettings;
        //         return settingsFromPinned;
        //     }
        //
        //     owner = null;
        //     return GetDefaultPlaybackSettings(composition?.Symbol.SymbolPackage);
        // }
        //
        // owner = null;
        //return null;
    }

    /// <summary>
    /// Scans the current composition path and its parents for a soundtrack 
    /// </summary>
    internal static bool TryFindingSoundtrack([NotNullWhen(true)] out AudioClipResourceHandle? soundtrack, 
                                              out IResourceConsumer? composition)
    {
        var settings = FindPlaybackSettings(out composition);
        if (composition != null)
            return settings.TryGetMainSoundtrack(composition, out soundtrack);
            
        soundtrack = null;
        return false;
    }

    /// <summary>
    /// Try to find playback settings for an instance.
    /// </summary>
    /// <returns>false if falling back to default settings</returns>
    internal static bool FindPlaybackSettingsForInstance(Instance startInstance, out Instance? instanceWithSettings, out PlaybackSettings settings)
    {
        instanceWithSettings = startInstance;
        while (true)
        {
            if (instanceWithSettings == null)
            {
                settings = _defaultPlaybackSettings;
                instanceWithSettings = null;
                return false;
            }
                
            settings = instanceWithSettings.Symbol.PlaybackSettings;
            if (settings != null && settings.Enabled)
            {
                return true;
            }
                
            instanceWithSettings = instanceWithSettings.Parent;
        }
    }

    
    
    private static readonly PlaybackSettings _defaultPlaybackSettings = new()
                                                                   {
                                                                       Enabled = false,
                                                                       Bpm = 120,
                                                                       AudioSource = PlaybackSettings.AudioSources.ProjectSoundTrack,
                                                                       Syncing = PlaybackSettings.SyncModes.Timeline,
                                                                       AudioInputDeviceName = string.Empty,
                                                                       AudioGainFactor = 1,
                                                                       AudioDecayFactor = 1,
                                                                   };
        
    
}