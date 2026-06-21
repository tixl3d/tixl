#nullable enable
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Settings;
using T3.Core.Video;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Per-project settings window with categories for Playback, Audio, Rendering, IO, and Performance.
/// </summary>
/// <remarks>
/// Controlling the primary soundtrack is finicky:
/// - "Add soundtrack" adds a <see cref="AudioClipResourceHandle"/> with empty filepath.
/// - When modifying the path we use to resolve the path (i.e. verify if file exists) before setting the filepath.
/// - If valid and set, <see cref="AudioEngine"/> will then load them in CompleteFrame.
/// </remarks>
internal sealed class ProjectSettingsWindow : Window
{
    internal ProjectSettingsWindow()
    {
        Config.Title = "Composition Settings";
    }

    internal override List<Window> GetInstances() => [];

    protected override void DrawContent()
    {
        var composition = ProjectView.Focused?.CompositionInstance;

        if (composition == null)
        {
            CustomComponents.EmptyWindowMessage("no composition active");
            return;
        }

        NavigationSidebar.BeginLayout();

        PlaybackUtils.FindCompositionSettingsForInstance(composition, out var compositionWithSettings,
            out var settings);

        var isEnabledForCurrent = compositionWithSettings == composition && settings is {Enabled: true};
        
        // Main toggle with composition name
        {
            FormInputs.SetIndent(0);
            FormInputs.AddVerticalSpace();
            
            if (FormInputs.AddCheckBox("Specify settings for ", ref isEnabledForCurrent))
            {
                if (isEnabledForCurrent)
                {
                    settings = composition.Symbol.CompositionSettings;
                    if (settings == null)
                    {
                        settings = new CompositionSettings();
                        composition.Symbol.CompositionSettings = settings;
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

                composition.Symbol.GetSymbolUi().FlagAsModified();
            }

            ImGui.SameLine();
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(composition.Symbol.Name);
            ImGui.PopFont();

            // Explanation hint
            var hint = "";
            if (isEnabledForCurrent)
            {
                hint = "These symbol settings are also used for its child operators.";
            }
            else if (compositionWithSettings != null && compositionWithSettings != composition)
            {
                hint = $"Currently inheriting settings from {compositionWithSettings.Symbol.Name}";
            }

            FormInputs.AddVerticalSpace(4);
            ImGui.SetCursorPosX( ImGui.GetCursorPosX() + 20 * T3Ui.UiScaleFactor);
            CustomComponents
                .HelpText(hint);

            FormInputs.AddVerticalSpace();
        }

        ImGui.Separator();

        if (isEnabledForCurrent)
        {
            DrawSettingsPanels(composition, settings, compositionWithSettings);
        }
        else
        {
            CustomComponents.EmptyWindowMessage("No settings");
            FormInputs.SetIndentToParameters();
        }
    }

    private enum Categories
    {
        Playback,
        Audio,
        Proxies,
        Recording,
        Executable,
    }

    private static Categories _activeCategory;

    private void DrawSettingsPanels(Instance composition, CompositionSettings settings,
        Instance? compositionWithSettings)
    {
        NavigationSidebar.BeginColumn("categories", 120);
        {
            foreach (var category in Enum.GetValues<Categories>())
            {
                var name = CustomComponents.HumanReadablePascalCase(Enum.GetName(category));
                if (NavigationSidebar.Item(name, _activeCategory == category))
                    _activeCategory = category;
            }
        }
        NavigationSidebar.EndColumn();

        NavigationSidebar.BeginContentPanel(PanelTitle(_activeCategory), this);
        {
            FormInputs.SetIndentToParameters();
            var modified = false;
            switch (_activeCategory)
            {
                case Categories.Playback:
                    modified |= DrawPlaybackSettings(composition, settings, compositionWithSettings);
                    break;
                case Categories.Audio:
                    modified |= DrawAudioSettings(settings);
                    break;
                case Categories.Proxies:
                    modified |= DrawProxySettings(composition, settings);
                    break;
                case Categories.Recording:
                    modified |= DrawRecordingSettings(composition);
                    break;
                case Categories.Executable:
                    modified |= DrawRenderingSettings(settings);
                    break;
            }

            if (modified)
                composition.Symbol.GetSymbolUi().FlagAsModified();
        }
        NavigationSidebar.EndContentPanel();
    }

    private static string PanelTitle(Categories category) => category switch
                                                                 {
                                                                     Categories.Playback   => "Playback",
                                                                     Categories.Audio      => "Audio Mix",
                                                                     Categories.Proxies    => "Video Proxies",
                                                                     Categories.Recording  => "Recording",
                                                                     Categories.Executable => "Export",
                                                                     _                     => string.Empty,
                                                                 };

    #region Category panels

    private static bool DrawAudioSettings(CompositionSettings settings)
    {
        var modified = false;
        var audio = settings.Audio;
        var defaults = CompositionSettings.Defaults.Audio;

        modified |= FormInputs.AddFloat("Soundtrack Volume",
            ref audio.SoundtrackVolume, 0f, 10f, 0.01f, true, true,
            "Volume level for the project soundtrack.",
            defaults.SoundtrackVolume);

        modified |= FormInputs.AddCheckBox("Mute Soundtrack",
            ref audio.SoundtrackMute,
            "Mute the soundtrack audio.",
            defaults.SoundtrackMute);

        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddFloat("Operator Volume",
            ref audio.OperatorVolume, 0f, 1f, 0.01f, true, true,
            "Volume level for operator-generated audio.",
            defaults.OperatorVolume);

        modified |= FormInputs.AddCheckBox("Mute Operators",
            ref audio.OperatorMute,
            "Mute all operator audio output.",
            defaults.OperatorMute);

        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddFloat("Resync Threshold",
            ref audio.AudioResyncThreshold, 0.001f, 0.1f, 0.001f, true, true,
            "If audio playback drifts too far from the animation it will be resynced. A normal range is between 0.02s and 0.05s.",
            defaults.AudioResyncThreshold);

        return modified;
    }

    private static bool DrawProxySettings(Instance composition, CompositionSettings settings)
    {
        var modified = false;
        var proxy = settings.Proxy;
        var defaults = CompositionSettings.Defaults.Proxy;

        CustomComponents.HelpText("Proxies are downscaled, fast-seeking copies of video clips used for preview. "
                                  + "Rendering to file always uses the full-resolution source. Generate them from a "
                                  + "video operator's context menu.");
        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddCheckBox("Use proxies for preview",
            ref proxy.UseForPreview,
            "When a clip has a generated proxy beside it, play the proxy while previewing/scrubbing for faster seeking.",
            defaults.UseForPreview);

        FormInputs.AddVerticalSpace();

        // Restricted to all-intra / LGPL codecs — a proxy never uses H.264/HEVC (libx264/libx265 = GPL).
        modified |= FormInputs.AddDropdown(ref proxy.Format, ProxyFormats, "Proxy Format", ProxyFormatName,
            "All-intra codec used when generating proxies. ProRes is a balanced default; Hap variants scrub fastest at a larger file size.");

        modified |= FormInputs.AddFloat("Proxy Resolution",
            ref proxy.Resolution, 0.1f, 1f, 0.05f, true, true,
            "Proxy size as a fraction of the source resolution (0.5 = half-size). Smaller seeks faster and uses less disk.",
            defaults.Resolution);

        // Per-machine guard (not project data) — saved to UserSettings directly so it doesn't dirty the symbol.
        if (FormInputs.AddFloat("Min. free disk (GB)",
                ref UserSettings.Config.ProxyMinFreeDiskGb, 0f, 500f, 1f, true, true,
                "Generation is blocked when the source's drive has less than this much free space. Per-machine, not part of the project.",
                UserSettings.Defaults.ProxyMinFreeDiskGb))
        {
            UserSettings.Save();
        }

        DrawActiveProxyJobs();
        DrawProxyStorage(composition);

        return modified;
    }

    // Generated proxies pile up next to their sources; surface how much disk they use and offer a one-click cleanup.
    // The directory scan is cached and only re-runs when the file watcher reports a change (a proxy was written or
    // deleted) or the active project changed — so the panel stays allocation-free per frame.
    private static void DrawProxyStorage(Instance composition)
    {
        var projectFolder = composition.Symbol.SymbolPackage.AssetsFolder;
        EnsureProxyScan(projectFolder);

        FormInputs.AddVerticalSpace();
        FormInputs.AddSectionSubHeader("Storage");

        DrawStorageRow(_thisProjectProxyLabel, _thisProjectProxyTooltip, _thisProjectProxyFiles, "##deleteThisProjectProxies");
        FormInputs.AddVerticalSpace(5);
        DrawStorageRow(_allProjectsProxyLabel, _allProjectsProxyTooltip, _allProjectsProxyFiles, "##deleteAllProxies");
    }

    private static void DrawStorageRow(string label, string fileBreakdown, List<string> files, string popupId)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        if (files.Count == 0)
            return;

        FormInputs.AppendTooltip(fileBreakdown); // (i) marker on the same line, hover lists the files
        FormInputs.AddVerticalSpace(2);

        if (ImGui.SmallButton($"Delete{popupId}"))
            ImGui.OpenPopup(popupId);

        if (ImGui.BeginPopup(popupId))
        {
            ImGui.TextUnformatted($"Delete {files.Count} proxy file(s)? Sources are untouched and proxies can be regenerated.");
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            if (ImGui.Button($"Delete{popupId}confirm"))
            {
                DeleteProxyFiles(files);
                ImGui.CloseCurrentPopup();
            }

            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button($"Cancel{popupId}"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private static void DeleteProxyFiles(List<string> files)
    {
        int deleted = 0, failed = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception e)
            {
                failed++;
                Log.Warning($"Couldn't delete proxy '{file}': {e.Message}");
            }
        }

        var inUseHint = failed > 0 ? $" {failed} could not be removed (in use? stop playback and retry)." : string.Empty;
        Log.Info($"Deleted {deleted} proxy file(s).{inUseHint}");
        _proxyScanWatcherState = -1; // force a rescan on the next draw
    }

    // Walks every writable project's asset folder for proxy files, caching totals + paths until the next change.
    private static void EnsureProxyScan(string currentProjectFolder)
    {
        var watcherState = ResourceFileWatcher.FileStateChangeCounter;
        if (watcherState == _proxyScanWatcherState && currentProjectFolder == _scannedProjectFolder)
            return;

        _proxyScanWatcherState = watcherState;
        _scannedProjectFolder = currentProjectFolder;
        _thisProjectProxyFiles.Clear();
        _allProjectsProxyFiles.Clear();
        long thisBytes = 0, allBytes = 0;
        var thisBreakdown = new StringBuilder();
        var allBreakdown = new StringBuilder();

        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package.IsReadOnly) // shipped/read-only packages never hold user-generated proxies
                continue;

            var folder = package.AssetsFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                continue;

            var isCurrent = string.Equals(folder, currentProjectFolder, StringComparison.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.mov", SearchOption.AllDirectories))
                {
                    if (!file.EndsWith(VideoPlayback.ProxySuffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    long size;
                    try
                    {
                        size = new FileInfo(file).Length;
                    }
                    catch
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(file);
                    _allProjectsProxyFiles.Add(file);
                    allBytes += size;
                    AppendBreakdownLine(allBreakdown, $"{package.Name}: {fileName} — {FormatBytes(size)}", _allProjectsProxyFiles.Count);
                    if (isCurrent)
                    {
                        _thisProjectProxyFiles.Add(file);
                        thisBytes += size;
                        AppendBreakdownLine(thisBreakdown, $"{fileName} — {FormatBytes(size)}", _thisProjectProxyFiles.Count);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't scan proxies in '{folder}': {e.Message}");
            }
        }

        _thisProjectProxyLabel = $"This project: {FormatBytes(thisBytes)} in {_thisProjectProxyFiles.Count} proxy file(s).";
        _allProjectsProxyLabel = $"All projects: {FormatBytes(allBytes)} in {_allProjectsProxyFiles.Count} proxy file(s).";
        _thisProjectProxyTooltip = thisBreakdown.ToString();
        _allProjectsProxyTooltip = allBreakdown.ToString();
    }

    // Keeps the hover breakdown bounded so a project with hundreds of proxies doesn't produce a screen-tall tooltip.
    private static void AppendBreakdownLine(StringBuilder breakdown, string line, int indexSoFar)
    {
        const int maxLines = 40;
        if (indexSoFar < maxLines)
        {
            if (breakdown.Length > 0)
                breakdown.Append('\n');

            breakdown.Append(line);
        }
        else if (indexSoFar == maxLines)
        {
            breakdown.Append("\n…");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        // _byteUnits[0] is KB, so the index trails the division count by one: after the first ÷1024 the value is
        // in KB (unit 0), after the second it's MB (unit 1), etc.
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < _byteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {_byteUnits[unit]}";
    }

    // Live feedback for the background generation queue: anything currently queued / encoding, plus failures
    // (e.g. low disk). Done jobs are omitted — they're silent successes.
    private static void DrawActiveProxyJobs()
    {
        var shownHeader = false;
        foreach (var (path, status) in ProxyGenerationService.Jobs)
        {
            var line = status.State switch
                           {
                               ProxyGenerationService.JobState.Queued     => $"Queued — {Path.GetFileName(path)}",
                               ProxyGenerationService.JobState.Generating => $"Generating {Path.GetFileName(path)} — {status.Progress * 100:0}%",
                               ProxyGenerationService.JobState.Failed     => $"Failed {Path.GetFileName(path)} — {status.Error}",
                               _                                          => null,
                           };
            if (line == null)
                continue;

            if (!shownHeader)
            {
                FormInputs.AddVerticalSpace();
                FormInputs.AddSectionSubHeader("Generation");
                shownHeader = true;
            }

            var color = status.State == ProxyGenerationService.JobState.Failed ? UiColors.StatusError : UiColors.TextMuted;
            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
            ImGui.TextWrapped(line);
            ImGui.PopStyleColor();
        }
    }

    private static readonly VideoExportCodec[] ProxyFormats =
        [VideoExportCodec.ProRes, VideoExportCodec.Hap, VideoExportCodec.HapAlpha, VideoExportCodec.HapQ];

    private static string ProxyFormatName(VideoExportCodec codec) => codec switch
                                                                         {
                                                                             VideoExportCodec.ProRes   => "ProRes",
                                                                             VideoExportCodec.Hap      => "Hap",
                                                                             VideoExportCodec.HapAlpha => "Hap Alpha",
                                                                             VideoExportCodec.HapQ     => "Hap Q",
                                                                             _                         => codec.ToString(),
                                                                         };

    /// <summary>
    /// Per-composition toggles for the live recording feature: what the Record button on
    /// the timeline toolbar should actually capture. Lives on
    /// <see cref="T3.Editor.UiModel.SymbolUi.RecordingSettings"/> (Editor-only — Core has
    /// no consumer for these flags). Defaults to capturing everything so compositions
    /// that never visited this panel keep working unchanged. The settings instance is
    /// lazily created on first edit so .t3ui files for never-touched compositions stay
    /// clean.
    /// </summary>
    private static bool DrawRecordingSettings(Instance composition)
    {
        var modified = false;
        var symbolUi = composition.GetSymbolUi();
        var recording = symbolUi.RecordingSettings ??= new RecordingSettings();
        var defaults = RecordingSettings.Defaults;
        CustomComponents.HelpText("Which sources the timeline Record button captures.");
        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddCheckBox("Capture Audio",
                                           ref recording.CaptureAudio,
                                           "Record the active WASAPI input device while the session is running. The device is configured under Playback → Audio Source.",
                                           defaults.CaptureAudio);

        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddCheckBox("Capture IO",
                                           ref recording.CaptureIo,
                                           "Record MIDI and / or OSC events into a .data file alongside the audio clip.",
                                           defaults.CaptureIo);

        // Indent the IO sub-flags so the visual hierarchy matches "MIDI / OSC are children
        // of IO". Skip the sub-rows entirely when the parent is off — they're inert and
        // would just clutter the panel.
        if (recording.CaptureIo)
        {
            FormInputs.AddVerticalSpace();
            FormInputs.Indent(20 * T3Ui.UiScaleFactor);
            modified |= FormInputs.AddCheckBox("MIDI",
                                               ref recording.CaptureMidi,
                                               "Subscribe to MIDI events for this recording.",
                                               defaults.CaptureMidi);
            modified |= FormInputs.AddCheckBox("OSC",
                                               ref recording.CaptureOsc, 
                                               "Subscribe to OSC events on the configured DefaultOscPort for this recording.",
                                               defaults.CaptureOsc);
            FormInputs.Unindent(20 * T3Ui.UiScaleFactor);
            
            if (!recording.CaptureMidi && !recording.CaptureOsc)
            {
                CustomComponents.HelpText("Both MIDI and OSC are off — Capture IO has nothing to record.");
            }
        }

        return modified;
    }

    private static bool DrawRenderingSettings(CompositionSettings settings)
    {
        var modified = false;
        var export = settings.Export;
        var defaults = CompositionSettings.Defaults.Export;

        CustomComponents.HelpText("These settings apply when exporting as executable.");
        FormInputs.AddVerticalSpace();

        modified |= FormInputs.AddEnumDropdown(ref export.DefaultWindowMode,
            "Window Mode",
            "The default window mode when running the exported executable.",
            defaults.DefaultWindowMode);

        modified |= FormInputs.AddCheckBox("Enable Playback Control",
            ref export.EnablePlaybackControlWithKeyboard,
            "Users can use cursor left/right to skip through time\nand space key to pause playback\nof exported executable.",
            defaults.EnablePlaybackControlWithKeyboard);

        return modified;
    }


    #endregion

    #region Playback settings

    private static bool DrawPlaybackSettings(Instance composition, CompositionSettings settings,
        Instance? compositionWithSettings)
    {
        var modified = false;

        var playback = settings.Playback;

        if (FormInputs.AddSegmentedButtonWithLabel(ref playback.AudioSource, "Audio Source"))
        {
            modified = true;
            UpdatePlaybackAndTimeline(settings);
        }

        FormInputs.AddVerticalSpace();
        ImGui.Separator();
        FormInputs.AddVerticalSpace();

        switch (playback.AudioSource)
        {
            case CompositionSettings.AudioSources.ProjectSoundTrack:
            {
                if (!settings.TryGetMainSoundtrack(compositionWithSettings, out var soundtrackHandle))
                {
                    if (ImGui.Button("Add soundtrack to composition"))
                    {
                        modified = true;
                        playback.AudioClips.Add(new TimelineAudioClip()
                        {
                            IsMainSoundtrack = true,
                        });
                        _tempSoundtrackFilepathForEdit = string.Empty;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(soundtrackHandle.Clip.AssetPath))
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
                                _tempSoundtrackFilepathForEdit = soundtrackHandle.Clip.AssetPath;
                            }
                        }
                        else
                        {
                            Log.Warning($"Removing invalid soundtrack file: {soundtrackHandle.Clip.AssetPath}");
                            soundtrackHandle.Clip.AssetPath = string.Empty;
                            modified = true;
                        }
                    }

                    var editResult = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File,
                        AudioFileFilter,
                        ref _tempSoundtrackFilepathForEdit,
                        showAssetFolderToggle: false);

                    var filepathModified = (editResult & InputEditStateFlags.Modified) != 0;
                    if (filepathModified)
                    {
                        // Push the picker's value back onto the clip. TryToApplyFilePath validates
                        // via AssetRegistry and clears AssetPath if the path doesn't resolve.
                        if (!string.IsNullOrEmpty(_tempSoundtrackFilepathForEdit))
                        {
                            if (compositionWithSettings != null)
                                soundtrackHandle.TryToApplyFilePath(_tempSoundtrackFilepathForEdit, compositionWithSettings);
                        }
                        else
                        {
                            soundtrackHandle.Clip.AssetPath = string.Empty;
                        }
                        modified = true;
                    }

                    FormInputs.ApplyIndent();
                    if (ImGui.Button("Reload"))
                    {
                        AudioEngine.ReloadSoundtrackClip(soundtrackHandle);
                        AudioImageFactory.ResetImageCache();
                        modified = true;
                        filepathModified = true;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Remove"))
                    {
                        playback.AudioClips.Remove(soundtrackHandle.Clip);
                        modified = true;
                    }

                    FormInputs.AddVerticalSpace();

                    var bpm = (float)playback.Bpm;
                    if (FormInputs.AddFloat("BPM",
                            ref bpm,
                            0,
                            1000,
                            0.02f,
                            true, true,
                            "In T3 animation units are in bars.\nThe BPM rate controls the animation speed of your project.",
                            120))
                    {
                        Playback.Current.Bpm = bpm;
                        playback.Bpm = bpm;
                        modified = true;
                    }

                    // Offset is the clip's TimeRange.Start (bars on the timeline).
                    var soundtrackStart = soundtrackHandle.Clip.TimeRange.Start;
                    if (FormInputs.AddFloat("Offset",
                            ref soundtrackStart,
                            -100,
                            100,
                            0.02f,
                            false, true,
                            "Offsets the start of the soundtrack on the timeline, in bars.",
                            0))
                    {
                        soundtrackHandle.Clip.TimeRange.Start = soundtrackStart;
                        modified = true;
                    }

                    FormInputs.AddEnumDropdown(ref UserSettings.Config.TimeDisplayMode, "Display Timeline in");

                    modified |= FormInputs.AddFloat("Audio Decay", ref playback.AudioDecayFactor,
                        0.001f,
                        1f,
                        0.01f,
                        true, true,
                        "The decay factors controls the impact of [AudioReaction] when AttackMode. Good values strongly depend on style, loudness and variation of input signal.",
                        0.9f);

                    if (filepathModified)
                    {
                        composition.Symbol.GetSymbolUi().FlagAsModified();
                        AudioEngine.ReloadSoundtrackClip(soundtrackHandle);
                        UpdateBpmFromSoundtrackConfig(soundtrackHandle.Clip);
                        UpdatePlaybackAndTimeline(settings);
                    }
                }

                break;
            }
            case CompositionSettings.AudioSources.ExternalDevice:
            {
                FormInputs.AddVerticalSpace();

                if (FormInputs.AddSegmentedButtonWithLabel(ref playback.Syncing, "Sync Mode"))
                {
                    UpdatePlaybackAndTimeline(settings);
                    modified = true;
                }

                if (playback.Syncing == CompositionSettings.SyncModes.Tapping)
                {
                    FormInputs.SetIndentToParameters();
                    FormInputs.AddHint("""
                                       Tap the [Sync] button on every beat.
                                       The right click on measure to resync and refine.
                                       """);

                    modified |= FormInputs.AddCheckBox("Enable audio beat lock",
                        ref playback.EnableAudioBeatLocking,
                        """
                        If enabled, the editor will look for transient bass, hi-hats and snares and attempt to lock the playback speed onto the incoming audio signal.
                        To use this, start by tapping the base beat (e.g. with Z) then tap the beginning of the bar with (e.g. with X).
                        From now on, you will see the BPM be constantly sliding to lock onto the beat).
                        """,
                        true
                    );
                    FormInputs.AddVerticalSpace();
                }

                if (!playback.EnableAudioBeatLocking || playback.Syncing == CompositionSettings.SyncModes.Timeline)
                {
                    modified |= FormInputs.AddFloat("BPM",
                        ref playback.Bpm,
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
                    ref playback.BeatLockAudioOffsetSec,
                    -1f, 1f, 0.001f,
                    true, true,
                    """
                    When using beat lock through audio analysis, you can slightly offset the phase.

                    This might be useful to tighten the sync between audio and video, e.g. if the visual output is delayed by video-processing devices.
                    """,
                    0);

                FormInputs.AddVerticalSpace();

                modified |= FormInputs.AddFloat("Audio Gain", ref playback.AudioGainFactor, 0.01f, 100, 0.01f, true,
                    true,
                    "Can be used to adjust the input signal (e.g. in live situation where the input level might vary.",
                    1);

                modified |= FormInputs.AddFloat("Audio Decay", ref playback.AudioDecayFactor,
                    0.001f,
                    1f,
                    0.01f,
                    true, true,
                    "The decay factors controls the impact of [AudioReaction] when AttackMode. Good values strongly depend on style, loudness and variation of input signal.",
                    0.9f);

                // Input meter
                var level = playback.AudioGainFactor * WasapiAudioInput.DecayingAudioLevel * 0.03f;
                var normalizedLevel = level / 644f;
                FormInputs.DrawInputLabel("Input Level");
                var inputSize = FormInputs.GetAvailableInputSize(" ", true, true);
                var cursorScreenPos = ImGui.GetCursorScreenPos();
                AudioLevelMeter.DrawAbsoluteWithinBounds("", normalizedLevel, ref _smoothedLevel, 2f, cursorScreenPos.X,
                    cursorScreenPos.X + inputSize.X);

                FormInputs.DrawInputLabel("Input Device");
                ImGui.BeginGroup();

                modified |= AudioDeviceSelector.DrawProjectDeviceCombo("##SelectDevice", ref playback.AudioInputDeviceName);

                if (string.IsNullOrEmpty(playback.AudioInputDeviceName))
                {
                    // Project defers to the machine default; expose the local device so a portable
                    // project still resolves to a real input on this machine.
                    FormInputs.DrawInputLabel("Default Device");
                    AudioDeviceSelector.DrawLocalDefaultDeviceCombo("##SelectLocalDevice");
                    CustomComponents.HelpText("Stored per machine, not in the project. Set this once and shared projects work everywhere.");
                }
                else if (playback.AudioInputDeviceName != WasapiAudioInput.ActiveInputDeviceName)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
                    ImGui.TextUnformatted(playback.AudioInputDeviceName + " (NOT FOUND)");
                    ImGui.PopStyleColor();
                }

                ImGui.EndGroup();
                break;
            }
        }

        return modified;
    }

    #endregion

    #region Helpers

    private static void UpdatePlaybackAndTimeline(CompositionSettings settings)
    {
        var playback = settings.Playback;
        if (playback.AudioSource == CompositionSettings.AudioSources.ProjectSoundTrack)
        {
            Playback.Current = T3Ui.DefaultTimelinePlayback;

            if (playback.AudioClips.Count > 0)
            {
                // The settings loader already migrated any legacy per-clip BPM into playback.Bpm.
                Playback.Current.Bpm = playback.Bpm;
                if (Playback.Current.Settings != null)
                    Playback.Current.Settings.Playback.Syncing = CompositionSettings.SyncModes.Timeline;
            }

            UserSettings.Config.ShowTimeline = true;
        }
        else
        {
            if (playback.Syncing == CompositionSettings.SyncModes.Tapping)
            {
                Playback.Current = T3Ui.DefaultBeatTimingPlayback;
                UserSettings.Config.ShowTimeline = false;
                UserSettings.Config.EnableIdleMotion = true;
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

    private static void UpdateBpmFromSoundtrackConfig(TimelineAudioClip? audioClip)
    {
        if (audioClip == null || string.IsNullOrEmpty(audioClip.AssetPath))
        {
            Log.Error("Can't detected BPM-rate from empty undefined audio-clip filename");
            return;
        }

        var matchBpmPattern = new Regex(@"(\d+\.?\d*)bpm");
        var result = matchBpmPattern.Match(audioClip.AssetPath);
        if (!result.Success)
            return;

        if (float.TryParse(result.Groups[1].Value, out var bpm))
        {
            Log.Debug($"Using bpm-rate {bpm} from filename.");
            Playback.Current.Bpm = bpm;
        }
    }

    #endregion

    private static string? _tempSoundtrackFilepathForEdit = string.Empty;
    private static float _smoothedLevel;
    private const string AudioFileFilter = "mp3,wav,ogg";

    // Cached proxy-storage scan (see DrawProxyStorage / EnsureProxyScan).
    private static readonly string[] _byteUnits = ["KB", "MB", "GB", "TB"];
    private static int _proxyScanWatcherState = -1;
    private static string? _scannedProjectFolder;
    private static readonly List<string> _thisProjectProxyFiles = [];
    private static readonly List<string> _allProjectsProxyFiles = [];
    private static string _thisProjectProxyLabel = string.Empty;
    private static string _allProjectsProxyLabel = string.Empty;
    private static string _thisProjectProxyTooltip = string.Empty;
    private static string _allProjectsProxyTooltip = string.Empty;
}