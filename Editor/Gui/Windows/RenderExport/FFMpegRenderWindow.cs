#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using System.IO;
using T3.Editor.Gui.Interaction.Timing;
using T3.Core.SystemUi;
using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class FFMpegRenderWindow : Window
{
    public FFMpegRenderWindow()
    {
        Config.Title = "FFMpeg Render";
    }

    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);

        if (!FFMpegInstallation.IsInstalled())
        {
            FFMpegInstallation.DrawInstallUi();
            return;
        }

        DrawTimeSetup();
        ImGui.Indent(1 * T3Ui.UiScaleFactor);
        DrawInnerContent();
        ImGui.Unindent(1 * T3Ui.UiScaleFactor);
    }


    private void DrawInnerContent()
    {
        if (FFMpegRenderProcess.State == FFMpegRenderProcess.States.NoOutputWindow)
        {
            _lastHelpString = "No output view available";
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        if (FFMpegRenderProcess.State == FFMpegRenderProcess.States.NoValidOutputType)
        {
            _lastHelpString = FFMpegRenderProcess.MainOutputType == null
                                  ? "The output view is empty"
                                  : "Select or pin a Symbol with Texture2D output in order to render to file";
            FormInputs.AddVerticalSpace(5);
            ImGui.Separator();
            FormInputs.AddVerticalSpace(5);
            ImGui.BeginDisabled();
            ImGui.Button("Start Render");
            CustomComponents.TooltipForLastItem("Only Symbols with a texture2D output can be rendered to file");
            ImGui.EndDisabled();
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        _lastHelpString = "Ready to render";

        FormInputs.SetIndentToParameters();
        FormInputs.AddSegmentedButtonWithLabel(ref FFMpegRenderSettings.RenderMode, "Output");

        if (FFMpegRenderSettings.RenderMode == RenderSettings.RenderModes.Video)
            DrawVideoSettings();
        else
            DrawImageSequenceSettings();

        DrawSummary();
        DrawRenderingControls();
    }

    private static void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range
        FormInputs.AddSegmentedButtonWithLabel(ref FFMpegRenderSettings.TimeRange, "Range");

        // ApplyTimeRange replacement:
        switch (FFMpegRenderSettings.TimeRange)
        {
            case FFMpegRenderSettings.TimeRanges.Loop:
                if (Core.Animation.Playback.Current.IsLooping)
                {
                    var playback = Core.Animation.Playback.Current;
                    var startInSeconds = playback.SecondsFromBars(playback.LoopRange.Start);
                    var endInSeconds = playback.SecondsFromBars(playback.LoopRange.End);

                    FFMpegRenderSettings.StartInBars = (float)RenderTiming.SecondsToReferenceTime(startInSeconds, (RenderSettings.TimeReference)FFMpegRenderSettings.Reference, FFMpegRenderSettings.Fps);
                    FFMpegRenderSettings.EndInBars = (float)RenderTiming.SecondsToReferenceTime(endInSeconds, (RenderSettings.TimeReference)FFMpegRenderSettings.Reference, FFMpegRenderSettings.Fps);
                }
                break;

            case FFMpegRenderSettings.TimeRanges.Soundtrack:
                if (PlaybackUtils.TryFindingSoundtrack(out var handle, out _))
                {
                    var playback = Core.Animation.Playback.Current;
                    var clip = handle.Clip;
                    var startTime = (float)RenderTiming.SecondsToReferenceTime(playback.SecondsFromBars(clip.StartTime), (RenderSettings.TimeReference)FFMpegRenderSettings.Reference, FFMpegRenderSettings.Fps);
                    var endTime = (float)RenderTiming.SecondsToReferenceTime(clip.EndTime > 0
                                                                                ? playback.SecondsFromBars(clip.EndTime)
                                                                                : clip.LengthInSeconds,
                                                                             (RenderSettings.TimeReference)FFMpegRenderSettings.Reference,
                                                                             FFMpegRenderSettings.Fps);

                    FFMpegRenderSettings.StartInBars = startTime;
                    FFMpegRenderSettings.EndInBars = endTime;
                }
                break;
        }

        FormInputs.AddVerticalSpace();

        // Reference switch converts values
        var oldRef = FFMpegRenderSettings.Reference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref FFMpegRenderSettings.Reference, "Scale"))
        {
            FFMpegRenderSettings.StartInBars = (float)RenderTiming.ConvertReferenceTime(FFMpegRenderSettings.StartInBars,
                                                                                       (RenderSettings.TimeReference)oldRef,
                                                                                       (RenderSettings.TimeReference)FFMpegRenderSettings.Reference,
                                                                                       FFMpegRenderSettings.Fps);
            FFMpegRenderSettings.EndInBars = (float)RenderTiming.ConvertReferenceTime(FFMpegRenderSettings.EndInBars,
                                                                                       (RenderSettings.TimeReference)oldRef,
                                                                                       (RenderSettings.TimeReference)FFMpegRenderSettings.Reference,
                                                                                       FFMpegRenderSettings.Fps);
        }

        var changed = false;
        changed |= FormInputs.AddFloat($"Start ({FFMpegRenderSettings.Reference})", ref FFMpegRenderSettings.StartInBars);
        changed |= FormInputs.AddFloat($"End ({FFMpegRenderSettings.Reference})", ref FFMpegRenderSettings.EndInBars);

        if (changed)
            FFMpegRenderSettings.TimeRange = FFMpegRenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace();

        FormInputs.AddVerticalSpace();

        FormInputs.AddFloat("FPS", ref FFMpegRenderSettings.Fps, 0);
        if (FFMpegRenderSettings.Fps < 0) FFMpegRenderSettings.Fps = -FFMpegRenderSettings.Fps;

        // Resolution row
        FormInputs.DrawInputLabel(FFMpegRenderUiStrings.ResolutionLabel);
        var resSize = FormInputs.GetAvailableInputSize(null, false, true);
        DrawResolutionPopoverCompact(resSize.X);
        
        FormInputs.AddVerticalSpace(10);

        // Handle FPS change rescaling
        if (FFMpegRenderSettings.Fps != 0 && Math.Abs(_lastValidFps - FFMpegRenderSettings.Fps) > float.Epsilon)
        {
            FFMpegRenderSettings.StartInBars = (float)RenderTiming.ConvertFps(FFMpegRenderSettings.StartInBars, _lastValidFps, FFMpegRenderSettings.Fps);
            FFMpegRenderSettings.EndInBars = (float)RenderTiming.ConvertFps(FFMpegRenderSettings.EndInBars, _lastValidFps, FFMpegRenderSettings.Fps);
            _lastValidFps = FFMpegRenderSettings.Fps;
        }

        // Calculate Frame Count
        var start = RenderTiming.ReferenceTimeToSeconds(FFMpegRenderSettings.StartInBars, (RenderSettings.TimeReference)FFMpegRenderSettings.Reference, FFMpegRenderSettings.Fps);
        var end = RenderTiming.ReferenceTimeToSeconds(FFMpegRenderSettings.EndInBars, (RenderSettings.TimeReference)FFMpegRenderSettings.Reference, FFMpegRenderSettings.Fps);
        FFMpegRenderSettings.FrameCount = (int)Math.Round((end - start) * FFMpegRenderSettings.Fps);
    }

    private static void DrawVideoSettings()
    {
        var bitrateMbps = FFMpegRenderSettings.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat(FFMpegRenderUiStrings.BitrateLabel, ref bitrateMbps, 0.1f, 500f, 0.1f, false, false, FFMpegRenderUiStrings.BitrateTooltip))
        {
            FFMpegRenderSettings.Bitrate = (int)(bitrateMbps * 1_000_000);
        }

        var videoPath = UserSettings.Config.RenderVideoFilePath ?? FFMpegRenderUiStrings.DefaultVideoPath;
        string? directory = Path.GetDirectoryName(videoPath) ?? string.Empty;
        var filename = Path.GetFileName(videoPath) ?? FFMpegRenderUiStrings.DefaultVideoFilename;

        if (FormInputs.AddFilePicker(FFMpegRenderUiStrings.FolderLabel, ref directory, FFMpegRenderUiStrings.DefaultFolderPlaceholder, null, null, FileOperations.FilePickerTypes.Folder))
        {
            UserSettings.Config.RenderVideoFilePath = Path.Combine(directory!, filename!);
        }

        var nonNullFilename = filename ?? string.Empty;
        if (FormInputs.AddStringInput(FFMpegRenderUiStrings.FilenameLabel, ref nonNullFilename))
        {
            filename = nonNullFilename;
            UserSettings.Config.RenderVideoFilePath = Path.Combine(directory!, filename!);
        }

        FormInputs.AddCheckBox(FFMpegRenderUiStrings.AutoIncrementLabel, ref FFMpegRenderSettings.AutoIncrementVideo);
        FormInputs.AddCheckBox(FFMpegRenderUiStrings.ExportAudioLabel, ref FFMpegRenderSettings.ExportAudio);
        FormInputs.AddCheckBox(FFMpegRenderUiStrings.AdvancedSettingsLabel, ref FFMpegRenderSettings.ShowAdvancedSettings);

        if (FFMpegRenderSettings.ShowAdvancedSettings)
        {
            FormInputs.DrawFieldSetHeader("Codec Settings");
            FormInputs.AddVerticalSpace(5);
            FormInputs.AddEnumDropdown(ref FFMpegRenderSettings.Codec, "Codec");

            if (FFMpegRenderSettings.Codec == FFMpegRenderSettings.SelectedCodec.OpenH264 ||
                FFMpegRenderSettings.Codec == FFMpegRenderSettings.SelectedCodec.Vp9)
            {
                FormInputs.AddVerticalSpace(10);
                FormInputs.AddInt("CRF Quality", ref FFMpegRenderSettings.CrfQuality, 0, 51, 1, "Lower is better quality. 0 is lossless (for H.264), 23 is default.");
                FormInputs.AddVerticalSpace(5);
                FormInputs.AddEnumDropdown(ref FFMpegRenderSettings.Preset, "Preset");
                FormInputs.AddVerticalSpace(5);
            }
            ImGui.Unindent();
        }
    }
    
    private static void DrawImageSequenceSettings()
    {
        FormInputs.AddFilePicker("Main Folder",
                                 ref UserSettings.Config.RenderSequenceFilePath!, 
                                 ".\\ImageSequence ", 
                                 null, 
                                 "Folder to save the sequence", 
                                 FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Subfolder", ref UserSettings.Config.RenderSequenceFileName))
        {
            UserSettings.Config.RenderSequenceFileName = (UserSettings.Config.RenderSequenceFileName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequenceFileName)) UserSettings.Config.RenderSequenceFileName = "v01";
        }

        if (FormInputs.AddStringInput("Filename Prefix", ref UserSettings.Config.RenderSequencePrefix))
        {
            UserSettings.Config.RenderSequencePrefix = (UserSettings.Config.RenderSequencePrefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequencePrefix)) UserSettings.Config.RenderSequencePrefix = "img";
        }

        FormInputs.AddEnumDropdown(ref FFMpegRenderSettings.FileFormat, "Format");
        if (FFMpegRenderSettings.FileFormat == FFMpegRenderSettings.ImageFileFormats.WebP)
        {
            FormInputs.AddInt(FFMpegRenderUiStrings.WebpQualityLabel, ref FFMpegRenderSettings.WebpQuality, 0, 100, 1, FFMpegRenderUiStrings.WebpQualityTooltip);
            FormInputs.AddInt(FFMpegRenderUiStrings.WebpCompressionLabel, ref FFMpegRenderSettings.WebpCompressionLevel, 0, 6, 1, FFMpegRenderUiStrings.WebpCompressionTooltip);
        }

        FormInputs.AddCheckBox(FFMpegRenderUiStrings.CreateSubfolderLabel, ref FFMpegRenderSettings.CreateSubFolder);
        FormInputs.AddCheckBox(FFMpegRenderUiStrings.AutoIncrementSubfolderLabel, ref FFMpegRenderSettings.AutoIncrementSubFolder);
        
        if (FFMpegRenderSettings.AutoIncrementSubFolder)
        {
            var targetToIncrement = FFMpegRenderSettings.CreateSubFolder ? UserSettings.Config.RenderSequenceFileName : UserSettings.Config.RenderSequencePrefix;
            var hasVersion = RenderPaths.IsFilenameIncrementable(targetToIncrement);
            if (!hasVersion)
            {
                // No hint needed anymore as it's expected behavior
            }
        }
    }
    
    // Helper to resolve target path for UI
    private static string GetCachedTargetFilePath()
    {
         return RenderPaths.GetExpectedTargetDisplayPath(FFMpegRenderSettings.RenderMode);
    }

    private static void DrawResolutionPopoverCompact(float width)
    {
        var currentPct = (int)(FFMpegRenderSettings.ResolutionFactor * 100);
        ImGui.SetNextItemWidth(width);
        
        if (ImGui.Button($"{currentPct}%##Res", new System.Numerics.Vector2(width, 0)))
        {
            ImGui.OpenPopup("ResolutionPopover");
        }
        CustomComponents.TooltipForLastItem(FFMpegRenderUiStrings.ResolutionTooltip);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(8, 8));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(160 * T3Ui.UiScaleFactor, 0));
        
        if (ImGui.BeginPopup("ResolutionPopover", ImGuiWindowFlags.NoMove))
        {
            static void DrawSelectable(string label, float factor)
            {
                bool isSelected = Math.Abs(FFMpegRenderSettings.ResolutionFactor - factor) < 0.001f;
                if (ImGui.Selectable(label, isSelected))
                {
                    FFMpegRenderSettings.ResolutionFactor = factor;
                }
            }

            DrawSelectable("25%", 0.25f);
            DrawSelectable("50%", 0.5f);
            DrawSelectable("100%", 1.0f);
            DrawSelectable("200%", 2.0f);

            CustomComponents.SeparatorLine();
            
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(FFMpegRenderUiStrings.CustomResolutionLabel);
            ImGui.PopStyleColor();
            
            var customPct = FFMpegRenderSettings.ResolutionFactor * 100f;
            ImGui.SetNextItemWidth(100 * T3Ui.UiScaleFactor);
            if (ImGui.InputFloat("##CustomRes", ref customPct, 0, 0, "%.0f%%"))
            {
                customPct = Math.Clamp(customPct, 1f, 1000f);
                FFMpegRenderSettings.ResolutionFactor = customPct / 100f;
            }
            
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
    }

    private void DrawRenderingControls()
    {
        FormInputs.AddVerticalSpace(5);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(5);

        if (FFMpegRenderProcess.IsExporting)
        {
            DrawExportingControls();
        }
        else
        {
            DrawIdleControls();
        }

        _overwriteDialog.Draw(FFMpegRenderSettings);
    }
    

    private static void DrawExportingControls()
    {
        var progress = (float)FFMpegRenderProcess.Progress;
        var elapsed = Playback.RunTimeInSecs - FFMpegRenderProcess.ExportStartedTimeLocal;

        var timeRemainingStr = "Calculating...";
        if (progress > 0.01)
        {
            var estimatedTotal = elapsed / progress;
            var remaining = estimatedTotal - elapsed;
            timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(remaining) + " remaining";
        }

        var progressStr = $"{timeRemainingStr} ({FFMpegRenderProcess.FrameIndex}/{FFMpegRenderProcess.FrameCount})";

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UiColors.StatusAutomated.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
        ImGui.ProgressBar(progress, new Vector2(-1, 4 * T3Ui.UiScaleFactor), "");
        ImGui.PopStyleColor(2);

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.SetCursorPosX(20 * T3Ui.UiScaleFactor);
        ImGui.TextUnformatted(progressStr);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        FormInputs.AddVerticalSpace(5);
        if (ImGui.Button("Cancel Render", new Vector2(-1, 24 * T3Ui.UiScaleFactor)))
        {
            FFMpegRenderProcess.Cancel("Cancelled manually");
        }
    }

    private void DrawIdleControls()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Fade(0.8f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundActive.Fade(0.6f).Rgba);
        
        var startLabel = (FFMpegRenderSettings.RenderMode, FFMpegRenderSettings.IsAutoIncrementing) switch
                         {
                             (RenderSettings.RenderModes.Video, true)         => FFMpegRenderUiStrings.RenderVideoAutoLabel,
                             (RenderSettings.RenderModes.ImageSequence, true) => FFMpegRenderUiStrings.RenderSequenceAutoLabel,
                             _                                                      => FFMpegRenderUiStrings.StartRenderLabel
                         };

        if (ImGui.Button(startLabel, new Vector2(-1, 0)))
        {
            var targetPath = RenderPaths.GetTargetFilePath(FFMpegRenderSettings.RenderMode);
            
            // If AutoIncrement is ON, we skip overwrite check because we WILL generate a new name in Process
            if (FFMpegRenderSettings.IsAutoIncrementing)
            {
                FFMpegRenderProcess.TryStart(FFMpegRenderSettings);
            }
            else
            {
                _overwriteDialog.Show(targetPath);
            }
        }
        ImGui.PopStyleColor(3);

        if (!string.IsNullOrEmpty(FFMpegRenderProcess.LastOutputPath))
        {
            ImGui.SetCursorPosX(1 * T3Ui.UiScaleFactor);
            if (ImGui.Button("Open Folder"))
            {
                var folder = Path.GetDirectoryName(FFMpegRenderProcess.LastOutputPath);
                if (folder != null && Directory.Exists(folder))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(folder);
                }
            }
            ImGui.SameLine();
            ImGui.TextDisabled(Path.GetFileName(FFMpegRenderProcess.LastOutputPath));
            CustomComponents.TooltipForLastItem($"Open folder and select {Path.GetFileName(FFMpegRenderProcess.LastOutputPath)}");
        }
    }

    private void DrawSummary()
    {
        if (FFMpegRenderProcess.IsExporting)
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        var size = FFMpegRenderProcess.MainOutputRenderedSize;
        
        string format;
        if (FFMpegRenderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
             format = $"{FFMpegRenderSettings.Codec.ToString().ToUpper()} Video";
        }
        else
        {
             format = $"{FFMpegRenderSettings.FileFormat} Sequence";
        }

        ImGui.SetCursorPosX(20 * T3Ui.UiScaleFactor);
        ImGui.TextUnformatted($"{format} - {size.Width}x{size.Height} @ {FFMpegRenderSettings.Fps}fps");
        
        var duration = FFMpegRenderSettings.FrameCount / FFMpegRenderSettings.Fps;
        ImGui.SetCursorPosX(20 * T3Ui.UiScaleFactor);
        ImGui.TextUnformatted($"{duration:F2}s ({FFMpegRenderSettings.FrameCount} frames)");
        
        var targetPath = GetCachedTargetFilePath();
        if (!string.IsNullOrEmpty(targetPath))
        {
             // Pretty print path: replace %04d with [####]
             var prettyPath = targetPath.Replace("%04d", "[####]");
             ImGui.SetCursorPosX(20 * T3Ui.UiScaleFactor);
             ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X - 20 * T3Ui.UiScaleFactor);
             ImGui.TextWrapped($"-> {prettyPath}");
             ImGui.PopTextWrapPos();
        }
        
        ImGui.PopStyleColor();
    }

    internal override List<Window> GetInstances() => [];

    private static string _lastHelpString = string.Empty;
    private static float _lastValidFps = 60f;
    private static FFMpegRenderSettings FFMpegRenderSettings => FFMpegRenderSettings.Current;

    private readonly OverwriteDialog _overwriteDialog = new();

    private class OverwriteDialog
    {
        public void Show(string path)
        {
            if (RenderPaths.FileExists(path, FFMpegRenderSettings.RenderMode))
            {
                _targetPathForOverwrite = path;
                _shouldOpen = true;
            }
            else
            {
                FFMpegRenderProcess.TryStart(FFMpegRenderSettings);
            }
        }

        public void Draw(FFMpegRenderSettings settings)
        {
            if (_shouldOpen)
            {
                ImGui.OpenPopup(PopupId);
                _shouldOpen = false;
            }

            ImGui.SetNextWindowSize(new Vector2(600, 300));
            if (ImGui.BeginPopupModal(PopupId, ref _isOverwriteDialogNotUsed, ImGuiWindowFlags.NoResize))
            {
                var windowWidth = ImGui.GetContentRegionAvail().X;

                ImGui.PushFont(Fonts.FontLarge);
                var title = "Target already exists";
                var titleWidth = ImGui.CalcTextSize(title).X;
                ImGui.SetCursorPosX((windowWidth - titleWidth) * 0.5f);
                ImGui.TextUnformatted(title);
                ImGui.PopFont();

                FormInputs.AddVerticalSpace(10);

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                var msg = "The target output already exists:";
                var msgWidth = ImGui.CalcTextSize(msg).X;
                ImGui.SetCursorPosX((windowWidth - msgWidth) * 0.5f);
                ImGui.TextUnformatted(msg);

                FormInputs.AddVerticalSpace(5);

                ImGui.PushFont(Fonts.FontSmall);
                var pathSize = ImGui.CalcTextSize(_targetPathForOverwrite);
                if (pathSize.X < windowWidth - 40) // Allow some padding
                {
                    ImGui.SetCursorPosX((windowWidth - pathSize.X) * 0.5f);
                    ImGui.TextUnformatted(_targetPathForOverwrite);
                }
                else
                {
                    ImGui.TextWrapped(_targetPathForOverwrite);
                }

                ImGui.PopFont();
                ImGui.PopStyleColor();

                FormInputs.AddVerticalSpace(20);

                // Buttons
                var buttonWidth = 150f;
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var totalButtonWidth = buttonWidth * 2 + spacing;
                ImGui.SetCursorPosX((windowWidth - totalButtonWidth) * 0.5f);

                if (ImGui.Button("Overwrite", new Vector2(buttonWidth, 40)))
                {
                    FFMpegRenderProcess.TryStart(settings);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 40)))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private string _targetPathForOverwrite = string.Empty;
        private bool _shouldOpen;
        private bool _isOverwriteDialogNotUsed = true;
        private const string PopupId = "Overwrite?";
    }
    
    private static class FFMpegInstallation
    {
        public static bool IsInstalled() => FFMpegRenderProcess.IsFFMpegAvailable();

        public static void DrawInstallUi()
        {
            if (FFMpegRenderProcess.State == FFMpegRenderProcess.States.Downloading)
            {
                ImGui.Text("Downloading FFMpeg...");
                ImGui.ProgressBar(FFMpegRenderProcess.DownloadProgress, new Vector2(-1, 0));
            }
            else
            {
                ImGui.Text("FFMpeg is missing.");
                ImGui.Spacing();
                if (ImGui.Button("Download FFMpeg Suite"))
                {
                    FFMpegRenderProcess.InstallFFMpeg();
                }

                ImGui.TextDisabled("This will download ffmpeg.exe and ffprobe.exe using ffbinaries.");
            }
        }
    }

    private static class FFMpegRenderUiStrings
    {
        public const string BitrateLabel = "Bitrate (Mbps)";
        public const string BitrateTooltip = "Target bitrate for video encoding in Megabits per second.";
        public const string FolderLabel = "Folder";
        public const string FilenameLabel = "Filename";
        public const string DefaultFolderPlaceholder = ".\\Render\\";
        public const string DefaultVideoPath = ".\\Render\\output.mp4";
        public const string DefaultVideoFilename = "output.mp4";
        public const string AutoIncrementLabel = "Auto-Increment Version";
        public const string ExportAudioLabel = "Export Audio";
        public const string AdvancedSettingsLabel = "Advanced Settings";
        public const string WebpQualityLabel = "WebP Quality";
        public const string WebpQualityTooltip = "0 is fastest/lowest, 100 is slowest/best compression in lossless mode.";
        public const string WebpCompressionLabel = "Compression Level";
        public const string WebpCompressionTooltip = "0 is fastest, 6 is slowest/highest compression.";
        public const string CreateSubfolderLabel = "Create subfolder";
        public const string AutoIncrementSubfolderLabel = "Auto-increment version";
        public const string StartRenderLabel = "Start FFMpeg Render";
        public const string RenderVideoAutoLabel = "Render Video (Auto-Increment)";
        public const string RenderSequenceAutoLabel = "Render Sequence (Auto-Increment)";

        
        public const string ResolutionLabel = "Resolution";
        public const string CustomResolutionLabel = "Custom:";
        public const string ResolutionTooltip = "Scale resolution of rendered frames.";
    }
}
