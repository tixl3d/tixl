#nullable enable
using System.IO;
using ImGuiNET;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Utils;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Video;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

[HelpUiID("RenderSettings")]
internal sealed class RenderWindow : Window
{
    public RenderWindow()
    {
        Config.Title = "Render To File";
    }

    private enum Sections
    {
        Source,
        FormatAndQuality,
        OutputTarget,
    }

    protected override void DrawContent()
    {
        if (RenderProcess.State is RenderProcess.States.Undefined
                                or RenderProcess.States.NoOutputWindow
                                or RenderProcess.States.NoValidOutputType
                                or RenderProcess.States.NoValidOutputTexture)
        {
            DrawBlockedState();
            return;
        }

        _uiState.LastHelpString = "Ready to render.";
        var scale = T3Ui.UiScaleFactor;

        NavigationSidebar.BeginLayout();

        var footerHeight = 56 * scale;
        var bodyHeight = ImGui.GetContentRegionAvail().Y - footerHeight;

        var modified = false;

        ImGui.BeginChild("body", new Vector2(0, bodyHeight), ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        {
            NavigationSidebar.BeginColumn("renderNav", 140);
            {
                if (NavigationSidebar.Item("Source", _uiState.ActiveSection == Sections.Source))
                    _uiState.ActiveSection = Sections.Source;

                if (NavigationSidebar.Item("Format & Quality", _uiState.ActiveSection == Sections.FormatAndQuality))
                    _uiState.ActiveSection = Sections.FormatAndQuality;

                if (NavigationSidebar.Item("Output Target", _uiState.ActiveSection == Sections.OutputTarget))
                    _uiState.ActiveSection = Sections.OutputTarget;
            }
            NavigationSidebar.EndColumn();

            NavigationSidebar.BeginContentPanel(SectionTitle(_uiState.ActiveSection), this, DrawHeaderHelp);
            {
                FormInputs.SetIndentToParameters();
                switch (_uiState.ActiveSection)
                {
                    case Sections.Source:
                        modified |= DrawSourceSection();
                        break;
                    case Sections.FormatAndQuality:
                        modified |= DrawFormatSection();
                        break;
                    case Sections.OutputTarget:
                        modified |= DrawOutputSection();
                        break;
                }
            }
            NavigationSidebar.EndContentPanel();
        }
        ImGui.EndChild();

        DrawFooter();
        DrawOverwriteDialog();

        if (modified)
            ProjectView.Focused?.CompositionInstance?.Symbol.GetSymbolUi()?.FlagAsModified();
    }

    // Help button on the content-panel header: hover shows the embedded snippet for the active section, click
    // opens the published documentation page.
    private const string RenderHelpUrl = "https://help.tixl.app/using/ExportVideos/";

    private void DrawHeaderHelp()
    {
        var size = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());

        // Flush to the content region's right edge (the panel padding is already excluded), so it sits as far
        // right as the title's left inset — RightAlign() would double-count the window padding and inset it more.
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - size.X);

