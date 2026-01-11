#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Utils;
using T3.Core.Animation;
using T3.Core.SystemUi;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderWindow : Window
{
    public RenderWindow()
    {
        Config.Title = RenderWindowStrings.WindowTitle;
    }

    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);
        DrawTimeSetup();
        
        DrawInnerContent();
        
    }

    private void DrawInnerContent()
    {
        if (RenderProcess.State == RenderProcess.States.NoOutputWindow)
        {
            _uiState.LastHelpString = RenderWindowStrings.NoOutputView;
            CustomComponents.HelpText(_uiState.LastHelpString);
            return;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputType)
        {
            _uiState.LastHelpString = RenderProcess.MainOutputType == null
                                  ? RenderWindowStrings.OutputViewEmpty
                                  : RenderWindowStrings.SymbolMustHaveTextureOutput;
            ImGui.Button(RenderWindowStrings.StartRenderButton, new Vector2(-1, 0));
            CustomComponents.TooltipForLastItem(RenderWindowStrings.TooltipTextureOutputRequired);
            ImGui.EndDisabled();
            CustomComponents.HelpText(_uiState.LastHelpString);
            return;
        }

        _uiState.LastHelpString = RenderWindowStrings.ReadyToRender;

        FormInputs.AddVerticalSpace();
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.RenderMode, RenderWindowStrings.RenderModeLabel);

        FormInputs.AddVerticalSpace();

        if (RenderSettings.RenderMode == RenderSettings.RenderModes.Video)
            DrawVideoSettings(RenderProcess.MainOutputRenderedSize);
        else
            DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(2);
        
        // Final Summary Card
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        
        if (ImGui.BeginChild("Summary", new Vector2(-1, 64 * T3Ui.UiScaleFactor), false, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4);
            DrawRenderSummary();
        }
        ImGui.EndChild();
        
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        FormInputs.AddVerticalSpace(5);
        DrawRenderingControls();
        DrawOverwriteDialog();

        CustomComponents.HelpText(RenderProcess.IsExporting ? RenderProcess.LastHelpString : _uiState.LastHelpString);
        
        if (!RenderProcess.IsExporting && !string.IsNullOrEmpty(RenderProcess.LastTargetDirectory) && Directory.Exists(RenderProcess.LastTargetDirectory))
        {
            if (ImGui.Button(RenderWindowStrings.OpenOutputFolderButton))
            {
                CoreUi.Instance.OpenWithDefaultApplication(RenderProcess.LastTargetDirectory);
            }
        }
    }

    private void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range row
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.TimeRange, RenderWindowStrings.RangeLabel);
        RenderTiming.ApplyTimeRange(RenderSettings.TimeRange, RenderSettings);
        
        // Scale row (now under Range)
        var oldRef = RenderSettings.Reference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.Reference, RenderWindowStrings.ScaleLabel))
        {
            RenderSettings.StartInBars =
                (float)RenderTiming.ConvertReferenceTime(RenderSettings.StartInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
            RenderSettings.EndInBars = (float)RenderTiming.ConvertReferenceTime(RenderSettings.EndInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
        }

        FormInputs.AddVerticalSpace(5);

        // Start and End on separate rows (standard style)
        var changed = FormInputs.AddFloat($"{RenderWindowStrings.StartLabel} ({RenderSettings.Reference})", ref RenderSettings.StartInBars, 0, float.MaxValue, 0.1f, true);
        changed |= FormInputs.AddFloat($"{RenderWindowStrings.EndLabel} ({RenderSettings.Reference})", ref RenderSettings.EndInBars, 0, float.MaxValue, 0.1f, true);
        
        if (changed)
            RenderSettings.TimeRange = RenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace(5);

        // FPS row
        if (FormInputs.AddFloat(RenderWindowStrings.FpsLabel, ref RenderSettings.Fps, 1, 120, 0.1f, true))
        {
            if (RenderSettings.Reference == RenderSettings.TimeReference.Frames)
            {
                RenderSettings.StartInBars = (float)RenderTiming.ConvertFps(RenderSettings.StartInBars, _uiState.LastValidFps, RenderSettings.Fps);
                RenderSettings.EndInBars = (float)RenderTiming.ConvertFps(RenderSettings.EndInBars, _uiState.LastValidFps, RenderSettings.Fps);
            }
            _uiState.LastValidFps = RenderSettings.Fps;
        }

        // Resolution row
        FormInputs.DrawInputLabel(RenderWindowStrings.ResolutionLabel);
        var resSize = FormInputs.GetAvailableInputSize(null, false, true);
        DrawResolutionPopoverCompact(resSize.X); 
        
        FormInputs.AddVerticalSpace(10);
        RenderSettings.FrameCount = RenderTiming.ComputeFrameCount(RenderSettings);
        FormInputs.AddVerticalSpace(5);
        
        // Motion Blur Samples
        if (FormInputs.AddInt(RenderWindowStrings.MotionBlurLabel, ref RenderSettings.OverrideMotionBlurSamples, -1, 50, 1,
                              RenderWindowStrings.TooltipMotionBlur))
        {
            RenderSettings.OverrideMotionBlurSamples = Math.Clamp(RenderSettings.OverrideMotionBlurSamples, -1, 50);
        }

        // Show hint when motion blur is disabled
        if (RenderSettings.OverrideMotionBlurSamples == -1)
        {
            FormInputs.AddHint(RenderWindowStrings.HintMotionBlur);
        }
    }

    private static void DrawResolutionPopoverCompact(float width)
    {
        var currentPct = (int)(RenderSettings.ResolutionFactor * 100);
        ImGui.SetNextItemWidth(width);
        
        if (ImGui.Button($"{currentPct}%##Res", new Vector2(width, 0)))
        {
            ImGui.OpenPopup("ResolutionPopover");
        }
        CustomComponents.TooltipForLastItem(RenderWindowStrings.TooltipResolutionScale);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        ImGui.SetNextWindowSize(new Vector2(160 * T3Ui.UiScaleFactor, 0));
        
        if (ImGui.BeginPopup("ResolutionPopover", ImGuiWindowFlags.NoMove))
        {
            static void DrawSelectable(string label, float factor)
            {
                bool isSelected = Math.Abs(RenderSettings.ResolutionFactor - factor) < 0.001f;
                if (ImGui.Selectable(label, isSelected))
                {
                    RenderSettings.ResolutionFactor = factor;
                }
            }

            DrawSelectable("25%", 0.25f);
            DrawSelectable("50%", 0.5f);
            DrawSelectable("100%", 1.0f);
            DrawSelectable("200%", 2.0f);

            CustomComponents.SeparatorLine();
            
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(RenderWindowStrings.CustomResolutionLabel);
            ImGui.PopStyleColor();
            
            var customPct = RenderSettings.ResolutionFactor * 100f;
            ImGui.SetNextItemWidth(100 * T3Ui.UiScaleFactor);
            if (ImGui.InputFloat("##CustomRes", ref customPct, 0, 0, "%.0f%%"))
            {
                customPct = Math.Clamp(customPct, 1f, 1000f);
                RenderSettings.ResolutionFactor = customPct / 100f;
            }
            
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
    }

    private void DrawVideoSettings(Int2 size)
    {
        // Bitrate in Mbps
        var bitrateMbps = RenderSettings.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat(RenderWindowStrings.BitrateLabel, ref bitrateMbps, 0.1f, 500f, 0.5f, true, true,
                                RenderWindowStrings.TooltipBitrate))
        {
            RenderSettings.Bitrate = (int)(bitrateMbps * 1_000_000f);
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        var totalPixels = (long)size.Width * size.Height;
        bool isValidSize = totalPixels > 0 && RenderSettings.Fps > 0;
        double bitsPerPixel = isValidSize 
                                  ? RenderSettings.Bitrate / (double)totalPixels / RenderSettings.Fps 
                                  : 0;

        var matchingQuality = GetQualityLevelFromRate((float)bitsPerPixel);
        FormInputs.AddHint($"{matchingQuality.Title} quality (Est. {RenderSettings.Bitrate * duration / 1024 / 1024 / 8:0.#} MB)");
        CustomComponents.TooltipForLastItem(matchingQuality.Description);

        // Path
        var currentPath = UserSettings.Config.RenderVideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        FormInputs.AddFilePicker(RenderWindowStrings.MainFolderLabel, ref directory!, ".\\Render", null, RenderWindowStrings.SaveFolderLabel, FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput(RenderWindowStrings.FilenameLabel, ref filename))
        {
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
        }

        if (!filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) filename += ".mp4";
        UserSettings.Config.RenderVideoFilePath = Path.Combine(directory, filename);

        if (RenderPaths.IsFilenameIncrementable())
        {
            FormInputs.AddCheckBox(RenderWindowStrings.AutoIncrementLabel, ref RenderSettings.AutoIncrementVersionNumber);
        }

        FormInputs.AddCheckBox(RenderWindowStrings.ExportAudioLabel, ref RenderSettings.ExportAudio);
    }

    private static void DrawImageSequenceSettings()
    {
        FormInputs.AddFilePicker(RenderWindowStrings.MainFolderLabel, ref UserSettings.Config.RenderSequenceFilePath!, ".\\ImageSequence ", null, RenderWindowStrings.SaveFolderLabel, FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput(RenderWindowStrings.SubfolderLabel, ref UserSettings.Config.RenderSequenceFileName))
        {
            UserSettings.Config.RenderSequenceFileName = (UserSettings.Config.RenderSequenceFileName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequenceFileName)) UserSettings.Config.RenderSequenceFileName = "v01";
        }

        if (FormInputs.AddStringInput(RenderWindowStrings.PrefixLabel, ref UserSettings.Config.RenderSequencePrefix))
        {
            UserSettings.Config.RenderSequencePrefix = (UserSettings.Config.RenderSequencePrefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequencePrefix)) UserSettings.Config.RenderSequencePrefix = "render";
        }

        FormInputs.AddEnumDropdown(ref RenderSettings.FileFormat, RenderWindowStrings.FormatLabel);

        FormInputs.AddCheckBox(RenderWindowStrings.CreateSubFolderLabel, ref RenderSettings.CreateSubFolder);
        FormInputs.AddCheckBox(RenderWindowStrings.AutoIncrementLabel, ref RenderSettings.AutoIncrementSubFolder);
        
        if (RenderSettings.AutoIncrementSubFolder)
        {
            var targetToIncrement = RenderSettings.CreateSubFolder ? UserSettings.Config.RenderSequenceFileName : UserSettings.Config.RenderSequencePrefix;
            var hasVersion = RenderPaths.IsFilenameIncrementable(targetToIncrement);
            if (!hasVersion)
            {
                FormInputs.AddHint(RenderWindowStrings.HintWillAppendVersion);
            }
        }
    }

    private static void DrawRenderSummary()
    {
        var size = RenderProcess.MainOutputOriginalSize;
        var scaledWidthRaw = (int)(size.Width * RenderSettings.ResolutionFactor);
        var scaledHeightRaw = (int)(size.Height * RenderSettings.ResolutionFactor);
        
        // Ensure even dimensions and clamp to valid range
        var scaledWidth = (scaledWidthRaw / 2 * 2).Clamp(2, 16384);
        var scaledHeight = (scaledHeightRaw / 2 * 2).Clamp(2, 16384);

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        var outputPath = RenderPaths.GetExpectedTargetDisplayPath(RenderSettings.Current.RenderMode);
        string format = RenderSettings.RenderMode == RenderSettings.RenderModes.Video 
                            ? "MP4 Video" 
                            : $"{RenderSettings.FileFormat} Sequence";

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"{format} - {scaledWidth}×{scaledHeight} @ {RenderSettings.Fps:0}fps");
        ImGui.TextUnformatted($"{duration / 60:0}:{duration % 60:00.0}s ({RenderSettings.FrameCount} frames)");
        
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextWrapped($"-> {outputPath}");
        ImGui.PopFont();
        
        ImGui.PopStyleColor();
    }
    
    private string GetCachedTargetFilePath(RenderSettings.RenderModes mode)
    {
        var now = Playback.RunTimeInSecs;
        if (now - _uiState.LastPathUpdateTime < 0.2 && !string.IsNullOrEmpty(_uiState.CachedTargetPath))
            return _uiState.CachedTargetPath;

        _uiState.CachedTargetPath = RenderPaths.GetTargetFilePath(mode);
        _uiState.LastPathUpdateTime = now;
        return _uiState.CachedTargetPath;
    }

    private void DrawRenderingControls()
    {
        if (!RenderProcess.IsExporting && !RenderProcess.IsToollRenderingSomething)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Fade(0.7f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            
            if (ImGui.Button(RenderWindowStrings.StartRenderButton, new Vector2(-1, 36 * T3Ui.UiScaleFactor)))
            {
                var targetPath = GetCachedTargetFilePath(RenderSettings.RenderMode);
                if (RenderPaths.FileExists(targetPath))
                {
                    _uiState.ShowOverwriteModal = true;
                }
                else
                {
                    RenderProcess.TryStart(RenderSettings);
                }
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
        }
        else if (RenderProcess.IsExporting)
        {
            var progress = (float)RenderProcess.Progress;
            var elapsed = Playback.RunTimeInSecs - RenderProcess.ExportStartedTimeLocal;

            var timeRemainingStr = RenderWindowStrings.Calculating;
            if (progress > 0.01)
            {
                var estimatedTotal = elapsed / progress;
                var remaining = estimatedTotal - elapsed;
                timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(remaining) + RenderWindowStrings.Remaining;
            }

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UiColors.StatusAutomated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
            ImGui.ProgressBar(progress, new Vector2(-1, 4 * T3Ui.UiScaleFactor), "");
            ImGui.PopStyleColor(2);

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(timeRemainingStr);
            ImGui.PopStyleColor();
            ImGui.PopFont();

            FormInputs.AddVerticalSpace(5);
            if (ImGui.Button(RenderWindowStrings.CancelRenderButton, new Vector2(-1, 24 * T3Ui.UiScaleFactor)))
            {
            RenderProcess.Cancel(RenderWindowStrings.RenderCancelled + StringUtils.HumanReadableDurationFromSeconds(elapsed));
            }
        }
    }

    private void DrawOverwriteDialog()
    {
        // Handle deferred render start (from previous frame's Overwrite button click)
        // This is to have less freeze when clicking the "Overwrite" button.
        if (_uiState.PendingRenderStart)
        {
            _uiState.PendingRenderStart = false;
            RenderProcess.TryStart(RenderSettings);
        }
        
        if (_uiState.ShowOverwriteModal)
        {
            _uiState.DummyOpen = true;
            ImGui.OpenPopup(RenderWindowStrings.OverwriteTitle);
            _uiState.ShowOverwriteModal = false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        
        if (ImGui.BeginPopupModal(RenderWindowStrings.OverwriteTitle, ref _uiState.DummyOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.BeginGroup();
            var targetPath = GetCachedTargetFilePath(RenderSettings.RenderMode);
            bool isFolder = RenderSettings.RenderMode == RenderSettings.RenderModes.ImageSequence && RenderSettings.CreateSubFolder;

            var displayPath = isFolder ? Path.GetFileName(Path.GetDirectoryName(targetPath)) : Path.GetFileName(targetPath);
            var message = isFolder ? RenderWindowStrings.OverwriteFolderMessage : RenderWindowStrings.OverwriteMessage;

            ImGui.TextUnformatted(message);
            
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(displayPath);
            ImGui.PopFont();
            
            ImGui.Dummy(new Vector2(0,10));
            ImGui.TextUnformatted(RenderWindowStrings.OverwriteConfirm);
            FormInputs.AddVerticalSpace(20);

            if (ImGui.Button(RenderWindowStrings.OverwriteButton, new Vector2(120, 0)))
            {
                // Defer render start to next frame so popup closes immediately
                _uiState.PendingRenderStart = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(RenderWindowStrings.CancelButton, new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            
            // Force minimum width
            ImGui.Dummy(new Vector2(350, 1));
            
            ImGui.EndGroup();
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
    }

    // Helpers
    private RenderSettings.QualityLevel GetQualityLevelFromRate(float bitsPerPixelSecond)
    {
        RenderSettings.QualityLevel matchingQuality = default;
        for (var i = _definedQualityLevels.Length - 1; i >= 0; i--)
        {
            matchingQuality = _definedQualityLevels[i];
            if (matchingQuality.MinBitsPerPixelSecond < bitsPerPixelSecond)
                break;
        }

        return matchingQuality;
    }

    internal override List<Window> GetInstances() => [];

    private readonly WindowUiState _uiState = new();
    
    private static RenderSettings RenderSettings => RenderSettings.Current;

    private readonly RenderSettings.QualityLevel[] _definedQualityLevels =
        [
            new(0.01, "Poor", "Very low quality. Consider lower resolution."),
            new(0.02, "Low", "Probable strong artifacts"),
            new(0.05, "Medium", "Will exhibit artifacts in noisy regions"),
            new(0.08, "Okay", "Compromise between filesize and quality"),
            new(0.12, "Good", "Good quality. Probably sufficient for YouTube."),
            new(0.5, "Very good", "Excellent quality, but large."),
            new(1, "Reference", "Indistinguishable. Very large files."),
        ];

    private sealed class WindowUiState
    {
        public string LastHelpString = string.Empty;
        public float LastValidFps = RenderSettings.Current.Fps;
        
        // UI State for Overwrite Dialog
        public bool ShowOverwriteModal;
        public bool PendingRenderStart;
        public bool DummyOpen = true;
        
        // Cached path
        public string CachedTargetPath = string.Empty;
        public double LastPathUpdateTime = -1;
    }

    private static class RenderWindowStrings
    {
        public const string WindowTitle = "Render To File";
        public const string NoOutputView = "No output view available";
        public const string OutputViewEmpty = "The output view is empty";
        public const string SymbolMustHaveTextureOutput = "Select or pin a Symbol with Texture2D output in order to render to file";
        public const string StartRenderButton = "Start Render";
        public const string CancelRenderButton = "Cancel Render";
        public const string TooltipTextureOutputRequired = "Only Symbols with a texture2D output can be rendered to file";
        public const string ReadyToRender = "Ready to render.";
        
        public const string RenderModeLabel = "Render Mode";
        public const string RangeLabel = "Range";
        public const string ScaleLabel = "Scale";
        public const string StartLabel = "Start";
        public const string EndLabel = "End";
        public const string FpsLabel = "FPS";
        public const string ResolutionLabel = "Resolution";
        public const string CustomResolutionLabel = "Custom:";
        public const string TooltipResolutionScale = "Scale resolution of rendered frames.";
        
        public const string MotionBlurLabel = "Motion Blur";
        public const string TooltipMotionBlur = "Number of motion blur samples. Set to -1 to disable. Requires [RenderWithMotionBlur] operator.";
        public const string HintMotionBlur = "Motion blur disabled. (Use samples > 0 and [RenderWithMotionBlur])";
        
        public const string BitrateLabel = "Bitrate";
        public const string TooltipBitrate = "Video bitrate in megabits per second.";
        public const string MainFolderLabel = "Main Folder";
        public const string SaveFolderLabel = "Save folder.";
        public const string FilenameLabel = "Filename";
        public const string FormatLabel = "Format";
        public const string SubfolderPrefixLabel = "Subfolder / Prefix";
        public const string SubfolderLabel = "Subfolder";
        public const string PrefixLabel = "Filename Prefix";
        public const string AutoIncrementLabel = "Auto-increment version";
        public const string CreateSubFolderLabel = "Create subfolder";
        public const string HintWillAppendVersion = "Suffix '_v01' will be added after render";
        public const string ExportAudioLabel = "Export Audio (experimental)";
        
        public const string Calculating = "Calculating...";
        public const string Remaining = " remaining";
        public const string RenderCancelled = "Render cancelled after ";
        
        public const string OverwriteTitle = "Overwrite?";
        public const string OverwriteMessage = "A file with this name already exists:";
        public const string OverwriteFolderMessage = "A folder with this name already exists and is not empty:";
        public const string OverwriteConfirm = "Do you want to overwrite it?";
        public const string OverwriteButton = "Overwrite";
        public const string CancelButton = "Cancel";
        public const string OpenOutputFolderButton = "Open output folder";
    }
}