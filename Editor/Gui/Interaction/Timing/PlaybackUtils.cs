#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Audio.Graph;
using T3.Core.IO;
using T3.Core.Operator;
using T3.Core.Settings;
using T3.Core.Resource;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Core.Audio.Input;

namespace T3.Editor.Gui.Interaction.Timing;

public static class PlaybackUtils
{
    public static BpmProvider BpmProvider = BpmProvider.Instance;
    public static TapProvider TapProvider = TapProvider.Instance;

    /// <summary>
    /// Declares the clips audible at the current playhead. Live playback and video export both call this,
    /// so that an export renders what was heard: the engine frees a stream the first frame it isn't registered.
    /// </summary>
    internal static void RegisterAudioClips(CompositionSettings settings, IResourceConsumer? audioComposition)
    {
        if (settings.Playback.AudioSource == CompositionSettings.AudioSources.ProjectSoundTrack)
        {
            // Register every clip in the composition's AudioClips list so the engine
            // can play multiple clips simultaneously. The engine's per-clip TimeRange
            // bound-check decides which clips are audible at the current playhead.
            var time = Playback.Current.TimeInSecs;
            foreach (var clip in settings.Playback.AudioClips)
            {
                if (string.IsNullOrEmpty(clip.AssetPath))
                    continue;
                AudioEngine.UseSoundtrackClip(clip.GetResourceHandle(audioComposition), time);
            }
        }

        // Op-backed audio clips ([AudioClip]) with AutoPlay register themselves here, independent of
        // the AudioSource mode. Coexists with the CompositionSettings layer clips above while the
        // op-backed audio-clip system is being migrated in.
        AudioClipCollector.RegisterAutoPlayClips(TryGetFocusedComposition(),
                                                 Playback.Current.TimeInBars,
                                                 Playback.Current.TimeInSecs);
    }

    internal static void UpdatePlaybackAndSyncing()
    {
        var settings = FindCompositionSettings(out var audioComposition);

        AudioInput.StartFrame(settings);

        RegisterAudioClips(settings, audioComposition);

        // Play "loose" audio-graph sources (whose output isn't wired into any bus) through an implicit
        // default bus, so basic audio plays without an [AudioBus] op. Wired sources go through their bus.
        AudioGraphCollector.CollectLooseSources(TryGetFocusedComposition());

        if (settings.Playback.UsesBeatTapping)
        {
            // Seed the tap clock from the project BPM when entering tapping mode — otherwise the
            // BeatTiming default leaks back into the settings via the write-back below.
            if (Playback.Current is not BeatTimingPlayback)
            {
                BeatTiming.SetBpmRate(settings.Playback.Bpm);
            }

            Playback.Current = T3Ui.DefaultBeatTimingPlayback;

            if (Playback.Current.Settings is { Playback.UsesBeatTapping: true })
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

    internal static CompositionSettings FindCompositionSettings(out IResourceConsumer? owner)
    {
        var composition = TryGetFocusedComposition();

        if (composition != null && FindCompositionSettingsForInstance(composition, out var instance, out var settings))
        {
            owner = instance;
            return settings;
        }

        owner = null;
        return _defaultCompositionSettings;
    }

    /// <summary>
    /// The focused composition, or null. Reading <c>CompositionInstance</c> resolves the instance's symbol,
    /// which throws when that symbol is not registered yet - reachable whenever a project is created,
    /// reopened or removed while the editor runs, and this runs every frame.
    /// </summary>
    private static Instance? TryGetFocusedComposition()
    {
        return ProjectView.Focused?.TryGetCompositionInstance();
    }

    /// <summary>
    /// Scans the current composition path and its parents for a soundtrack. Each level is asked for a
    /// main soundtrack directly (settings-list entries and op-flagged [AudioClip]s alike) — an op-provided
    /// soundtrack must be found in whichever composition holds it, independent of where (or whether)
    /// project settings are enabled.
    /// </summary>
    internal static bool TryFindingSoundtrack([NotNullWhen(true)] out AudioClipResourceHandle? soundtrack,
                                              out IResourceConsumer? composition)
    {
        var instance = TryGetFocusedComposition();
        while (instance != null)
        {
            // Same guard as the settings walk: InstanceView.Symbol throws for a composition whose symbol
            // is not registered (a project created or removed while the editor runs), and this runs every
            // frame. No settings here means no soundtrack at this level; keep walking to the parent.
            if (TryGetCompositionSettings(instance, out var levelSettings)
                && levelSettings.TryGetMainSoundtrack(instance, out soundtrack))
            {
                composition = instance;
                return true;
            }

            instance = instance.Parent;
        }

        soundtrack = null;
        composition = null;
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

            // A composition whose symbol is not registered (a project created or removed while the
            // editor was running, or one whose package failed to load) used to throw straight out of
            // the render loop and kill the editor. Treat it as "no settings here" and keep walking.
            if (!TryGetCompositionSettings(instanceWithSettings, out var candidate))
            {
                if (!_reportedMissingSymbol)
                {
                    _reportedMissingSymbol = true;
                    Log.Warning($"No composition settings: symbol for '{instanceWithSettings}' is not registered. Using the default playback settings.");
                }

                instanceWithSettings = instanceWithSettings.Parent;
                continue;
            }

            settings = candidate;
            if (settings.Enabled)
            {
                return true;
            }

            instanceWithSettings = instanceWithSettings.Parent;
        }
    }

    private static bool TryGetCompositionSettings(Instance instance, [NotNullWhen(true)] out CompositionSettings? settings)
    {
        try
        {
            settings = instance.Symbol.CompositionSettings;
            return settings != null;
        }
        catch (Exception)
        {
            // InstanceView.Symbol throws when the symbol is not in the registry.
            settings = null;
            return false;
        }
    }

    private static bool _reportedMissingSymbol;

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
