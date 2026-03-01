#nullable enable
using System.IO;
using ImGuiNET;
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
        Config.Title = "Render To File";
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
            _uiState.LastHelpString = "No output view available";
            CustomComponents.HelpText(_uiState.LastHelpString);
            return;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputType)
        {
            _uiState.LastHelpString = RenderProcess.MainOutputType == null
                                  ? "The output view is empty"
                                  : "Select or pin a Symbol with Texture2D output in order to render to file";
            ImGui.Button("Start Render", new Vector2(-1, 0));
            CustomComponents.TooltipForLastItem("Only Symbols with a texture2D output can be rendered to file");
            ImGui.EndDisabled();
            CustomComponents.HelpText(_uiState.LastHelpString);
            return;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputTexture)
        {
            CustomComponents.HelpText("Please select or pin an Image operator.");
            return;
        }
        
        //Debug.Assert(RenderProcess.MainOutputTexture != null && !RenderProcess.MainOutputTexture.IsDisposed);

        _uiState.LastHelpString = "Ready to render.";

        FormInputs.AddVerticalSpace();
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.ForNextExport.RenderMode, "Render Mode");

        FormInputs.AddVerticalSpace();

        if (RenderSettings.ForNextExport.RenderMode == RenderSettings.RenderModes.Video)
            DrawVideoSettings();
        else
            DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(2);
        
        // Final Summary Card
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        
        if (ImGui.BeginChild("Summary", new Vector2(-1, 64 * T3Ui.UiScaleFactor), false, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4);
            DrawRenderSummary(RenderProcess.GetActiveOrRequestedSettings());
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
            if (ImGui.Button("Open output folder"))
            {
                CoreUi.Instance.OpenWithDefaultApplication(RenderProcess.LastTargetDirectory);
            }
        }
    }

    private void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range row
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.ForNextExport.TimeRange, "Range");
        RenderTiming.ApplyTimeRange(RenderSettings.ForNextExport);
        
        // Scale row (now under Range)
        var oldRef = RenderSettings.ForNextExport.TimeReference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.ForNextExport.TimeReference, "Scale"))
        {
            RenderSettings.ForNextExport.StartInBars =
                (float)RenderTiming.ConvertReferenceTime(RenderSettings.ForNextExport.StartInBars, oldRef, RenderSettings.ForNextExport.TimeReference, RenderSettings.ForNextExport.FrameRate);
            RenderSettings.ForNextExport.EndInBars = (float)RenderTiming.ConvertReferenceTime(RenderSettings.ForNextExport.EndInBars, oldRef, RenderSettings.ForNextExport.TimeReference, RenderSettings.ForNextExport.FrameRate);
        }

        FormInputs.AddVerticalSpace(5);

        // Start and End on separate rows (standard style)
        var changed = FormInputs.AddFloat($"{"Start"} ({RenderSettings.ForNextExport.TimeReference})", ref RenderSettings.ForNextExport.StartInBars, 0, float.MaxValue, 0.1f, true);
        changed |= FormInputs.AddFloat($"{"End"} ({RenderSettings.ForNextExport.TimeReference})", ref RenderSettings.ForNextExport.EndInBars, 0, float.MaxValue, 0.1f, true);
        
        if (changed)
            RenderSettings.ForNextExport.TimeRange = RenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace(5);

        // FPS row
        if (FormInputs.AddFloat("FPS", ref RenderSettings.ForNextExport.FrameRate, 1, 120, 0.1f, true))
        {
            if (RenderSettings.ForNextExport.TimeReference == RenderSettings.TimeReferences.Frames)
            {
                RenderSettings.ForNextExport.StartInBars = (float)RenderTiming.ConvertFps(RenderSettings.ForNextExport.StartInBars, _uiState.LastValidFps, RenderSettings.ForNextExport.FrameRate);
                RenderSettings.ForNextExport.EndInBars = (float)RenderTiming.ConvertFps(RenderSettings.ForNextExport.EndInBars, _uiState.LastValidFps, RenderSettings.ForNextExport.FrameRate);
            }
            _uiState.LastValidFps = RenderSettings.ForNextExport.FrameRate;
        }

        // Resolution row
        FormInputs.DrawInputLabel("Resolution");
        var resSize = FormInputs.GetAvailableInputSize(null, false, true);
        DrawResolutionPopoverCompact(resSize.X); 
        
        FormInputs.AddVerticalSpace(10);
        //RenderSettings.ForNextExport.FrameCount = RenderTiming.ComputeFrameCount(ActiveSettings);
        FormInputs.AddVerticalSpace(5);
        
        // Motion Blur Samples
        if (FormInputs.AddInt("Motion Blur", ref RenderSettings.ForNextExport.OverrideMotionBlurSamples, -1, 50, 1,
                              "Number of motion blur samples. Set to -1 to disable. Requires [RenderWithMotionBlur] operator."))
        {
            RenderSettings.ForNextExport.OverrideMotionBlurSamples = Math.Clamp(RenderSettings.ForNextExport.OverrideMotionBlurSamples, -1, 50);
        }

        // Show hint when motion blur is disabled
        if (RenderSettings.ForNextExport.OverrideMotionBlurSamples == -1)
        {
            FormInputs.AddHint("Motion blur disabled. (Use samples > 0 and [RenderWithMotionBlur])");
        }
    }

    private static void DrawResolutionPopoverCompact(float width)
    {
        var currentPct = (int)(RenderSettings.ForNextExport.ResolutionFactor * 100);
        ImGui.SetNextItemWidth(width);
        
        if (ImGui.Button($"{currentPct}%##Res", new Vector2(width, 0)))
        {
            ImGui.OpenPopup("ResolutionPopover");
        }
        CustomComponents.TooltipForLastItem("Scale resolution of rendered frames.");

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        ImGui.SetNextWindowSize(new Vector2(160 * T3Ui.UiScaleFactor, 0));
        
        if (ImGui.BeginPopup("ResolutionPopover", ImGuiWindowFlags.NoMove))
        {
            static void DrawSelectable(string label, float factor)
            {
                bool isSelected = Math.Abs(RenderSettings.ForNextExport.ResolutionFactor - factor) < 0.001f;
                if (ImGui.Selectable(label, isSelected))
                {
                    RenderSettings.ForNextExport.ResolutionFactor = factor;
                }
            }

            DrawSelectable("25%", 0.25f);
            DrawSelectable("50%", 0.5f);
            DrawSelectable("100%", 1.0f);
            DrawSelectable("200%", 2.0f);

            CustomComponents.SeparatorLine();
            
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("Custom:");
            ImGui.PopStyleColor();
            
            var customPct = RenderSettings.ForNextExport.ResolutionFactor * 100f;
            ImGui.SetNextItemWidth(100 * T3Ui.UiScaleFactor);
            if (ImGui.InputFloat("##CustomRes", ref customPct, 0, 0, "%.0f%%"))
            {
                customPct = Math.Clamp(customPct, 1f, 1000f);
                RenderSettings.ForNextExport.ResolutionFactor = customPct / 100f;
            }
            
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
    }

    private void DrawVideoSettings()
    {
        var settings = RenderSettings.ForNextExport;
        
        // Bitrate in Mbps
        var bitrateMbps = RenderSettings.ForNextExport.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat("Bitrate", ref bitrateMbps, 0.1f, 500f, 0.5f, true, true,
                                "Video bitrate in megabits per second."))
        {
            RenderSettings.ForNextExport.Bitrate = (int)(bitrateMbps * 1_000_000f);
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.ForNextExport.StartInBars, RenderSettings.ForNextExport.TimeReference, RenderSettings.ForNextExport.FrameRate);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.ForNextExport.EndInBars, RenderSettings.ForNextExport.TimeReference, RenderSettings.ForNextExport.FrameRate);
        var duration = Math.Max(0, endSec - startSec);

        RenderProcess.TryGetRenderResolution(settings, out var resolution);
        var totalPixels = (long)resolution.Width * resolution.Height;
        bool isValidSize = totalPixels > 0 && RenderSettings.ForNextExport.FrameRate > 0;
        double bitsPerPixel = isValidSize 
                                  ? RenderSettings.ForNextExport.Bitrate / (double)totalPixels / RenderSettings.ForNextExport.FrameRate 
                                  : 0;

        var matchingQuality = GetQualityLevelFromRate((float)bitsPerPixel);
        FormInputs.AddHint($"{matchingQuality.Title} quality (Est. {RenderSettings.ForNextExport.Bitrate * duration / 1024 / 1024 / 8:0.#} MB)");
        CustomComponents.TooltipForLastItem(matchingQuality.Description);

        // Path
        var currentPath = UserSettings.Config.RenderVideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        FormInputs.AddFilePicker("Main Folder", ref directory!, ".\\Render", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Filename", ref filename))
        {
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
        }

        if (!filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) filename += ".mp4";
        UserSettings.Config.RenderVideoFilePath = Path.Combine(directory, filename);

        FormInputs.AddCheckBox("Auto-increment version", ref RenderSettings.ForNextExport.AutoIncrementVersionNumber);
        if (RenderSettings.ForNextExport.AutoIncrementVersionNumber)
        {
            var nextTargetPath = GetCachedTargetFilePath(RenderSettings.RenderModes.Video);
            var nextVersion = RenderPaths.GetVersionString(nextTargetPath);
            
            if (RenderPaths.IsFilenameIncrementable(UserSettings.Config.RenderVideoFilePath))
            {
                FormInputs.AddHint($"Next version will be '{nextVersion}'");
            }
            else
            {
                FormInputs.AddHint($"Suffix '_{nextVersion}' will be added after render");
            }
        }

        FormInputs.AddCheckBox("Export Audio (experimental)", ref RenderSettings.ForNextExport.ExportAudio);
    }

    private void DrawImageSequenceSettings()
    {
        var userConfig = UserSettings.Config;
        var settings = RenderSettings.ForNextExport;
        
        FormInputs.AddFilePicker("Main Folder", ref userConfig.RenderSequenceFilePath!, ".\\ImageSequence ", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Subfolder", ref userConfig.RenderSequenceFileName))
        {
            userConfig.RenderSequenceFileName = (userConfig.RenderSequenceFileName ?? string.Empty).Trim();
        }

        if (FormInputs.AddStringInput("Filename Prefix", ref userConfig.RenderSequencePrefix))
        {
            userConfig.RenderSequencePrefix = (userConfig.RenderSequencePrefix ?? string.Empty).Trim();
        }

        FormInputs.AddEnumDropdown(ref settings.FileFormat, "Format");

        FormInputs.AddCheckBox("Create subfolder", ref settings.CreateSubFolder);
        FormInputs.AddCheckBox("Auto-increment version", ref settings.AutoIncrementSubFolder);
        
        if (settings.AutoIncrementSubFolder)
        {
            var nextTargetPath = GetCachedTargetFilePath(RenderSettings.RenderModes.ImageSequence);
            
            // If we are creating subfolders, the 'prefix' part of the path (the last component) 
            // is NOT the versioned part. The version is in the directory name.
            if (settings.CreateSubFolder)
            {
                nextTargetPath = Path.GetDirectoryName(nextTargetPath) ?? nextTargetPath;
            }
            
            var nextVersion = RenderPaths.GetVersionString(nextTargetPath);
            var targetToIncrement = settings.CreateSubFolder ? userConfig.RenderSequenceFileName : userConfig.RenderSequencePrefix;
            
            if (RenderPaths.IsFilenameIncrementable(targetToIncrement))
            {
                FormInputs.AddHint($"Next version will be '{nextVersion}'");
            }
            else
            {
                FormInputs.AddHint($"Suffix '_{nextVersion}' will be added after render");
            }
        }
    }

    private static void DrawRenderSummary(RenderSettings settings)
    {
        var startSec = RenderTiming.ReferenceTimeToSeconds(settings.StartInBars, settings.TimeReference, settings.FrameRate);
        var endSec = RenderTiming.ReferenceTimeToSeconds(settings.EndInBars, settings.TimeReference, settings.FrameRate);
        var duration = Math.Max(0, endSec - startSec);

        var outputPath = RenderPaths.GetExpectedTargetDisplayPath(settings.RenderMode);
        string format = settings.RenderMode == RenderSettings.RenderModes.Video 
                            ? "MP4 Video" 
                            : $"{settings.FileFormat} Sequence";

        RenderProcess.TryGetRenderResolution(settings, out var resolution);

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"{format} - {resolution.Width}×{resolution.Height} @ {settings.FrameRate:0}fps");

        var frameCount = RenderTiming.ComputeFrameCount(settings);
        ImGui.TextUnformatted($"{duration / 60:0}:{duration % 60:00.0}s ({frameCount} frames)");
        
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextUnformatted("Export to:");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Fade(1.2f).Rgba);
        ImGui.TextWrapped(outputPath);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        
        ImGui.PopStyleColor();
    }
    
    public string GetCachedTargetFilePath(RenderSettings.RenderModes mode)
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
        if (RenderProcess.IsExporting)
        {
            var progress = (float)RenderProcess.Progress;
            var elapsed = Playback.RunTimeInSecs - RenderProcess.ExportStartedTimeLocal;

            var timeRemainingStr = "Calculating...";
            if (progress > 0.01)
            {
                var estimatedTotal = elapsed / progress;
                var remaining = estimatedTotal - elapsed;
                timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(remaining) + " remaining";
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
            if (ImGui.Button("Cancel Render", new Vector2(-1, 24 * T3Ui.UiScaleFactor)))
            {
                RenderProcess.Cancel("Render cancelled after " + StringUtils.HumanReadableDurationFromSeconds(elapsed));
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Fade(0.7f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

            var isValid = ValidateSettings(out var errorMessage);
            if (!isValid)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Start Render", new Vector2(-1, 36 * T3Ui.UiScaleFactor)))
            {
                var targetPath = GetCachedTargetFilePath(RenderSettings.ForNextExport.RenderMode);
                if (RenderPaths.FileExists(targetPath))
                {
                    _uiState.ShowOverwriteModal = true;
                }
                else
                {
                    RenderProcess.TryStartVideoExport();
                }
            }

            if (!isValid)
            {
                ImGui.EndDisabled();
                CustomComponents.TooltipForLastItem(errorMessage);
                _uiState.LastHelpString = errorMessage;
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
        }
    }

    private static bool ValidateSettings(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (RenderSettings.ForNextExport.RenderMode == RenderSettings.RenderModes.Video)
        {
            var currentPath = UserSettings.Config.RenderVideoFilePath ?? string.Empty;
            var filename = Path.GetFileNameWithoutExtension(currentPath);
            if (string.IsNullOrWhiteSpace(filename) || filename == ".")
            {
                errorMessage = "Filename cannot be empty.";
                return false;
            }
        }
        else
        {
            if (RenderSettings.ForNextExport.CreateSubFolder && string.IsNullOrWhiteSpace(UserSettings.Config.RenderSequenceFileName))
            {
                errorMessage = "Subfolder name cannot be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(UserSettings.Config.RenderSequencePrefix))
            {
                errorMessage = "Filename prefix cannot be empty.";
                return false;
            }
        }

        return true;
    }

    private void DrawOverwriteDialog()
    {
        // Handle deferred render start (from previous frame's Overwrite button click)
        // This is to have less freeze when clicking the "Overwrite" button.
        if (_uiState.PendingRenderStart)
        {
            _uiState.PendingRenderStart = false;
            RenderProcess.TryStartVideoExport();
        }
        
        if (_uiState.ShowOverwriteModal)
        {
            _uiState.DummyOpen = true;
            ImGui.OpenPopup("Overwrite?");
            _uiState.ShowOverwriteModal = false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        
        if (ImGui.BeginPopupModal("Overwrite?", ref _uiState.DummyOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.BeginGroup();
            var targetPath = GetCachedTargetFilePath(RenderSettings.ForNextExport.RenderMode);
            bool isFolder = RenderSettings.ForNextExport.RenderMode == RenderSettings.RenderModes.ImageSequence && RenderSettings.ForNextExport.CreateSubFolder;

            var displayPath = isFolder ? Path.GetFileName(Path.GetDirectoryName(targetPath)) : Path.GetFileName(targetPath);
            var message = isFolder ? "A folder with this name already exists and is not empty:" : "A file with this name already exists:";

            ImGui.TextUnformatted(message);
            
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(displayPath);
            ImGui.PopFont();
            
            ImGui.Dummy(new Vector2(0,10));
            ImGui.TextUnformatted("Do you want to overwrite it?");
            FormInputs.AddVerticalSpace(20);

            if (ImGui.Button("Overwrite", new Vector2(120, 0)))
            {
                // Defer render start to next frame so popup closes immediately
                _uiState.PendingRenderStart = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
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
    
    // Simplified access to current settings
    //private static readonly RenderSettings ActiveSettings = RenderSettings.Current;

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
        public float LastValidFps = RenderSettings.ForNextExport.FrameRate;
        
        // UI State for Overwrite Dialog
        public bool ShowOverwriteModal;
        public bool PendingRenderStart;
        public bool DummyOpen = true;
        
        // Cached path
        public string CachedTargetPath = string.Empty;
        public double LastPathUpdateTime = -1;
    }
}