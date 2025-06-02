using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.InputsAndTypes;
using Icon = T3.Editor.Gui.Styling.Icon;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace T3.Editor.Gui.Windows.TimeLine;

internal static class TimeControls
{
    internal static void HandleTimeControlActions()
    {
        var playback = Playback.Current; // TODO, this should be non-static eventually

        if (KeyboardBinding.Triggered(UserActions.TapBeatSync))
            BeatTiming.TriggerSyncTap();

        if (KeyboardBinding.Triggered(UserActions.TapBeatSyncMeasure))
            BeatTiming.TriggerResyncMeasure();

        if (KeyboardBinding.Triggered(UserActions.PlaybackJumpToPreviousKeyframe))
            UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToPreviousKeyframe);

        if (KeyboardBinding.Triggered(UserActions.PlaybackJumpToStartTime))
            playback.TimeInBars = playback.IsLooping ? playback.LoopRange.Start : 0;

        if (KeyboardBinding.Triggered(UserActions.PlaybackJumpToPreviousKeyframe))
            UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToPreviousKeyframe);

        {
            //const float editFrameRate = 30;

            var frameDuration = UserSettings.Config.FrameStepAmount switch
                                    {
                                        FrameStepAmount.FrameAt60Fps => 1 / 60f,
                                        FrameStepAmount.FrameAt30Fps => 1 / 30f,
                                        FrameStepAmount.FrameAt15Fps => 1 / 15f,
                                        FrameStepAmount.Bar          => (float)playback.SecondsFromBars(1),
                                        FrameStepAmount.Beat         => (float)playback.SecondsFromBars(1 / 4f),
                                        FrameStepAmount.Tick         => (float)playback.SecondsFromBars(1 / 16f),
                                        _                            => 1
                                    };

            var editFrameRate = 1 / frameDuration;

            // Step to previous frame
            if (KeyboardBinding.Triggered(UserActions.PlaybackPreviousFrame))
            {
                var rounded = Math.Round(playback.TimeInSecs * editFrameRate) / editFrameRate;
                playback.TimeInSecs = rounded - frameDuration;
            }

            if (KeyboardBinding.Triggered(UserActions.PlaybackJumpBack))
            {
                playback.TimeInBars -= 1;
            }

            // Step to next frame
            if (KeyboardBinding.Triggered(UserActions.PlaybackNextFrame))
            {
                var rounded = Math.Round(playback.TimeInSecs * editFrameRate) / editFrameRate;
                playback.TimeInSecs = rounded + frameDuration;
            }
        }

        // Play backwards with increasing speed
        if (KeyboardBinding.Triggered(UserActions.PlaybackBackwards))
        {
            Log.Debug("Backwards triggered with speed " + playback.PlaybackSpeed);
            if (playback.PlaybackSpeed >= 0)
            {
                playback.PlaybackSpeed = -1;
            }
            else if (playback.PlaybackSpeed > -16)
            {
                playback.PlaybackSpeed *= 2;
            }
        }

        // Play forward with increasing speed
        if (KeyboardBinding.Triggered(UserActions.PlaybackForward))
        {
            if (playback.PlaybackSpeed <= 0)
            {
                _lastPlaybackStartTime = playback.TimeInBars;
                playback.PlaybackSpeed = 1;
            }
            else if (playback.PlaybackSpeed < 16) // Bass can't play much faster anyways
            {
                playback.PlaybackSpeed *= 2;
            }
        }

        if (KeyboardBinding.Triggered(UserActions.PlaybackForwardHalfSpeed))
        {
            if (playback.PlaybackSpeed > 0 && playback.PlaybackSpeed < 1f)
                playback.PlaybackSpeed *= 0.5f;
            else
                playback.PlaybackSpeed = 0.5f;
        }

        // Stop as separate keyboard action 
        if (KeyboardBinding.Triggered(UserActions.PlaybackStop))
        {
            playback.PlaybackSpeed = 0;
            if (UserSettings.Config.ResetTimeAfterPlayback)
                playback.TimeInBars = _lastPlaybackStartTime;
        }

        if (KeyboardBinding.Triggered(UserActions.PlaybackToggle))
        {
            if (playback.PlaybackSpeed == 0)
            {
                playback.PlaybackSpeed = 1;
                _lastPlaybackStartTime = playback.TimeInBars;
            }
            else
            {
                playback.PlaybackSpeed = 0;
                if (UserSettings.Config.ResetTimeAfterPlayback)
                    playback.TimeInBars = _lastPlaybackStartTime;
            }
        }

        if (KeyboardBinding.Triggered(UserActions.SetStartTime))
        {
            Playback.Current.IsLooping = true;
            Playback.Current.LoopRange.Start = (float)Playback.Current.TimeInBars;
            if(Playback.Current.LoopRange.End < Playback.Current.LoopRange.Start)
                Playback.Current.LoopRange.End = Playback.Current.LoopRange.Start + 4;
        }

        if (KeyboardBinding.Triggered(UserActions.SetEndTime))
        {
            Playback.Current.IsLooping = true;
            Playback.Current.LoopRange.End = (float)Playback.Current.TimeInBars;
            if(Playback.Current.LoopRange.Start > Playback.Current.LoopRange.End)
                Playback.Current.LoopRange.Start = Playback.Current.LoopRange.End - 4;
        }

        if (KeyboardBinding.Triggered(UserActions.PlaybackJumpToNextKeyframe))
            UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToNextKeyframe);

        if (KeyboardBinding.Triggered(UserActions.PlaybackJumpToPreviousKeyframe))
            UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToPreviousKeyframe);


    }

    internal static void DrawTimeControls(TimeLineCanvas timeLineCanvas, Instance composition)
    {
        var playback = Playback.Current; // TODO, this should be non-static eventually

        // Settings
        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var compositionWithSettings, out var settings);
        var opHasSettings = compositionWithSettings == composition;

        if (CustomComponents.IconButton(Icon.Settings, ControlSize, opHasSettings
                                                                        ? CustomComponents.ButtonStates.Normal
                                                                        : CustomComponents.ButtonStates.Dimmed))
        {
            //playback.TimeInBars = playback.LoopRange.Start;
            ImGui.OpenPopup(PlaybackSettingsPopup.PlaybackSettingsPopupId);
        }

        PlaybackSettingsPopup.DrawPlaybackSettings(composition);

        CustomComponents.TooltipForLastItem("Timeline Settings",
                                            "Switch between soundtrack and VJ modes. Control BPM and other inputs.");

        ImGui.SameLine();

        // Current Time
        var delta = 0.0;
        string formattedTime = "";
        switch (UserSettings.Config.TimeDisplayMode)
        {
            case TimeFormat.TimeDisplayModes.Bars:
                formattedTime = TimeFormat.FormatTimeInBars(playback.TimeInBars, 0);
                break;

            case TimeFormat.TimeDisplayModes.Secs:
                formattedTime = TimeSpan.FromSeconds(playback.TimeInSecs).ToString(@"hh\:mm\:ss\:ff");
                break;

            case TimeFormat.TimeDisplayModes.F30:
                var frames = playback.TimeInSecs * 30;
                formattedTime = $"{frames:0}f ";
                break;
            case TimeFormat.TimeDisplayModes.F60:
                var frames60 = playback.TimeInSecs * 60;
                formattedTime = $"{frames60:0}f ";
                break;
        }

        if (CustomComponents.JogDial(formattedTime, ref delta, new Vector2(100, ControlSize.Y)))
        {
            playback.PlaybackSpeed = 0;
            playback.TimeInBars += delta;
            if (UserSettings.Config.TimeDisplayMode == TimeFormat.TimeDisplayModes.F30)
            {
                playback.TimeInSecs = Math.Floor(playback.TimeInSecs * 30) / 30;
            }
        }

        if(ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem($"Current playtime at {settings.Bpm:0.0} BPM.", "Click mode button to toggle between timeline formats.");

        ImGui.SameLine();

        // Time Mode with context menu
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        if (ImGui.Button(UserSettings.Config.TimeDisplayMode.ToString(), ControlSize))
        {
            UserSettings.Config.TimeDisplayMode =
                (TimeFormat.TimeDisplayModes)(((int)UserSettings.Config.TimeDisplayMode + 1) % Enum.GetNames(typeof(TimeFormat.TimeDisplayModes)).Length);
        }

        ImGui.PopStyleColor();

        CustomComponents.TooltipForLastItem("Timeline format",
                                            "Click to toggle through BPM, Frames and Normal time modes");

        ImGui.SameLine();

        // Idle motion 
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UserSettings.Config.EnableIdleMotion
                                                    ? UiColors.TextDisabled
                                                    : new Vector4(0, 0, 0, 0.5f));

            if (CustomComponents.IconButton(Icon.BeatGrid, ControlSize))
            {
                UserSettings.Config.EnableIdleMotion = !UserSettings.Config.EnableIdleMotion;
            }

            CustomComponents.TooltipForLastItem("Idle Motion - Keeps beat time running",
                                                "This will keep updating the output [Time]\nwhich is useful for procedural animation and syncing.");

            if (UserSettings.Config.EnableIdleMotion)
            {
                var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                var beat = (int)(playback.FxTimeInBars * 4) % 4;
                var beatPulse = (playback.FxTimeInBars * 4) % 4 - beat;
                var bar = (int)(playback.FxTimeInBars) % 4;

                if (beat < 0)
                    beat = 4 + beat;

                if (bar < 0)
                    bar = 4 + bar;

                const int gridSize = 4;
                var drawList = ImGui.GetWindowDrawList();
                var min = center - new Vector2(7, 7) * T3Ui.UiScaleFactor + new Vector2(beat * gridSize, bar * gridSize) * T3Ui.UiScaleFactor;

                drawList.AddRectFilled(min, min + new Vector2(gridSize - 1, gridSize - 1),
                                       Color.Mix(UiColors.StatusAnimated,
                                                 UiColors.Gray,
                                                 (float)beatPulse));
            }

            ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        // MidiIndicator
        {
            var timeSinceLastEvent = Playback.RunTimeInSecs - Math.Max(T3Ui.MidiDataRecording.LastEventTime, T3Ui.OscDataRecording.LastEventTime);
            var flashFactor = MathF.Pow((float)timeSinceLastEvent.Clamp(0, 1) / 1, 0.5f);
            var color = Color.Mix(UiColors.StatusAnimated, UiColors.BackgroundFull.Fade(0.3f), flashFactor);
            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
            if (CustomComponents.IconButton(Icon.IO, ControlSize))
            {
                //T3Ui.MidiStreamRecorder.Reset();
                //DataRecording.ActiveRecordingSet.WriteToFile();
                WindowManager.ToggleInstanceVisibility<IoViewWindow>();
            }

            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (timeSinceLastEvent < 10)
                {
                    ImGui.BeginChild("canvas", new Vector2(400, 250));

                    //DataSetOutputUi.DrawDataSet(dataSet);
                    _dataSetView.Draw(DataRecording.ActiveRecordingSet);
                    ImGui.EndChild();
                }
                else
                {
                    ImGui.Text("Midi and OSC input indicator\nClick to open IO window.");
                }

                ImGui.EndTooltip();
            }

            ImGui.SameLine();
        }

        if (settings.Syncing == PlaybackSettings.SyncModes.Tapping)
        {
            var bpm = BeatTiming.Bpm;
            if (SingleValueEdit.Draw(ref bpm, new Vector2(100, ControlSize.Y) * T3Ui.UiScaleFactor, 1, 360, true, 0.01f, "{0:0.00 BPM}") ==
                InputEditStateFlags.Modified)
            {
                BeatTiming.SetBpmRate(bpm);
            }

            ImGui.SameLine();

            ImGui.Button("Sync", ControlSize);
            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        var roundedBpm = Math.Round(BeatTiming.Bpm*2)/2;
                        BeatTiming.SetBpmRate((float)roundedBpm);
                    }
                    else
                    {
                        BeatTiming.TriggerSyncTap();
                    }
                }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    BeatTiming.TriggerResyncMeasure();
                }
            }

            CustomComponents.TooltipForLastItem("Click on beat to sync. Tap later once to refine. Click right to sync measure.\n"
                                                + "Ctrl+Click to round BPM",
                                                $"Tap: {KeyboardBinding.ListKeyboardShortcuts(UserActions.TapBeatSync)}\n"
                                                + $"Resync: {KeyboardBinding.ListKeyboardShortcuts(UserActions.TapBeatSyncMeasure)}");

            ImGui.SameLine();

            // ImGui.PushButtonRepeat(true);
            // {
            //     if (CustomComponents.IconButton(Icon.ChevronLeft, ControlSize))
            //     {
            //         BeatTiming.TriggerDelaySync();
            //     }
            //
            //     ImGui.SameLine();
            //
            //     if (CustomComponents.IconButton(Icon.ChevronRight, ControlSize))
            //     {
            //         BeatTiming.TriggerAdvanceSync();
            //     }
            //
            //     ImGui.SameLine();
            // }
            // ImGui.PopButtonRepeat();
        }
        else
        {
            // Jump to start
            if (CustomComponents.IconButton(Icon.JumpToRangeStart,
                                            ControlSize,
                                            playback.TimeInBars != playback.LoopRange.Start
                                                ? CustomComponents.ButtonStates.Dimmed
                                                : CustomComponents.ButtonStates.Disabled
                                           )
                || KeyboardBinding.Triggered(UserActions.PlaybackJumpToStartTime)
               )
            {
                playback.TimeInBars = playback.IsLooping ? playback.LoopRange.Start : 0;
            }

            CustomComponents.TooltipForLastItem("Jump to beginning",
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackJumpToStartTime));

            ImGui.SameLine();

            // Prev Keyframe
            if (CustomComponents.IconButton(Icon.JumpToPreviousKeyframe,
                                            ControlSize,
                                            FrameStats.Last.HasKeyframesBeforeCurrentTime
                                                ? CustomComponents.ButtonStates.Dimmed
                                                : CustomComponents.ButtonStates.Disabled)
               )
            {
                UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToPreviousKeyframe);
            }

            CustomComponents.TooltipForLastItem("Jump to previous keyframe",
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackJumpToPreviousKeyframe));

            ImGui.SameLine();

            // Play backwards
            if (CustomComponents.IconButton(Icon.PlayBackwards,
                                            ControlSize,
                                            playback.PlaybackSpeed < 0
                                                ? CustomComponents.ButtonStates.Activated
                                                : CustomComponents.ButtonStates.Dimmed
                                           ))
            {
                if (playback.PlaybackSpeed != 0)
                {
                    playback.PlaybackSpeed = 0;
                }
                else if (playback.PlaybackSpeed == 0)
                {
                    playback.PlaybackSpeed = -1;
                }
            }

            if (playback.PlaybackSpeed < -1)
            {
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + new Vector2(20, 4), UiColors.ForegroundFull, $"×{-playback.PlaybackSpeed:0}");
            }

            CustomComponents.TooltipForLastItem("Play backwards",
                                                "Play backwards (and faster): " +
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackBackwards, false) +
                                                "\nPrevious frame:" + KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackPreviousFrame, false));

            ImGui.SameLine();

            // Play forward
            if (CustomComponents.IconButton(Icon.PlayForwards,
                                            ControlSize,
                                            playback.PlaybackSpeed > 0
                                                ? CustomComponents.ButtonStates.Activated
                                                : CustomComponents.ButtonStates.Dimmed
                                           ))
            {
                if (Math.Abs(playback.PlaybackSpeed) > 0.001f)
                {
                    playback.PlaybackSpeed = 0;
                }
                else if (Math.Abs(playback.PlaybackSpeed) < 0.001f)
                {
                    playback.PlaybackSpeed = 1;
                }
            }

            if (playback.PlaybackSpeed > 1)
            {
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + new Vector2(20, 4), UiColors.ForegroundFull, $"×{playback.PlaybackSpeed:0}");
            }

            CustomComponents.TooltipForLastItem("Start playback",
                                                "Play forward (and faster): " +
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackForward, false) +
                                                "\nPlay half speed (and slower): " +
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackForwardHalfSpeed, false) +
                                                "\nNext frame:" + KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackNextFrame, false));

            ImGui.SameLine();

            // Next Keyframe
            if (CustomComponents.IconButton(Icon.JumpToNextKeyframe,
                                            ControlSize,
                                            FrameStats.Last.HasKeyframesAfterCurrentTime
                                                ? CustomComponents.ButtonStates.Dimmed
                                                : CustomComponents.ButtonStates.Disabled)
               )
            {
                UserActionRegistry.DeferredActions.Add(UserActions.PlaybackJumpToNextKeyframe);
            }

            CustomComponents.TooltipForLastItem("Jump to next keyframe",
                                                KeyboardBinding.ListKeyboardShortcuts(UserActions.PlaybackJumpToNextKeyframe));
            ImGui.SameLine();

            // // End
            // Loop
            if (CustomComponents.IconButton(Icon.Loop,
                                            ControlSize,
                                            playback.IsLooping
                                                ? CustomComponents.ButtonStates.Activated
                                                : CustomComponents.ButtonStates.Dimmed))
            {
                playback.IsLooping = !playback.IsLooping;
                var loopRangeMatchesTime = playback.LoopRange.IsValid && playback.LoopRange.Contains(playback.TimeInBars);
                if (playback.IsLooping && !loopRangeMatchesTime)
                {
                    playback.LoopRange.Start = (float)(playback.TimeInBars - playback.TimeInBars % 4);
                    playback.LoopRange.Duration = 4;
                }
            }

            CustomComponents.TooltipForLastItem("Loop playback", "This will initialize one bar around current time.");

            ImGui.SameLine();

            // Curve Mode
            var hasKeyframes = FrameStats.Current.HasKeyframesAfterCurrentTime || FrameStats.Current.HasKeyframesAfterCurrentTime;
            ImGui.PushStyleColor(ImGuiCol.Text, hasKeyframes ? UiColors.Text.Rgba : UiColors.TextMuted);
            if (ImGui.Button(timeLineCanvas.Mode.ToString(), DopeCurve)) //
            {
                timeLineCanvas.Mode = (TimeLineCanvas.Modes)(((int)timeLineCanvas.Mode + 1) % Enum.GetNames(typeof(TimeLineCanvas.Modes)).Length);
            }

            ImGui.PopStyleColor();

            CustomComponents.TooltipForLastItem("Toggle keyframe view between Dope sheet and Curve mode.");

            ImGui.SameLine();
        }

        // ToggleAudio
        if (CustomComponents.IconButton(UserSettings.Config.AudioMuted ? Icon.ToggleAudioOff : Icon.ToggleAudioOn,
                                        ControlSize,
                                        UserSettings.Config.AudioMuted
                                            ? CustomComponents.ButtonStates.Dimmed
                                            : CustomComponents.ButtonStates.Normal
                                       ))
        {
            UserSettings.Config.AudioMuted = !UserSettings.Config.AudioMuted;
            AudioEngine.SetMute(UserSettings.Config.AudioMuted);
        }

        // ToggleHover
        {
            ImGui.SameLine();
            Icon icon;
            string hoverModeTooltip;
            string hoverModeAdditionalTooltip = null;
            CustomComponents.ButtonStates state = CustomComponents.ButtonStates.Normal;
            switch (UserSettings.Config.HoverMode)
            {
                case UserSettings.GraphHoverModes.Disabled:
                    state = CustomComponents.ButtonStates.Dimmed;
                    icon = Icon.HoverPreviewDisabled;
                    hoverModeTooltip = "No preview images on hover";
                    break;
                case UserSettings.GraphHoverModes.Live:
                    icon = Icon.HoverPreviewPlay;
                    hoverModeTooltip = "Live Hover Preview - Render explicit thumbnail image.";
                    hoverModeAdditionalTooltip = "This can interfere with the rendering of the current output.";
                    break;
                default:
                    icon = Icon.HoverPreviewSmall;
                    hoverModeTooltip = "Last - Show the current state of the operator.";
                    hoverModeAdditionalTooltip = "This can be outdated if operator is not require for current output.";
                    break;
            }

            if (CustomComponents.IconButton(icon, ControlSize, state))
            {
                UserSettings.Config.HoverMode =
                    (UserSettings.GraphHoverModes)(((int)UserSettings.Config.HoverMode + 1) % Enum.GetNames(typeof(UserSettings.GraphHoverModes)).Length);
            }

            CustomComponents.TooltipForLastItem(hoverModeTooltip, hoverModeAdditionalTooltip);
        }

        if (FrameStats.Last.HasAnimatedParameters)
        {
            // Lock all animated parameters
            ImGui.SameLine();
            var state = UserSettings.Config.AutoPinAllAnimations
                            ? CustomComponents.ButtonStates.Activated
                            : CustomComponents.ButtonStates.Dimmed;

            if (CustomComponents.IconButton(Icon.PinParams, ControlSize, state, KeyboardBinding.Triggered(UserActions.ToggleAnimationPinning)))
            {
                UserSettings.Config.AutoPinAllAnimations = !UserSettings.Config.AutoPinAllAnimations;

                if (!UserSettings.Config.AutoPinAllAnimations)
                {
                    timeLineCanvas.DopeSheetArea.PinnedParameters.Clear();
                }
            }
        }

        CustomComponents.TooltipForLastItem("Keep animated parameters visible",
                                            "This can be useful when align animations between multiple operators. Toggle again to clear the visible animations.\n\n"
                                            + KeyboardBinding.ListKeyboardShortcuts(UserActions.ToggleAnimationPinning)
                                           );
        ImGui.SameLine();
    }

    public static double _lastPlaybackStartTime;

    public static Vector2 ControlSize => new Vector2(45, 28) * T3Ui.UiScaleFactor;
    public static Vector2 DopeCurve => new Vector2(95, 28) * T3Ui.UiScaleFactor;

    private static readonly DataSetViewCanvas _dataSetView = new()
                                                                 {
                                                                     ShowInteraction = false,
                                                                     MaxTreeLevel = 0,
                                                                 };
}