using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.Vector;
using T3.Core.IO;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
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

        if (UserActions.TapBeatSync.Triggered())
            BeatTiming.TriggerSyncTap();

        if (UserActions.TapBeatSyncMeasure.Triggered())
            BeatTiming.TriggerResyncMeasure();

        if (UserActions.PlaybackJumpToPreviousKeyframe.Triggered())
            UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);

        if (UserActions.PlaybackJumpToStartTime.Triggered())
            playback.TimeInBars = playback.IsLooping ? playback.LoopRange.Start : 0;

        if (UserActions.PlaybackJumpToPreviousKeyframe.Triggered())
            UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);

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
            if (UserActions.PlaybackPreviousFrame.Triggered())
            {
                var rounded = Math.Round(playback.TimeInSecs * editFrameRate) / editFrameRate;
                playback.TimeInSecs = rounded - frameDuration;
            }

            if (UserActions.PlaybackJumpBack.Triggered())
            {
                playback.TimeInBars -= 1;
            }

            // Step to next frame
            if (UserActions.PlaybackNextFrame.Triggered())
            {
                var rounded = Math.Round(playback.TimeInSecs * editFrameRate) / editFrameRate;
                playback.TimeInSecs = rounded + frameDuration;
            }
        }

        // Play backwards with increasing speed
        if (UserActions.PlaybackBackwards.Triggered())
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
        if (UserActions.PlaybackForward.Triggered())
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

        if (UserActions.PlaybackForwardHalfSpeed.Triggered())
        {
            if (playback.PlaybackSpeed is > 0 and < 1f)
                playback.PlaybackSpeed *= 0.5f;
            else
                playback.PlaybackSpeed = 0.5f;
        }

        // Stop as separate keyboard action 
        if (UserActions.PlaybackStop.Triggered())
        {
            playback.PlaybackSpeed = 0;
            if (UserSettings.Config.ResetTimeAfterPlayback)
                playback.TimeInBars = _lastPlaybackStartTime;
        }

        if (UserActions.PlaybackToggle.Triggered())
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

        if (UserActions.SetStartTime.Triggered())
        {
            Playback.Current.IsLooping = true;
            Playback.Current.LoopRange.Start = (float)Playback.Current.TimeInBars;
            if(Playback.Current.LoopRange.End < Playback.Current.LoopRange.Start)
                Playback.Current.LoopRange.End = Playback.Current.LoopRange.Start + 4;
        }

        if (UserActions.SetEndTime.Triggered())
        {
            Playback.Current.IsLooping = true;
            Playback.Current.LoopRange.End = (float)Playback.Current.TimeInBars;
            if(Playback.Current.LoopRange.Start > Playback.Current.LoopRange.End)
                Playback.Current.LoopRange.Start = Playback.Current.LoopRange.End - 4;
        }

        if (UserActions.PlaybackJumpToNextKeyframe.Triggered())
            UserActionRegistry.QueueAction(UserActions.PlaybackJumpToNextKeyframe);

        if (UserActions.PlaybackJumpToPreviousKeyframe.Triggered())
            UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);


    }

    internal static void DrawTimeControls(TimeLineCanvas timeLineCanvas, Instance composition)
    {
        var playback = Playback.Current;

        // Settings
        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var compositionWithSettings, out var settings);
        var opHasSettings = compositionWithSettings == composition;

        
        // if (CustomComponents.IconButton(ProjectSettings.Config.AudioMuted ? Icon.ToggleAudioOff : Icon.ToggleAudioOn,
        //                                 ControlSize,
        //                                 ProjectSettings.Config.AudioMuted
        //                                     ? CustomComponents.ButtonStates.NeedsAttention
        //                                     : CustomComponents.ButtonStates.Dimmed
        //                                ))
        // {
        //     ProjectSettings.Config.AudioMuted = !ProjectSettings.Config.AudioMuted;
        //     AudioEngine.SetMute(ProjectSettings.Config.AudioMuted);
        // }
        
        if (CustomComponents.IconButton(Icon.Settings, ControlSize, opHasSettings
                                                                        ? CustomComponents.ButtonStates.Normal
                                                                        : CustomComponents.ButtonStates.Dimmed))
        {
            //playback.TimeInBars = playback.LoopRange.Start;
            ImGui.OpenPopup(PlaybackSettingsPopup.PlaybackSettingsPopupId);
        }

        if (PlaybackSettingsPopup.DrawPlaybackSettings(composition))
        {
            composition.Symbol.GetSymbolUi().FlagAsModified();
        }

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
                var ts = TimeSpan.FromSeconds(playback.TimeInSecs);

                formattedTime = ts.Hours > 0
                                           ? ts.ToString(@"hh\:mm\:ss\:ff")
                                           : ts.ToString(@"mm\:ss\:ff");
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

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        if (CustomComponents.JogDial(formattedTime, ref delta, new Vector2(StandardWidth, ControlSize.Y)))
        {
            playback.PlaybackSpeed = 0;
            playback.TimeInBars += delta;
            if (UserSettings.Config.TimeDisplayMode == TimeFormat.TimeDisplayModes.F30)
            {
                playback.TimeInSecs = Math.Floor(playback.TimeInSecs * 30) / 30;
            }
        }
        ImGui.PopStyleColor();

        if(ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem($"Current playtime at {settings.Bpm:0.0} BPM.", "Click mode button to toggle between timeline formats.");

        ImGui.SameLine();

        // Time Mode with context menu
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        if (ImGui.Button(UserSettings.Config.TimeDisplayMode.ToString(), ControlSize * new Vector2(1.5f,1)))
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

            // Create invisible button with same size as icon buttons would have
            
            if (ImGui.Button("##idleMotionToggle", ControlSize))
            {
                UserSettings.Config.EnableIdleMotion = !UserSettings.Config.EnableIdleMotion;
            }

            // Tooltip (same as before)
            CustomComponents.TooltipForLastItem("Idle Motion - Keeps beat time running",
                                                "This will keep updating the output [Time]\nwhich is useful for procedural animation and syncing.");

            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) * 0.5f;

            // Draw beat grid background
            const int cellCount = 4;
            var cellSize = CellSize;
            var gridOffset = center - GridSize;

            // Pre-calculate the cell size minus 1 for the rect size
            var rectSize = new Vector2(cellSize - 1, cellSize - 1);

            var gridColor = UserSettings.Config.EnableIdleMotion ? UiColors.BackgroundFull.Fade(0.2f) : UiColors.ForegroundFull.Fade(0.1f);
            for (int x = 0; x < cellCount; x++)
            {
                for (int y = 0; y < cellCount; y++)
                {
                    var cellMin = gridOffset + new Vector2(x * cellSize, y * cellSize);
                    var cellMax = cellMin + rectSize;
                    drawList.AddRectFilled(cellMin, cellMax, gridColor);
                }
            }

            if (!UserSettings.Config.EnableIdleMotion)
            {
                var diagonal = new Vector2(CellSize, -CellSize) * 2.3f;
                var lineStart = center + diagonal;
                var lineEnd = center - diagonal;
                drawList.AddLine(lineStart,
                                 lineEnd, 
                                 UiColors.BackgroundPopup.Fade(0.4f),5);
                drawList.AddLine(lineStart,
                                 lineEnd, 
                                 UiColors.ForegroundFull.Fade(0.1f));
                
            }

            // If idle motion is enabled, draw the animated indicator
            if (UserSettings.Config.EnableIdleMotion)
            {
                var beat = (int)(playback.FxTimeInBars * 4) % 4;
                var beatPulse = (playback.FxTimeInBars * 4) % 4 - beat;
                var bar = (int)(playback.FxTimeInBars) % 4;

                if (beat < 0)
                    beat = 4 + beat;

                if (bar < 0)
                    bar = 4 + bar;

                var indicatorMin = gridOffset + new Vector2(beat * cellSize, bar * cellSize);
                var indicatorMax = indicatorMin + new Vector2(cellSize - 1, cellSize - 1);

                drawList.AddRectFilled(indicatorMin, indicatorMax,
                                       Color.Mix(UiColors.StatusAnimated,
                                                 UiColors.BackgroundFull.Fade(0.3f),
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
            if (SingleValueEdit.Draw(ref bpm, new Vector2(StandardWidth, ControlSize.Y), 1, 360, true, 0.01f, "{0:0.0 BPM}") ==
                InputEditStateFlags.Modified)
            {
                composition.Symbol.GetSymbolUi().FlagAsModified();
            }

            ImGui.SameLine();

            ImGui.Button("Sync", SyncButton);
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
                                                $"Tap: {UserActions.TapBeatSync.ListShortcuts()}\n"
                                                + $"Resync: {UserActions.TapBeatSyncMeasure.ListShortcuts()}");

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
                || UserActions.PlaybackJumpToStartTime.Triggered()
               )
            {
                playback.TimeInBars = playback.IsLooping ? playback.LoopRange.Start : 0;
            }

            CustomComponents.TooltipForLastItem("Jump to beginning",
                                                UserActions.PlaybackJumpToStartTime.ListShortcuts());

            ImGui.SameLine();

            // Prev Keyframe
            if (CustomComponents.IconButton(Icon.JumpToPreviousKeyframe,
                                            ControlSize,
                                            FrameStats.Last.HasKeyframesBeforeCurrentTime
                                                ? CustomComponents.ButtonStates.Dimmed
                                                : CustomComponents.ButtonStates.Disabled)
               )
            {
                UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);
            }

            CustomComponents.TooltipForLastItem("Jump to previous keyframe",
                                                UserActions.PlaybackJumpToPreviousKeyframe.ListShortcuts());

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
                                                UserActions.PlaybackBackwards.ListShortcuts() +
                                                "\nPrevious frame:" + UserActions.PlaybackPreviousFrame.ListShortcuts());

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
                                                UserActions.PlaybackForward.ListShortcuts() +
                                                "\nPlay half speed (and slower): " +
                                                UserActions.PlaybackForwardHalfSpeed.ListShortcuts() +
                                                "\nNext frame:" + UserActions.PlaybackNextFrame.ListShortcuts());

            ImGui.SameLine();

            // Next Keyframe
            if (CustomComponents.IconButton(Icon.JumpToNextKeyframe,
                                            ControlSize,
                                            FrameStats.Last.HasKeyframesAfterCurrentTime
                                                ? CustomComponents.ButtonStates.Dimmed
                                                : CustomComponents.ButtonStates.Disabled)
               )
            {
                UserActionRegistry.QueueAction(UserActions.PlaybackJumpToNextKeyframe);
            }

            CustomComponents.TooltipForLastItem("Jump to next keyframe",
                                                UserActions.PlaybackJumpToNextKeyframe.ListShortcuts());
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
        if (CustomComponents.IconButton(ProjectSettings.Config.AudioMuted ? Icon.ToggleAudioOff : Icon.ToggleAudioOn,
                                        ControlSize,
                                        ProjectSettings.Config.AudioMuted
                                            ? CustomComponents.ButtonStates.NeedsAttention
                                            : CustomComponents.ButtonStates.Dimmed
                                       ))
        {
            ProjectSettings.Config.AudioMuted = !ProjectSettings.Config.AudioMuted;
            AudioEngine.SetMute(ProjectSettings.Config.AudioMuted);
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

            if (CustomComponents.IconButton(Icon.PinParams, ControlSize, state, UserActions.ToggleAnimationPinning.Triggered()))
            {
                UserSettings.Config.AutoPinAllAnimations = !UserSettings.Config.AutoPinAllAnimations;

                if (!UserSettings.Config.AutoPinAllAnimations)
                {
                    timeLineCanvas.DopeSheetArea.PinnedParametersHashes.Clear();
                }
            }
        }

        CustomComponents.TooltipForLastItem("Keep animated parameters visible",
                                            "This can be useful when align animations between multiple operators. Toggle again to clear the visible animations.\n\n"
                                            + UserActions.ToggleAnimationPinning.ListShortcuts()
                                           );
        ImGui.SameLine();
    }

    private static double _lastPlaybackStartTime;
    // beat grid optimizations
    private static float _lastUiScaleFactor = -1f;
    private static float _cachedCellSize;
    private static Vector2 _cachedGridSize;

    private static float CellSize
    {
        get
        {
            if (!(Math.Abs(_lastUiScaleFactor - T3Ui.UiScaleFactor) > 0.001f)) 
                return _cachedCellSize;
            
            _lastUiScaleFactor = T3Ui.UiScaleFactor;
            _cachedCellSize = 4f * T3Ui.UiScaleFactor;
            _cachedGridSize = new Vector2(_cachedCellSize * 4 * 0.5f);
            return _cachedCellSize;
        }
    }

    private static Vector2 GridSize
    {
        get
        {
            // Trigger cache update if needed
            _ = CellSize;
            return _cachedGridSize;
        }
    }
    // end of beat grid optimizations

    private static float StandardWidth => 100f * T3Ui.UiScaleFactor;
    public static Vector2 ControlSize => new Vector2(35, 28) * T3Ui.UiScaleFactor;
    private static Vector2 DopeCurve => new Vector2(95, 28) * T3Ui.UiScaleFactor;
    private static Vector2 SyncButton => new Vector2(45, 28) * T3Ui.UiScaleFactor;

    private static readonly DataSetViewCanvas _dataSetView = new()
                                                                 {
                                                                     ShowInteraction = false,
                                                                     MaxTreeLevel = 0,
                                                                 };
}