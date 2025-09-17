#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Operator;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Draws playback settings
/// </summary>
/// <remarks>
/// Controlling the primary soundtrack is finicky:
/// - "Add soundtrack" adds a <see cref="AudioClipResourceHandle"/> with empty filepath. 
/// - When modifying the path we use to resolve the path (i.e. verify if file exists) before setting the filepath.
/// - If valid and set, <see cref="AudioEngine"/> will then load them in CompleteFrame.
/// 
/// </remarks>
internal static class PlaybackSettingsPopup
{
    /// <returns>true if composition was modified</returns>
    internal static bool DrawPlaybackSettings(Instance? composition)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));
        ImGui.SetNextWindowSize(new Vector2(650, 500) * T3Ui.UiScaleFactor);
        if (!ImGui.BeginPopupContextItem(PlaybackSettingsPopupId))
        {
            ImGui.PopStyleVar(1);
            return false;
        }

        FrameStats.Current.OpenedPopUpName = PlaybackSettingsPopupId;
        FrameStats.Current.OpenedPopupHovered = ImGui.IsWindowHovered();
        FrameStats.Current.IsItemContextMenuOpen = true;

        ImGui.PushFont(Fonts.FontLarge);
        ImGui.TextUnformatted("Playback settings");
        ImGui.PopFont();

        if (composition == null)
        {
            CustomComponents.EmptyWindowMessage("no composition active");
            ImGui.EndPopup();
            ImGui.PopStyleVar(1);

            return false;
        }

        FormInputs.SetIndentToLeft();

        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var compositionWithSettings, out var settings);
        //var compositionSettings = compWithSoundtrack == composition ? composition.Symbol.PlaybackSettings : null;

        // Main toggle with composition name 
        var isEnabledForCurrent = compositionWithSettings == composition && settings is { Enabled: true };
        var modified = false;

        if (FormInputs.AddCheckBox("Specify settings for", ref isEnabledForCurrent))
        {
            modified = true;
            if (isEnabledForCurrent)
            {
                settings = composition.Symbol.PlaybackSettings;
                if (settings == null)
                {
                    settings = new PlaybackSettings();
                    composition.Symbol.PlaybackSettings = settings;
                }

                compositionWithSettings = composition;
                settings.Enabled = true;
                Playback.Current.Settings = settings;
            }
            else
            {
                // ReSharper disable once PossibleNullReferenceException
                settings.Enabled = false;
            }
        }

        ImGui.SameLine();
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(composition.Symbol.Name);
        ImGui.PopFont();

        // Explanation hint
        string hint;
        if (isEnabledForCurrent)
        {
            hint = "You're defining new settings for this Project Operator.";
        }
        else if (compositionWithSettings != null && compositionWithSettings != composition)
        {
            hint = $"Inheriting settings from {compositionWithSettings.Symbol.Name}";
        }
        else
        {
            hint = string.Empty;
        }

        FormInputs.AddHint(hint);

        if (!isEnabledForCurrent)
        {
            CustomComponents.EmptyWindowMessage("No settings");
            ImGui.EndPopup();
            ImGui.PopStyleVar(1);
            FormInputs.SetIndentToParameters();
            return modified;
        }

        FormInputs.SetIndentToParameters();


        if (FormInputs.AddSegmentedButtonWithLabel(ref settings.AudioSource, "Audio Source"))
        {
            modified = true;
            UpdatePlaybackAndTimeline(settings);
        }

        FormInputs.AddVerticalSpace();
            
        ImGui.Separator();

        switch (settings.AudioSource)
        {
            case PlaybackSettings.AudioSources.ProjectSoundTrack:
            {
                if (!settings.TryGetMainSoundtrack(compositionWithSettings, out var soundtrackHandle))
                {
                    if (ImGui.Button("Add soundtrack to composition"))
                    {
                        modified = true;
                        settings.AudioClips.Add(new AudioClipDefinition()
                                                    {
                                                        IsSoundtrack = true,
                                                    });
                        _tempSoundtrackFilepathForEdit = string.Empty;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(soundtrackHandle.Clip.FilePath))
                    {
                        _tempSoundtrackFilepathForEdit = string.Empty;
                    }
                    else
                    {
                        var isSoundtrackFileValid = soundtrackHandle.TryGetFileResource(out _);
                        if (isSoundtrackFileValid)
                        {
                            if (ImGui.IsWindowAppearing())
                            {
                                _tempSoundtrackFilepathForEdit = soundtrackHandle.Clip.FilePath;
                            }
                        }
                        else
                        {
                            Log.Warning($"Removing invalid soundtrack file: {soundtrackHandle.Clip.FilePath}");
                            soundtrackHandle.Clip.FilePath = string.Empty;
                            modified = true;
                        }
                    }

                    var editResult = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, 
                                                                       AllFilesAudioFilesMp3WavOggMp3WavOgg,
                                                                       ref _tempSoundtrackFilepathForEdit);
                
                
                    var filepathModified = (editResult & InputEditStateFlags.Modified) != 0;
                    if (filepathModified)
                    {
                        modified = true;
                        if (!string.IsNullOrEmpty(_tempSoundtrackFilepathForEdit))
                        {
                            _warningMessage = soundtrackHandle.TryToApplyFilePath(_tempSoundtrackFilepathForEdit, composition)
                                                  ? string.Empty
                                                  : "File not found?";
                        }
                        else
                        {
                            _warningMessage = string.Empty;
                        }
                    }
                
                    FormInputs.ApplyIndent();
                    if (ImGui.Button("Reload"))
                    {
                        AudioEngine.ReloadClip(soundtrackHandle);
                        AudioImageFactory.ResetImageCache();
                        modified = true;
                        filepathModified = true;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Remove"))
                    {
                        settings.AudioClips.Remove(soundtrackHandle.Clip);
                        modified = true;
                    }

                    FormInputs.AddVerticalSpace();

                    if (FormInputs.AddFloat("BPM",
                                            ref soundtrackHandle.Clip.Bpm,
                                            0,
                                            1000,
                                            0.02f,
                                            true, true,
                                            "In T3 animation units are in bars.\nThe BPM rate controls the animation speed of your project.",
                                            120))
                    {
                        Playback.Current.Bpm = soundtrackHandle.Clip.Bpm;
                        settings.Bpm = soundtrackHandle.Clip.Bpm;
                        modified = true;
                    }

                    var soundtrackStartTime = (float)soundtrackHandle.Clip.StartTime;

                    if (FormInputs.AddFloat("Offset",
                                            ref soundtrackStartTime,
                                            -100,
                                            100,
                                            0.02f,
                                            false, true,
                                            "Offsets the beginning of the soundtrack in seconds.",
                                            0))
                    {
                        soundtrackHandle.Clip.StartTime = soundtrackStartTime;
                        modified = true;
                    }

                    FormInputs.AddEnumDropdown(ref UserSettings.Config.TimeDisplayMode, "Display Timeline in");

                    if (FormInputs.AddFloat("Resync Threshold",
                                            ref ProjectSettings.Config.AudioResyncThreshold,
                                            0.001f,
                                            0.1f,
                                            0.001f,
                                            true, true,
                                            "If audio playbacks drifts too far from the animation playback it will be resynced. If the threshold for this is too low you will encounter audio glitches. If the threshold is too large you will lose precision. A normal range is between 0.02s and 0.05s.",
                                            ProjectSettings.Defaults.AudioResyncThreshold))

                    {
                        modified = true;
                    }

                    modified |= FormInputs.AddFloat("AudioDecay", ref settings.AudioDecayFactor,
                                        0.001f,
                                        1f,
                                        0.01f,
                                        true, true,
                                        "The decay factors controls the impact of [AudioReaction] when AttackMode. Good values strongly depend on style, loudness and variation of input signal.",
                                        0.9f);
                    
                    if (filepathModified)
                    {
                        composition.Symbol.GetSymbolUi().FlagAsModified();
                        AudioEngine.ReloadClip(soundtrackHandle);
                        UpdateBpmFromSoundtrackConfig(soundtrackHandle.Clip);
                        UpdatePlaybackAndTimeline(settings);
                    }
                }

                break;
            }
            case PlaybackSettings.AudioSources.ExternalDevice:
            {
                FormInputs.AddVerticalSpace();

                if (FormInputs.AddSegmentedButtonWithLabel(ref settings.Syncing, "Sync Mode"))
                {
                    UpdatePlaybackAndTimeline(settings);
                    modified = true;
                }

                if (settings.Syncing == PlaybackSettings.SyncModes.Tapping)
                {
                    FormInputs.SetIndentToParameters();
                    FormInputs.AddHint("""
                                       Tap the [Sync] button on every beat.
                                       The right click on measure to resync and refine.
                                       """);


                    modified |= FormInputs.AddCheckBox("Enable audio beat lock",
                                           ref settings.EnableAudioBeatLocking,
                                           """
                                           If enabled, the editor will look for transient bass, hihats and snares and attempt to look the playback onto the incoming audio signal.
                                           To use this, start by tapping the base beat (e.g. with X) then tap the beginning of the bar with (e.g. with X).
                                           From now on, you will see the BPM be constantly sliding to look onto the beat).
                                           """,
                                           true
                                          );
                    FormInputs.AddVerticalSpace();
                }

                if (!settings.EnableAudioBeatLocking)
                {
                    modified |= FormInputs.AddFloat("BPM",
                                                  ref settings.Bpm,
                                                  0,
                                                  1000,
                                                  0.02f,
                                                  true, true,
                                                  """
                                                  In T3 animation units are in bars.
                                                  The BPM rate controls the animation speed of your project.
                                                  """,
                                                  120);
                }

                FormInputs.SetIndentToParameters();
                modified |= FormInputs.AddFloat("Beat Sync Offset (sec)",
                                                ref settings.BeatLockAudioOffsetSec,
                                                -1f, 1f, 0.001f, 
                                                true, true,
                                                """
                                                When using beat lock through audio analysis, you can slightly offset the phase. 

                                                This might be useful to tighten the sync between audio and video, e.g. if the visual output is delayed by video-processing devices.
                                                """,
                                                0);

                
                FormInputs.AddVerticalSpace();

                
                // var isInitialized = playback is BeatTimingPlayback;
                // if (!isInitialized)
                // {
                //     playback = new BeatTimingPlayback();
                // }

                modified |= FormInputs.AddFloat("AudioGain", ref settings.AudioGainFactor , 0.01f, 100, 0.01f, true, true,
                                    """Can be used to adjust the input signal (e.g. in live situation where the input level might vary.""",
                                    1);

                modified |= FormInputs.AddFloat("AudioDecay", ref settings.AudioDecayFactor,
                                    0.001f,
                                    1f,
                                    0.01f,
                                    true, true,
                                    "The decay factors controls the impact of [AudioReaction] when AttackMode. Good values strongly depend on style, loudness and variation of input signal.",
                                    0.9f);
                

                // Input meter
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                FormInputs.DrawInputLabel("Input Level");
                ImGui.PopStyleVar();
                ImGui.InvisibleButton("##gainMeter", new Vector2(-1, ImGui.GetFrameHeight()));
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var dl = ImGui.GetWindowDrawList();

                var level = settings.AudioGainFactor * WasapiAudioInput.DecayingAudioLevel * 0.03f;

                dl.AddRectFilled(min, new Vector2(min.X + level, max.Y), UiColors.BackgroundHover);

                FormInputs.DrawInputLabel("Input Device");
                ImGui.BeginGroup();

                if (ImGui.BeginCombo("##SelectDevice", settings.AudioInputDeviceName, ImGuiComboFlags.HeightLarge))
                {
                    foreach (var d in WasapiAudioInput.InputDevices)
                    {
                        var isSelected = d.DeviceInfo.Name == settings.AudioInputDeviceName;
                        if (ImGui.Selectable($"{d.DeviceInfo.Name}", isSelected, ImGuiSelectableFlags.DontClosePopups))
                        {
                            Bass.Configure(Configuration.UpdateThreads, false);

                            settings.AudioInputDeviceName = d.DeviceInfo.Name;
                            modified = true;
                            ProjectSettings.Save();
                            //WasapiAudioInput.StartInputCapture(d);
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.PushFont(Fonts.FontSmall);
                            var sb = new StringBuilder();
                            var di = d.DeviceInfo;

                            var fields = typeof(WasapiDeviceInfo).GetProperties();
                            foreach (var f in fields)
                            {
                                sb.Append(f.Name);
                                sb.Append(": ");
                                sb.Append(f.GetValue(di));
                                sb.Append("\n");
                            }

                            ImGui.TextUnformatted(sb.ToString());
                            ImGui.PopFont();
                            ImGui.EndTooltip();
                        }
                    }
                    ImGui.EndCombo();
                }
                
                if (!string.IsNullOrEmpty(settings.AudioInputDeviceName)
                    &&settings.AudioInputDeviceName != WasapiAudioInput.ActiveInputDeviceName)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
                    ImGui.TextUnformatted(settings.AudioInputDeviceName + " (NOT FOUND)");
                    ImGui.PopStyleColor();
                }
                
                ImGui.EndGroup();
                break;
            }
        }

        ImGui.EndPopup();
        ImGui.PopStyleVar(1);
        return modified;
    }

    private static void UpdatePlaybackAndTimeline(PlaybackSettings settings)
    {
        if (settings.AudioSource == PlaybackSettings.AudioSources.ProjectSoundTrack)
        {
            Playback.Current = T3Ui.DefaultTimelinePlayback;
                
            if (settings.AudioClips.Count > 0)
            {
                Bass.Configure(Configuration.UpdateThreads, true);
                Bass.Free();
                Bass.Init();
                Bass.Start();
                Playback.Current.Bpm = settings.AudioClips[0].Bpm;
                Playback.Current.Settings.Syncing = PlaybackSettings.SyncModes.Timeline;
            }

            UserSettings.Config.ShowTimeline = true;
        }
        else
        {
            if (settings.Syncing == PlaybackSettings.SyncModes.Tapping)
            {
                Playback.Current = T3Ui.DefaultBeatTimingPlayback;
                UserSettings.Config.ShowTimeline = false;
                UserSettings.Config.EnableIdleMotion = true;
                Bass.Configure(Configuration.UpdateThreads, true);
                    
                Bass.Free();
                Bass.Init();
                Bass.Start();
                Playback.Current.PlaybackSpeed = 1;
            }
            else
            {
                Playback.Current = T3Ui.DefaultTimelinePlayback;
                UserSettings.Config.ShowTimeline = true;
                Playback.Current.PlaybackSpeed = 0;

            }
        }
    }

    private static void UpdateBpmFromSoundtrackConfig(AudioClipDefinition? audioClip)
    {
        if (audioClip == null || string.IsNullOrEmpty(audioClip.FilePath))
        {
            Log.Error("Can't detected BPM-rate from empty undefined audio-clip filename");
            return;
        }
            
        var matchBpmPattern = new Regex(@"(\d+\.?\d*)bpm");
        var result = matchBpmPattern.Match(audioClip.FilePath);
        if (!result.Success)
            return;

        if (float.TryParse(result.Groups[1].Value, out var bpm))
        {
            Log.Debug($"Using bpm-rate {bpm} from filename.");
            audioClip.Bpm = bpm;
        }
    }

    /** We use this for modification inside the input field and checking if path is valid before actually assigning it to the soundtrack */
    private static string? _tempSoundtrackFilepathForEdit = string.Empty;

    private static string _warningMessage = string.Empty;
    public const string PlaybackSettingsPopupId = "##PlaybackSettings";
    private const string AllFilesAudioFilesMp3WavOggMp3WavOgg = "Audio files (mp3,wav,ogg)|*.mp3;*.wav;*.ogg";
}