        var docId = _uiState.ActiveSection switch
                        {
                            Sections.FormatAndQuality => "RenderExport_Format",
                            Sections.OutputTarget     => "RenderExport_Target",
                            _                         => "RenderExport_Source",
                        };
        DocumentationButton.Draw(docId, RenderHelpUrl, size);
    }

    private static string SectionTitle(Sections section) => section switch
                                                                {
                                                                    Sections.Source           => "Source",
                                                                    Sections.FormatAndQuality => "Format & Quality",
                                                                    Sections.OutputTarget     => "Output Target",
                                                                    _                         => string.Empty,
                                                                };

    // Reasons the output can't be rendered (no view, wrong output type, no texture). Shown in place of the
    // whole sidebar UI since none of the settings are actionable until an image output is available.
    private void DrawBlockedState()
    {
        var message = RenderProcess.State switch
                          {
                              RenderProcess.States.NoOutputWindow    => "No output view available",
                              RenderProcess.States.NoValidOutputType => RenderProcess.MainOutputType == null
                                                                            ? "The output view is empty"
                                                                            : "Select or pin a Symbol with Texture2D output in order to render to file",
                              RenderProcess.States.NoValidOutputTexture => "Please select or pin an Image operator.",
                              _                                         => "No renderable output available",
                          };
        _uiState.LastHelpString = message;
        FormInputs.AddVerticalSpace(15);
        CustomComponents.HelpText(message);
    }

    private const float FooterMargin = 16f;

    private void DrawFooter()
    {
        var scale = T3Ui.UiScaleFactor;
        var margin = FooterMargin * scale;

        FormInputs.AddVerticalSpace(10);

        if (RenderProcess.IsExporting)
        {
            DrawExportProgressFooter();
            return;
        }

        var settings = RenderProcess.GetActiveOrRequestedSettings();
        var isValid = ValidateSettings(out var errorMessage);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(isValid ? BuildSummaryLine(settings) : errorMessage,
                                      Fonts.FontNormal,
                                      isValid ? UiColors.Text : UiColors.StatusAttention);

        // Right cluster: open-folder icon followed by the primary Render button, kept off the window edge.
        var iconSize = ImGui.GetFrameHeight();
        var ctaSize = CustomComponents.GetCtaButtonSize("Render");
        CustomComponents.RightAlign(iconSize + 8 * scale + ctaSize.X + margin);

        var targetDir = GetConfiguredOutputDirectory();
        var canOpen = !string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir);
        if (CustomComponents.IconButton(Icon.FolderOpen, new Vector2(iconSize, iconSize),
                                        canOpen ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canOpen)
        {
            CoreUi.Instance.OpenWithDefaultApplication(targetDir);
        }
        CustomComponents.TooltipForLastItem("Open output folder", targetDir);

        ImGui.SameLine(0, 8 * scale);

        if (!isValid)
            ImGui.BeginDisabled();

        if (CustomComponents.DrawCtaButton("Render", Icon.None, CustomComponents.ButtonStates.Activated))
        {
            var targetPath = GetCachedTargetFilePath(RenderSettings.Current.RenderMode);
            if (RenderPaths.FileExists(targetPath))
                _uiState.ShowOverwriteModal = true;
            else
                RenderProcess.TryStartVideoExport();
        }

        if (!isValid)
        {
            ImGui.EndDisabled();
            CustomComponents.TooltipForLastItem(errorMessage);
            _uiState.LastHelpString = errorMessage;
        }
    }

    private static void DrawExportProgressFooter()
    {
        var scale = T3Ui.UiScaleFactor;
        var margin = FooterMargin * scale;

        if (RenderProcess.IsContinuousActive)
        {
            DrawContinuousActivityFooter(scale, margin);
            return;
        }

        var progress = (float)RenderProcess.Progress;
        var elapsed = RenderProcess.ExportElapsedSeconds;

        var timeRemainingStr = "Calculating...";
        if (progress > 0.01)
        {
            var estimatedTotal = elapsed / progress;
            timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(estimatedTotal - elapsed) + " remaining";
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UiColors.StatusAutomated.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
        ImGui.ProgressBar(progress, new Vector2(-margin, 4 * scale), "");
        ImGui.PopStyleColor(2);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
        CustomComponents.StylizedText(timeRemainingStr, Fonts.FontNormal, UiColors.Text);

        var cancelSize = CustomComponents.GetCtaButtonSize("Cancel");
        CustomComponents.RightAlign(cancelSize.X + margin);
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
        {
            RenderProcess.Cancel("Render cancelled after " + StringUtils.HumanReadableDurationFromSeconds(elapsed));
        }
    }

    // Open-ended captures have no determinate progress, so the footer shows a sweeping activity indicator —
    // a ~30%-wide segment scrolling across the same 4px line a normal render's progress bar would use.
    private static void DrawContinuousActivityFooter(float scale, float margin)
    {
        var barHeight = 4 * scale;
        var width = ImGui.GetContentRegionAvail().X - 2 * margin;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
        var drawList = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = new Vector2(p0.X + width, p0.Y + barHeight);
        var rounding = barHeight * 0.5f;
        drawList.AddRectFilled(p0, p1, UiColors.BackgroundInputField, rounding);

        const float segmentFraction = 0.3f;
        var segWidth = width * segmentFraction;
        var travel = width + segWidth;
        var t = (float)((ImGui.GetTime() * 0.3) % 1.0);
        var segStart = p0.X - segWidth + t * travel;
        var segLeft = Math.Max(segStart, p0.X);
        var segRight = Math.Min(segStart + segWidth, p1.X);
        if (segRight > segLeft)
            drawList.AddRectFilled(new Vector2(segLeft, p0.Y), new Vector2(segRight, p1.Y), UiColors.StatusAttention, rounding);

        ImGui.Dummy(new Vector2(width, barHeight));

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + margin);
        var elapsed = RenderProcess.ExportElapsedSeconds;
        CustomComponents.StylizedText($"Capturing… {RenderProcess.CapturedFrameCount} frames / {StringUtils.HumanReadableDurationFromSeconds(elapsed)}",
                                      Fonts.FontNormal, UiColors.Text);

        var stopSize = CustomComponents.GetCtaButtonSize("Stop");
        CustomComponents.RightAlign(stopSize.X + margin);
        if (CustomComponents.DrawCtaButton("Stop", Icon.None, CustomComponents.ButtonStates.Activated))
        {
            RenderProcess.StopContinuous();
        }
    }

    // One-line render summary shown in the footer, e.g. "2:12.0s / 1920×1080 / H.264 → ~95 MB / 2 min".
    private static string BuildSummaryLine(RenderSettings settings)
    {
        if (settings.TimeRange == RenderSettings.TimeRanges.Continuous)
        {
            RenderProcess.TryGetRenderResolution(settings, out var res);
            var clock = settings.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime ? "realtime" : "deterministic";
            var format = settings.RenderMode == RenderSettings.RenderModes.Video
                             ? CodecDisplayName(settings.VideoCodec)
                             : $"{settings.FileFormat} sequence";
            return $"Continuous · {res.Width}×{res.Height} · {format} · {clock} · {settings.FrameRate:0} fps";
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(settings.StartInBars, settings.TimeReference, settings.FrameRate);
        var endSec = RenderTiming.ReferenceTimeToSeconds(settings.EndInBars, settings.TimeReference, settings.FrameRate);
        var duration = Math.Max(0, endSec - startSec);
        var durationStr = $"{duration / 60:0}:{duration % 60:00.0}s";

        RenderProcess.TryGetRenderResolution(settings, out var resolution);
        var frameCount = RenderTiming.ComputeFrameCount(settings);

        if (settings.RenderMode != RenderSettings.RenderModes.Video)
            return $"{durationStr} / {resolution.Width}×{resolution.Height} / {settings.FileFormat} sequence / {frameCount} frames";

        var (w, h) = settings.VideoCodec.RoundToEncoderBlock(resolution.Width, resolution.Height);
        var bytes = RenderExportEstimate.EstimateBytes(settings.VideoCodec, new Int2(w, h), frameCount, duration, settings.Bitrate);
        var renderSecs = RenderExportEstimate.EstimateSeconds(settings.VideoCodec, new Int2(w, h), frameCount, settings.OverrideMotionBlurSamples);
        return $"{durationStr} / {w}×{h} / {CodecDisplayName(settings.VideoCodec)} → ~{RenderExportEstimate.FormatBytes(bytes)} / {RenderExportEstimate.FormatDuration(renderSecs)}";
    }

    // Folder the next render writes to, used to enable the footer's "open folder" button.
    // Relative paths must resolve against the project folder (like the render itself does), not the process CWD.
    private static string GetConfiguredOutputDirectory()
    {
        var s = RenderSettings.Current;
        if (s.RenderMode == RenderSettings.RenderModes.Video)
        {
            var path = s.VideoFilePath;
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetDirectoryName(RenderPaths.ResolveProjectRelativePath(path)) ?? string.Empty;
        }

        // SequenceFilePath is already a folder, not a file path.
        var folder = s.SequenceFilePath;
        return string.IsNullOrEmpty(folder) ? string.Empty : RenderPaths.ResolveProjectRelativePath(folder);
    }

    // The timeline loop range is empty (zero length), so rendering it would produce nothing.
    private static bool IsLoopRangeEmpty()
    {
        var loop = Playback.Current.LoopRange;
        return loop.End <= loop.Start;
    }

    private static bool IsRangeOptionDisabled(RenderSettings.TimeRanges range)
        => range == RenderSettings.TimeRanges.Loop && IsLoopRangeEmpty();

    private bool DrawSourceSection()
    {
        var modified = false;
        var s = RenderSettings.Current;

        FormInputs.SetIndentToParameters();

        // Range as a left-aligned section header (no label), divided from the timing parameters below.
        // Loop is disabled while the timeline loop has no length — fall back to Custom if it was selected.
        if (s.TimeRange == RenderSettings.TimeRanges.Loop && IsLoopRangeEmpty())
            s.TimeRange = RenderSettings.TimeRanges.Custom;

        modified |= FormInputs.SegmentedButton(ref s.TimeRange, 0, IsRangeOptionDisabled);
        FormInputs.AppendTooltip("""
                                 **What time span to render:**
                                 - **Custom** — a start/end you set
                                 - **Loop** — the timeline's loop range (set a loop first)
                                 - **Soundtrack** — the main soundtrack's length
                                 - **Continuous** — keep recording until you press stop
                                 """);
        RenderTiming.ApplyTimeRange(s);

        FormInputs.AddVerticalSpace(5);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(5);

        var isContinuous = s.TimeRange == RenderSettings.TimeRanges.Continuous;

        if (isContinuous)
        {
            modified |= DrawContinuousOptions(s);
        }
        else
        {
            // Scale row (now under Range)
            var oldRef = s.TimeReference;
            if (FormInputs.AddSegmentedButtonWithLabel(ref s.TimeReference, "Scale", 0,
                                                       "The unit for Start and End: musical bars, seconds, or frame numbers."))
            {
                modified = true;
                s.StartInBars = (float)RenderTiming.ConvertReferenceTime(s.StartInBars, oldRef, s.TimeReference, s.FrameRate);
                s.EndInBars = (float)RenderTiming.ConvertReferenceTime(s.EndInBars, oldRef, s.TimeReference, s.FrameRate);
            }

            FormInputs.AddVerticalSpace(5);

            // The canonical defaults are stored in bars; convert them to the active time unit so the
            // revert icon targets the right value when editing in Seconds or Frames.
            var startDefault = (float)RenderTiming.ConvertReferenceTime(RenderSettings.Defaults.StartInBars,
                                                                        RenderSettings.TimeReferences.Bars, s.TimeReference, s.FrameRate);
            var endDefault = (float)RenderTiming.ConvertReferenceTime(RenderSettings.Defaults.EndInBars,
                                                                      RenderSettings.TimeReferences.Bars, s.TimeReference, s.FrameRate);

            // Start and End on separate rows (standard style)
            var rangeChanged = FormInputs.AddFloat($"Start ({s.TimeReference})", ref s.StartInBars, 0, float.MaxValue, 0.1f, true, false,
                                                   "Where the render begins, in the unit chosen under Scale.", startDefault);
            rangeChanged |= FormInputs.AddFloat($"End ({s.TimeReference})", ref s.EndInBars, 0, float.MaxValue, 0.1f, true, false,
                                                "Where the render ends, in the unit chosen under Scale.", endDefault);

            if (rangeChanged)
                s.TimeRange = RenderSettings.TimeRanges.Custom;

            modified |= rangeChanged;
        }

        FormInputs.AddVerticalSpace(5);

        // FPS row
        if (FormInputs.AddFloat("FPS", ref s.FrameRate, 1, 120, 0.1f, true, false,
                                "Frames per second of the output. 60 is a good default; 24–30 for film-like motion.",
                                RenderSettings.Defaults.FrameRate))
        {
            modified = true;
            if (s.TimeReference == RenderSettings.TimeReferences.Frames)
            {
                s.StartInBars = (float)RenderTiming.ConvertFps(s.StartInBars, _uiState.LastValidFps, s.FrameRate);
                s.EndInBars = (float)RenderTiming.ConvertFps(s.EndInBars, _uiState.LastValidFps, s.FrameRate);
            }
            _uiState.LastValidFps = s.FrameRate;
        }

        // Resolution row — realtime grab captures the live texture as-is, so the scale factor doesn't apply.
        if (isContinuous && s.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime)
        {
            ImGui.BeginDisabled();
            var ignored = 1f;
            FormInputs.AddFloat("Resolution Scale", ref ignored, 0.01f, 10f, 0.01f, true, true, null, 1f);
            ImGui.EndDisabled();
            FormInputs.AddHint("Realtime capture uses the native output resolution.");
        }
        else
        {
            modified |= FormInputs.AddFloat("Resolution Scale", ref s.ResolutionFactor, 0.01f, 10f, 0.01f, true, true,
                                             "Multiplies the output resolution. 1 = native size; 2 = double (e.g. 4K from a 1080p comp).",
                                             RenderSettings.Defaults.ResolutionFactor);
        }

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


    // The Continuous-range options: which clock drives the capture, and (reserved) its frame-rate mode.
    private static bool DrawContinuousOptions(RenderSettings s)
    {
        FormInputs.AddVerticalSpace(5);

        var modified = FormInputs.AddSegmentedButtonWithLabel(ref s.ContinuousClock, "Clock", 0,
                                                             """
                                                             - **Realtime** — grabs the live output as you perform (video only, native resolution).
                                                             - **Deterministic** — steps time forward at the target FPS; frame-perfect and records audio.
                                                             """);
        FormInputs.AddHint(s.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime
                               ? "Grabs the live output as you perform; press capture again to stop. (Video only — no audio yet.)"
                               : "Advances time at the target FPS until you stop. Frame-perfect, but not realtime.");

        // Realtime capture only keeps pace with a hardware encoder — nudge toward NVENC/Quick Sync/AMF when the
        // selected video codec would fall back to software.
        if (s.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime
            && s.RenderMode == RenderSettings.RenderModes.Video
            && VideoEncoderAvailabilityCache.Get(s.VideoCodec) is { Kind: not VideoEncoderKind.Hardware })
        {
            DrawInlineEncoderHint(Icon.Tip, UiColors.StatusAttention,
                                  "Tip: pick a hardware (NVENC) codec — H.264 or HEVC — in Format & Quality for smooth realtime capture.");
        }

        // Frame-rate mode — only Fixed FPS is implemented; Variable (VFR) is reserved for a later version.
        ImGui.BeginDisabled();
        var frameRateMode = RenderSettings.ContinuousFrameRateMode.FixedFps;
        FormInputs.AddEnumDropdown(ref frameRateMode, "Frame Rate", null, RenderSettings.Defaults.ContinuousFrameRate,
                                   m => m == RenderSettings.ContinuousFrameRateMode.FixedFps ? "Fixed FPS" : "Variable (VFR)");
        ImGui.EndDisabled();
        FormInputs.AddHint("Fixed FPS. Variable (VFR) frame rate is coming soon.");

        return modified;
    }

    // ProRes is profile-based and FFV1 is lossless, so a target bitrate is meaningless for them.
    private static bool CodecUsesBitrate(VideoExportCodec codec)
        => codec is VideoExportCodec.H264 or VideoExportCodec.Hevc or VideoExportCodec.VP9 or VideoExportCodec.AV1;

    private static bool IsHapCodec(VideoExportCodec codec)
        => codec is VideoExportCodec.Hap or VideoExportCodec.HapAlpha or VideoExportCodec.HapQ;

    // Muted "→ ~95 MB, ~30 min" drawn after each codec dropdown item (rough estimate; empty when not yet renderable).
    private static string DropdownEstimateSuffix(VideoExportCodec codec, Int2 res, int frames, double durationSec, long bitRate, int motionBlurSamples)
    {
        if (frames <= 0 || res.Width <= 0)
            return string.Empty;

        var bytes = RenderExportEstimate.EstimateBytes(codec, res, frames, durationSec, bitRate);
        var seconds = RenderExportEstimate.EstimateSeconds(codec, res, frames, motionBlurSamples);
        return $"→ ~{RenderExportEstimate.FormatBytes(bytes)}, {RenderExportEstimate.FormatDuration(seconds)}";
    }

    // Warn when the target drive has less than 1 GB, or less than 2× the estimated output — renders can be huge.
    private static void DrawDiskSpaceWarning(string directory, VideoExportCodec codec, Int2 res, int frames, double durationSec, long bitRate)
    {
        if (frames <= 0 || string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
                return;

            var free = new DriveInfo(root).AvailableFreeSpace;
            var bytes = RenderExportEstimate.EstimateBytes(codec, res, frames, durationSec, bitRate);
            if (free >= Math.Max(1_000_000_000L, 2 * bytes))
                return;

            FormInputs.AddVerticalSpace(5);
            FormInputs.ApplyIndent();
            Icon.Warning.DrawAtCursor(UiColors.StatusAttention);
            ImGui.SameLine();
            CustomComponents.StylizedText($"Low disk space — only {RenderExportEstimate.FormatBytes(free)} free.",
                                          Fonts.FontSmall, UiColors.StatusAttention);
        }
        catch
        {
            // DriveInfo can throw for UNC / removed drives — a missing warning is harmless.
        }
    }

    // Friendly dropdown labels — the raw enum names ("HapAlpha", "H264") read poorly.
    private static string CodecDisplayName(VideoExportCodec codec) => codec switch
                                                                          {
                                                                              VideoExportCodec.H264 => "H.264",
                                                                              VideoExportCodec.Hevc => "HEVC (H.265)",
                                                                              VideoExportCodec.ProRes => "ProRes",
                                                                              VideoExportCodec.VP9 => "VP9",
                                                                              VideoExportCodec.AV1 => "AV1",
                                                                              VideoExportCodec.FFV1 => "FFV1",
                                                                              VideoExportCodec.Hap => "Hap",
                                                                              VideoExportCodec.HapAlpha => "Hap Alpha",
                                                                              VideoExportCodec.HapQ => "Hap Q",
                                                                              _ => codec.ToString(),
                                                                          };

    // Bytes per pixel of the uncompressed DXT data each HAP variant produces (Snappy then compresses it a bit).
    private static double HapBytesPerPixel(VideoExportCodec codec) => codec switch
                                                                          {
                                                                              VideoExportCodec.Hap => 0.5,      // DXT1 (RGB)
                                                                              VideoExportCodec.HapAlpha => 1.0, // DXT5 (RGBA)
                                                                              VideoExportCodec.HapQ => 1.0,     // scaled DXT5-YCoCg
                                                                              _ => 0,
                                                                          };

    // Inline indicator under the codec dropdown: hardware-accelerated, in-process software, or (rarely) an
    // encoder this FFmpeg build lacks. Keeps a constant footprint so switching codecs doesn't shift the layout.
    private static void DrawCodecAvailabilityHint(VideoExportCodec codec)
    {
        var availability = VideoEncoderAvailabilityCache.Get(codec);
        if (availability == null)
        {
            DrawInlineEncoderHint(Icon.Tip, UiColors.TextMuted, "Checking encoder…");
            return;
        }

        switch (availability.Value.Kind)
        {
            case VideoEncoderKind.Hardware:
                DrawInlineEncoderHint(Icon.Checkmark, UiColors.TextMuted, $"Hardware encoder ({availability.Value.EncoderName})");
                break;
            case VideoEncoderKind.Software:
                DrawInlineEncoderHint(Icon.Checkmark, UiColors.TextMuted, "Software encoder");
                break;
            case VideoEncoderKind.Unavailable:
            default:
                DrawInlineEncoderHint(Icon.Warning, UiColors.StatusAttention, "This codec can't be encoded in this build.");
                break;
        }
    }

    private static void DrawInlineEncoderHint(Icon icon, Color color, string text)
    {
        FormInputs.AddVerticalSpace(5);
        FormInputs.ApplyIndent();
        icon.DrawAtCursor(color);
        ImGui.SameLine();
        CustomComponents.StylizedText(text, Fonts.FontSmall, color);
    }

    private bool DrawFormatSection()
    {
        var s = RenderSettings.Current;
        var modified = FormInputs.AddSegmentedButtonWithLabel(ref s.RenderMode, "Render Mode", 0,
                                                             "Render one video file, or a numbered sequence of image files (PNG/JPG).");
        FormInputs.AddVerticalSpace();

        if (s.RenderMode == RenderSettings.RenderModes.Video)
            modified |= DrawVideoFormatSettings();
        else
            modified |= FormInputs.AddEnumDropdown(ref s.FileFormat, "Format",
                                                   "Image file type. PNG is lossless (larger); JPG is much smaller (no alpha).",
                                                   RenderSettings.Defaults.FileFormat);

        return modified;
    }

    private bool DrawOutputSection()
    {
        return RenderSettings.Current.RenderMode == RenderSettings.RenderModes.Video
                   ? DrawVideoOutputSettings()
                   : DrawImageSequenceOutputSettings();
    }

    private bool DrawVideoFormatSettings()
    {
        var modified = false;
        var s = RenderSettings.Current;

        // Codec / container — each option also shows a rough size/time estimate (e.g. "VP9   ~95 MB, ~30 min").
        RenderProcess.TryGetRenderResolution(s, out var estRes);
        var estFrames = RenderTiming.ComputeFrameCount(s);
        var estDuration = Math.Max(0, RenderTiming.ReferenceTimeToSeconds(s.EndInBars, s.TimeReference, s.FrameRate)
                                      - RenderTiming.ReferenceTimeToSeconds(s.StartInBars, s.TimeReference, s.FrameRate));
        var estBitrate = (long)s.Bitrate;
        var estSamples = s.OverrideMotionBlurSamples;

        modified |= FormInputs.AddEnumDropdown(ref s.VideoCodec, "Codec",
                                               """
                                               - **H.264** (.mp4) — broadly compatible, hardware-accelerated.
                                               - **HEVC / H.265** (.mp4) — more efficient than H.264; hardware-accelerated where available.
                                               - **ProRes** (.mov) — high-quality all-intra editing codec.
                                               - **VP9 / AV1** (.mp4) — efficient delivery codecs; software-encoded (slower).
                                               - **FFV1** (.mkv) — lossless archival (very large files).
                                               - **HAP / HAP Alpha / HAP Q** (.mov) — GPU-friendly intra codecs for realtime/VJ playback.
                                               """,
                                               RenderSettings.Defaults.VideoCodec,
                                               codec => CodecDisplayName(codec),
                                               codec => DropdownEstimateSuffix(codec, estRes, estFrames, estDuration, estBitrate, estSamples));

        DrawCodecAvailabilityHint(s.VideoCodec);

        // Continuous capture leans on a hardware encoder to keep realtime pace; warn (but allow) otherwise.
        if (s.TimeRange == RenderSettings.TimeRanges.Continuous
            && VideoEncoderAvailabilityCache.Get(s.VideoCodec) is { Kind: not VideoEncoderKind.Hardware })
        {
            DrawInlineEncoderHint(Icon.Warning, UiColors.StatusAttention,
                                  "No hardware encoder — continuous capture may drop or duplicate frames.");
        }

        // Bitrate applies to the rate-controlled codecs only — ProRes (profile-based) and FFV1 (lossless) ignore it.
        if (CodecUsesBitrate(s.VideoCodec))
        {
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
        }
        else if (IsHapCodec(s.VideoCodec))
        {
            // HAP is a fixed-ratio DXT codec, so its size is predictable from pixels × frames (Snappy then
            // shaves a little). Use the ×4-rounded dimensions the encoder actually writes.
            RenderProcess.TryGetRenderResolution(s, out var hapResolution);
            var (hapW, hapH) = s.VideoCodec.RoundToEncoderBlock(hapResolution.Width, hapResolution.Height);
            var hapFrameCount = RenderTiming.ComputeFrameCount(s);
            var hapMb = HapBytesPerPixel(s.VideoCodec) * hapW * hapH * hapFrameCount / 1024 / 1024;
            var hapSize = hapMb >= 1024 ? $"{hapMb / 1024:0.##} GB" : $"{hapMb:0.#} MB";
            FormInputs.AddHint($"Est. {hapSize} ({hapW}×{hapH}, DXT before Snappy)");
        }

        // Realtime continuous capture has no synced-audio path yet, so the option is disabled in that mode.
        if (s.TimeRange == RenderSettings.TimeRanges.Continuous
            && s.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime)
        {
            ImGui.BeginDisabled();
            var noAudio = false;
            FormInputs.AddCheckBox("Export Audio (experimental)", ref noAudio, null, false);
            ImGui.EndDisabled();
            FormInputs.AddHint("Audio capture isn't available in realtime mode yet.");
        }
        else
        {
            modified |= FormInputs.AddCheckBox("Export Audio (experimental)", ref s.ExportAudio,
                                               "Include the project's soundtrack as an audio track (AAC).",
                                               RenderSettings.Defaults.ExportAudio);
        }

        return modified;
    }

    private bool DrawVideoOutputSettings()
    {
        var modified = false;
        var s = RenderSettings.Current;

        var currentPath = s.VideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        modified |= FormInputs.AddFilePicker("Folder", ref directory!, ".\\Render", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Filename", ref filename, null, null,
                                      "Name of the output file. The extension (.mp4 / .mov / .mkv) is set automatically from the codec."))
        {
            modified = true;
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
        }

        // Keep the filename's extension in sync with the chosen codec's container.
        var videoExtension = s.VideoCodec.GetFileExtension();
        if (filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
            || filename.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            filename = filename[..^4];
        }

        if (!filename.EndsWith(videoExtension, StringComparison.OrdinalIgnoreCase))
            filename += videoExtension;

        s.VideoFilePath = Path.Combine(directory, filename);

        RenderProcess.TryGetRenderResolution(s, out var estRes);
        var estFrames = RenderTiming.ComputeFrameCount(s);
        var estDuration = Math.Max(0, RenderTiming.ReferenceTimeToSeconds(s.EndInBars, s.TimeReference, s.FrameRate)
                                      - RenderTiming.ReferenceTimeToSeconds(s.StartInBars, s.TimeReference, s.FrameRate));
        DrawDiskSpaceWarning(directory, s.VideoCodec, estRes, estFrames, estDuration, s.Bitrate);

        modified |= FormInputs.AddCheckBox("Auto-increment version", ref s.AutoIncrementVersionNumber,
                                           "After each render, bump a version number (v01 → v02) so you don't overwrite previous takes.",
                                           RenderSettings.Defaults.AutoIncrementVersionNumber);
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

        return modified;
    }

    private bool DrawImageSequenceOutputSettings()
    {
        var modified = false;
        var s = RenderSettings.Current;

        modified |= FormInputs.AddFilePicker("Folder", ref s.SequenceFilePath!, ".\\ImageSequence ", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

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

    public string GetCachedTargetFilePath(RenderSettings.RenderModes mode)
    {
        var now = Playback.RunTimeInSecs;
        if (now - _uiState.LastPathUpdateTime < 0.2 && !string.IsNullOrEmpty(_uiState.CachedTargetPath))
            return _uiState.CachedTargetPath;

        _uiState.CachedTargetPath = RenderPaths.GetTargetFilePath(mode);
        _uiState.LastPathUpdateTime = now;
        return _uiState.CachedTargetPath;
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

            // Block a codec this FFmpeg build genuinely has no encoder for, so export doesn't silently fall
            // back to another codec. (Rare — the bundled build covers every dropdown codec; H.264 always has a
            // path: hardware, OpenH264, or MPEG-4.)
            var codec = RenderSettings.Current.VideoCodec;
            if (VideoEncoderAvailabilityCache.Get(codec) is { Kind: VideoEncoderKind.Unavailable })
            {
                errorMessage = $"The {codec} encoder isn't available in this FFmpeg build.";
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
        public Sections ActiveSection = Sections.Source;
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
