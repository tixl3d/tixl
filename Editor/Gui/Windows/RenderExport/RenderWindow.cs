#nullable enable
using System.IO;
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Utils;
using T3.Core.Animation;
using T3.Core.SystemUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

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
        var modified = false;
        modified |= DrawTimeSetup();
        modified |= DrawInnerContent();

        if (modified)
            ProjectView.Focused?.CompositionInstance?.Symbol.GetSymbolUi()?.FlagAsModified();
    }

    private bool DrawInnerContent()
    {
        if (RenderProcess.State == RenderProcess.States.NoOutputWindow)
        {
            _uiState.LastHelpString = "No output view available";
            CustomComponents.HelpText(_uiState.LastHelpString);
            return false;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputType)
        {
            _uiState.LastHelpString = RenderProcess.MainOutputType == null
                                  ? "The output view is empty"
                                  : "Select or pin a Symbol with Texture2D output in order to render to file";
            ImGui.Button("Start Render", new Vector2(-1, 0));
            CustomComponents.TooltipForLastItem("Only Symbols with a texture2D output can be rendered to file");
            //ImGui.EndDisabled();
            CustomComponents.HelpText(_uiState.LastHelpString);
            return false;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputTexture)
        {
            CustomComponents.HelpText("Please select or pin an Image operator.");
            return false;
        }

        _uiState.LastHelpString = "Ready to render.";

        var modified = false;

        FormInputs.AddVerticalSpace();
        modified |= FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.Current.RenderMode, "Render Mode");

        FormInputs.AddVerticalSpace();

        if (RenderSettings.Current.RenderMode == RenderSettings.RenderModes.Video)
            modified |= DrawVideoSettings();
        else
            modified |= DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(2);

        // Final Summary Card
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);

        if (ImGui.BeginChild("Summary", new Vector2(-1, 64 * T3Ui.UiScaleFactor), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
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

        return modified;
    }

    private bool DrawTimeSetup()
    {
        var modified = false;
        var s = RenderSettings.Current;

        FormInputs.SetIndentToParameters();

        // Range row
        modified |= FormInputs.AddSegmentedButtonWithLabel(ref s.TimeRange, "Range");
        RenderTiming.ApplyTimeRange(s);

        // Scale row (now under Range)
        var oldRef = s.TimeReference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref s.TimeReference, "Scale"))
        {
            modified = true;
            s.StartInBars = (float)RenderTiming.ConvertReferenceTime(s.StartInBars, oldRef, s.TimeReference, s.FrameRate);
            s.EndInBars = (float)RenderTiming.ConvertReferenceTime(s.EndInBars, oldRef, s.TimeReference, s.FrameRate);
        }

        FormInputs.AddVerticalSpace(5);

        // Start and End on separate rows (standard style)
        var rangeChanged = FormInputs.AddFloat($"{"Start"} ({s.TimeReference})", ref s.StartInBars, 0, float.MaxValue, 0.1f, true);
        rangeChanged |= FormInputs.AddFloat($"{"End"} ({s.TimeReference})", ref s.EndInBars, 0, float.MaxValue, 0.1f, true);

        if (rangeChanged)
            s.TimeRange = RenderSettings.TimeRanges.Custom;

        modified |= rangeChanged;

        FormInputs.AddVerticalSpace(5);

        // FPS row
        if (FormInputs.AddFloat("FPS", ref s.FrameRate, 1, 120, 0.1f, true, false, null, RenderSettings.Defaults.FrameRate))
        {
            modified = true;
            if (s.TimeReference == RenderSettings.TimeReferences.Frames)
            {
                s.StartInBars = (float)RenderTiming.ConvertFps(s.StartInBars, _uiState.LastValidFps, s.FrameRate);
                s.EndInBars = (float)RenderTiming.ConvertFps(s.EndInBars, _uiState.LastValidFps, s.FrameRate);
            }
            _uiState.LastValidFps = s.FrameRate;
        }

        // Resolution row
        modified |= FormInputs.AddFloat("Resolution %", ref s.ResolutionFactor, 0.01f, 10f, 0.01f, true, true,
                                         "Scale factor for rendered resolution (1.0 = 100%).",
                                         RenderSettings.Defaults.ResolutionFactor);

        FormInputs.AddVerticalSpace(10);
        FormInputs.AddVerticalSpace(5);

        // Motion Blur Samples
        if (FormInputs.AddInt("Motion Blur", ref s.OverrideMotionBlurSamples, -1, 50, 1,
                              "Number of motion blur samples. Set to -1 to disable. Requires [RenderWithMotionBlur] operator.",
                              RenderSettings.Defaults.OverrideMotionBlurSamples))
        {
            modified = true;
            s.OverrideMotionBlurSamples = Math.Clamp(s.OverrideMotionBlurSamples, -1, 50);
        }

        // Show hint when motion blur is disabled
        if (s.OverrideMotionBlurSamples == -1)
        {
            FormInputs.AddHint("Motion blur disabled. (Use samples > 0 and [RenderWithMotionBlur])");
        }

        return modified;
    }


    private bool DrawVideoSettings()
    {
        var modified = false;
        var s = RenderSettings.Current;

        // Bitrate in Mbps
        var bitrateMbps = s.Bitrate / 1_000_000f;
        var defaultBitrateMbps = RenderSettings.Defaults.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat("Bitrate", ref bitrateMbps, 0.1f, 500f, 0.5f, true, true,
                                "Video bitrate in megabits per second.",
                                defaultBitrateMbps))
        {
            modified = true;
            s.Bitrate = (int)(bitrateMbps * 1_000_000f);
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(s.StartInBars, s.TimeReference, s.FrameRate);
        var endSec = RenderTiming.ReferenceTimeToSeconds(s.EndInBars, s.TimeReference, s.FrameRate);
        var duration = Math.Max(0, endSec - startSec);

        RenderProcess.TryGetRenderResolution(s, out var resolution);
        var totalPixels = (long)resolution.Width * resolution.Height;
        bool isValidSize = totalPixels > 0 && s.FrameRate > 0;
        double bitsPerPixel = isValidSize
                                  ? s.Bitrate / (double)totalPixels / s.FrameRate
                                  : 0;

        var matchingQuality = GetQualityLevelFromRate((float)bitsPerPixel);
        FormInputs.AddHint($"{matchingQuality.Title} quality (Est. {s.Bitrate * duration / 1024 / 1024 / 8:0.#} MB)");
        CustomComponents.TooltipForLastItem(matchingQuality.Description);

        // Path
        var currentPath = s.VideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        modified |= FormInputs.AddFilePicker("Main Folder", ref directory!, ".\\Render", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Filename", ref filename))
        {
            modified = true;
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
        }

        if (!filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) filename += ".mp4";
        s.VideoFilePath = Path.Combine(directory, filename);

        modified |= FormInputs.AddCheckBox("Auto-increment version", ref s.AutoIncrementVersionNumber, null, RenderSettings.Defaults.AutoIncrementVersionNumber);
        if (s.AutoIncrementVersionNumber)
        {
            var nextTargetPath = GetCachedTargetFilePath(RenderSettings.RenderModes.Video);
            var nextVersion = RenderPaths.GetVersionString(nextTargetPath);

            if (RenderPaths.IsFilenameIncrementable(s.VideoFilePath))
            {
                FormInputs.AddHint($"Next version will be '{nextVersion}'");
            }
            else
            {
                FormInputs.AddHint($"Suffix '_{nextVersion}' will be added after render");
            }
        }

        modified |= FormInputs.AddCheckBox("Export Audio (experimental)", ref s.ExportAudio, null, RenderSettings.Defaults.ExportAudio);
        return modified;
    }

    private bool DrawImageSequenceSettings()
    {
        var modified = false;
        var s = RenderSettings.Current;

        modified |= FormInputs.AddFilePicker("Main Folder", ref s.SequenceFilePath!, ".\\ImageSequence ", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Subfolder", ref s.SequenceFileName))
        {
            modified = true;
            s.SequenceFileName = (s.SequenceFileName ?? string.Empty).Trim();
        }

        if (FormInputs.AddStringInput("Filename Prefix", ref s.SequencePrefix))
        {
            modified = true;
            s.SequencePrefix = (s.SequencePrefix ?? string.Empty).Trim();
        }

        modified |= FormInputs.AddEnumDropdown(ref s.FileFormat, "Format", null, RenderSettings.Defaults.FileFormat);
        modified |= FormInputs.AddCheckBox("Create subfolder", ref s.CreateSubFolder, null, RenderSettings.Defaults.CreateSubFolder);
        modified |= FormInputs.AddCheckBox("Auto-increment version", ref s.AutoIncrementSubFolder, null, RenderSettings.Defaults.AutoIncrementSubFolder);

        if (s.AutoIncrementSubFolder)
        {
            var nextTargetPath = GetCachedTargetFilePath(RenderSettings.RenderModes.ImageSequence);

            // If we are creating subfolders, the 'prefix' part of the path (the last component)
            // is NOT the versioned part. The version is in the directory name.
            if (s.CreateSubFolder)
            {
                nextTargetPath = Path.GetDirectoryName(nextTargetPath) ?? nextTargetPath;
            }

            var nextVersion = RenderPaths.GetVersionString(nextTargetPath);
            var targetToIncrement = s.CreateSubFolder ? s.SequenceFileName : s.SequencePrefix;

            if (RenderPaths.IsFilenameIncrementable(targetToIncrement))
            {
                FormInputs.AddHint($"Next version will be '{nextVersion}'");
            }
            else
            {
                FormInputs.AddHint($"Suffix '_{nextVersion}' will be added after render");
            }
        }

        return modified;
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
                var targetPath = GetCachedTargetFilePath(RenderSettings.Current.RenderMode);
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

        if (RenderSettings.Current.RenderMode == RenderSettings.RenderModes.Video)
        {
            var currentPath = RenderSettings.Current.VideoFilePath ?? string.Empty;
            var filename = Path.GetFileNameWithoutExtension(currentPath);
            if (string.IsNullOrWhiteSpace(filename) || filename == ".")
            {
                errorMessage = "Filename cannot be empty.";
                return false;
            }
        }
        else
        {
            if (RenderSettings.Current.CreateSubFolder && string.IsNullOrWhiteSpace(RenderSettings.Current.SequenceFileName))
            {
                errorMessage = "Subfolder name cannot be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(RenderSettings.Current.SequencePrefix))
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
            var targetPath = GetCachedTargetFilePath(RenderSettings.Current.RenderMode);
            bool isFolder = RenderSettings.Current.RenderMode == RenderSettings.RenderModes.ImageSequence && RenderSettings.Current.CreateSubFolder;

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
        public float LastValidFps = RenderSettings.Current.FrameRate;

        // UI State for Overwrite Dialog
        public bool ShowOverwriteModal;
        public bool PendingRenderStart;
        public bool DummyOpen = true;

        // Cached path
        public string CachedTargetPath = string.Empty;
        public double LastPathUpdateTime = -1;
    }
}
