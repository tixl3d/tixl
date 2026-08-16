#nullable enable

using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.IoServices;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Coordinates a paired audio + IO recording session. Owns the "is something being
/// captured right now" state, dispatches start / stop to the underlying
/// <see cref="WasapiAudioInput"/> and <see cref="IoDataSetRecorder"/>, and spawns the
/// destination clips on the timeline at start so the user sees the recording grow in
/// real time. The whole session lands as one <see cref="MacroCommand"/> on stop so it
/// undoes as a unit.
/// </summary>
/// <remarks>
/// <para>
/// Clip growth is wall-clock driven: each frame, <see cref="OnFrame"/> extends the
/// <c>TimeRange.End</c> on both the <c>LoadDataClip</c> and <c>AudioClip</c> ops by the
/// elapsed seconds since record-start (converted to
/// bars at the current BPM). The recordings themselves measure wall-clock time
/// regardless of playback scrubbing, so the clip's visual duration matches the file's
/// real duration.
/// </para>
/// <para>
/// The composition reference is captured by symbol Guid (not <see cref="Instance"/>),
/// so a graph hot-reload mid-session doesn't strand a stale pointer — every resolve
/// roundtrips through <see cref="SymbolUiRegistry"/>.
/// </para>
/// </remarks>
internal static class RecordingSession
{
    public static bool IsActive { get; private set; }

    /// <summary>
    /// During an active session, returns the in-progress <see cref="DataSet"/> being
    /// written by <see cref="IoDataSetRecorder"/> if the given SymbolChild is the one
    /// the session is targeting. Lets timeline UI (the clip-body renderer) show events
    /// streaming in live before the file is finalised on stop.
    /// </summary>
    public static bool TryGetLiveDataSet(Guid clipChildId, out T3.Core.DataTypes.DataSet.DataSet? dataSet)
    {
        dataSet = null;
        if (!IsActive || clipChildId != _activeDataClipChildId)
            return false;
        dataSet = IoDataSetRecorder.ActiveDataSet;
        return dataSet != null;
    }

    public static void Start(Instance compositionOp)
    {
        if (IsActive)
        {
            Log.Warning("RecordingSession.Start called while another session is already active.");
            return;
        }

        if (Playback.Current == null)
        {
            Log.Warning("RecordingSession.Start: no active Playback; can't anchor the recording.");
            return;
        }

        _compositionSymbolId = compositionOp.Symbol.Id;
        _recordStartBars = Playback.Current.TimeInBars;
        _recordStartRunSecs = Playback.RunTimeInSecs;

        // Capture what to record per the composition's recording settings (lives on the
        // composition's SymbolUi — see RecordingSettings.cs). Permissive defaults — a
        // composition that never touched the panel still grabs everything (RecordingSettings
        // has a Defaults instance with all flags on). CaptureMidi / CaptureOsc only have
        // an effect when CaptureIo is on; the IoDataSetRecorder no-ops the session if
        // both sub-flags would land it with nothing to subscribe to.
        var recording = compositionOp.GetSymbolUi().RecordingSettings ?? RecordingSettings.Defaults;
        var captureAudio = recording.CaptureAudio;
        var captureIo = recording.CaptureIo && (recording.CaptureMidi || recording.CaptureOsc);
        var captureMidi = recording.CaptureMidi;
        var captureOsc = recording.CaptureOsc;

        if (!captureAudio && !captureIo)
        {
            Log.Warning("RecordingSession.Start: nothing to record — both Audio and IO are disabled in this project's recording settings.");
            return;
        }

        var startBars = (float)_recordStartBars;
        var baseLayer = FindNextLayerIndex(compositionOp, startBars);
        var dataLayer = baseLayer;
        var audioLayer = baseLayer + 1;
        _lastBaseLayerIndex = baseLayer;

        _activeMacro = new MacroCommand("Live recording session");

        // LoadDataClip op — only created when the user actually wants IO recorded.
        // Same rationale: avoid an empty clip + op pair just because the user opted out.
        if (captureIo)
        {
            var addCmd = new AddSymbolChildCommand(compositionOp.Symbol, _loadDataClipSymbolId)
                             {
                                 PosOnCanvas = FindFreeCanvasPositionForClip(compositionOp.Symbol, _loadDataClipSymbolId),
                             };
            _activeMacro.AddAndExecCommand(addCmd);
            _activeDataClipChildId = addCmd.AddedChildId;
            InitClipTimeClip(compositionOp.Symbol, _activeDataClipChildId, startBars, dataLayer);
        }

        // AudioClip op — only created when audio capture is enabled. Symmetric with the
        // LoadDataClip op above: both are TimeClip-backed ops that grow during capture and
        // get their file path committed on stop.
        if (captureAudio)
        {
            // When IO is off and audio is the only capture, stash the audio on the
            // base layer instead of layer+1 — there's no data clip to pair with.
            var rowForAudio = captureIo ? audioLayer : dataLayer;
            var addAudioCmd = new AddSymbolChildCommand(compositionOp.Symbol, _audioClipSymbolId)
                                  {
                                      PosOnCanvas = FindFreeCanvasPositionForClip(compositionOp.Symbol, _audioClipSymbolId),
                                  };
            _activeMacro.AddAndExecCommand(addAudioCmd);
            _activeAudioClipChildId = addAudioCmd.AddedChildId;
            InitClipTimeClip(compositionOp.Symbol, _activeAudioClipChildId, startBars, rowForAudio);
        }

        // Recorders last — if a Begin fails, the clips already exist but stay zero-width
        // until Stop. The user sees something went wrong; undo cleanly removes both clips.
        _audioCaptureActive = captureAudio;
        _ioCaptureActive = captureIo;
        // One shared session index so a session's audio + data files line up (AudioRec-007 / DataRec-007).
        // Computed once here (before either recorder writes) from the persistent imported recordings in the
        // project's Assets — not the temp dir, whose copies are deleted after import (which would reset the
        // index to 001 every session).
        var sessionIndex = RecordingPaths.NextSessionIndex(NextIndexScanDirs(compositionOp));
        if (captureAudio)
            WasapiAudioInput.BeginRecording(sessionIndex);
        if (captureIo)
            IoDataSetRecorder.BeginRecording(sessionIndex, captureMidi: captureMidi, captureOsc: captureOsc);

        // Without this, the graph canvas keeps drawing its cached child list until the
        // user clicks on it — the new LoadDataClip op isn't visible until then.
        // FlagAsModified bumps the symbol-UI revision; FlagChanges notifies the open
        // ProjectView so it rebuilds its child renderable set.
        compositionOp.GetSymbolUi().FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);

        IsActive = true;
    }

    /// <summary>
    /// Called once per frame while a session is active. Extends both clips' TimeRange.End
    /// based on wall-clock elapsed time since Start, converted to bars at the current BPM.
    /// No-op when no session is active.
    /// </summary>
    /// <remarks>
    /// Auto-stops the session when the user pauses playback (Spacebar → PlaybackSpeed = 0).
    /// The mental model the user works with is "record while playing": running the recorders
    /// past a pause produces a clip whose TimeRange end doesn't match the audible content,
    /// and the audio buffer keeps growing in the background. Stopping at pause matches the
    /// "Start recording also starts playback" affordance on the Record button.
    /// </remarks>
    public static void OnFrame()
    {
        if (!IsActive)
            return;

        var playback = Playback.Current;
        if (playback == null)
            return;

        // Pause-stops-recording: if playback is paused, finalise the session here so the
        // user doesn't have to remember to click Record off. Same Stop() the toolbar runs,
        // so the file finalisation, asset import, and undo macro all flow normally.
        if (playback.PlaybackSpeed == 0)
        {
            Stop();
            return;
        }

        var elapsedSecs = Playback.RunTimeInSecs - _recordStartRunSecs;
        var elapsedBars = elapsedSecs * playback.Bpm / 240.0;
        var timelineEnd = (float)(_recordStartBars + elapsedBars);
        var sourceEnd = (float)elapsedBars;

        // Grow both clip ops' TimeClip output data in place. Animated dirty flag on each op's
        // output ensures the next Update picks up the new range. TimeRange extends in
        // timeline-bar space (startBars + elapsed); SourceRange extends in file-time-bar space
        // (elapsed since record-start). Keeping the two separate lets cut / start-handle trim
        // work correctly — see InitClipTimeClip.
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi))
        {
            GrowClipTimeRange(compositionUi.Symbol, _activeDataClipChildId, timelineEnd, sourceEnd);
            GrowClipTimeRange(compositionUi.Symbol, _activeAudioClipChildId, timelineEnd, sourceEnd);
        }
    }

    private static void GrowClipTimeRange(Symbol composition, Guid childId, float timelineEnd, float sourceEnd)
    {
        if (childId == Guid.Empty || !composition.Children.TryGetValue(childId, out var symbolChild))
            return;

        foreach (var output in symbolChild.Outputs.Values)
        {
            if (output.OutputData is TimeClip tc)
            {
                tc.TimeRange.End = timelineEnd;
                tc.SourceRange.End = sourceEnd;
                break;
            }
        }
    }

    public static void Stop()
    {
        if (!IsActive)
            return;

        IsActive = false;

        // Skip EndRecording on whichever side never started — the underlying recorders
        // log a warning when called without an active session, which would otherwise spam
        // the Console every time the user records with only one source selected.
        var audioPath = _audioCaptureActive ? WasapiAudioInput.EndRecording() : null;
        var dataPath = _ioCaptureActive ? IoDataSetRecorder.EndRecording() : null;
        _audioCaptureActive = false;
        _ioCaptureActive = false;

        // Final extend with one last frame's worth so the clip ends precisely where the
        // recorders did. The recorders' EndRecording closes the files synchronously, so
        // the elapsed time at this exact moment is the real duration.
        OnFrame();

        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi))
        {
            Log.Warning($"RecordingSession.Stop: composition symbol {_compositionSymbolId} no longer registered; clips left with empty paths.");
            ResetSessionState();
            return;
        }

        // Import the just-finalised files into the project's Assets folder so they land
        // under the active package, register with AssetRegistry, and show up in the
        // AssetLib UI. The temp-staging copy in the recordings dir is removed on success
        // (see TryImportRecording) so each session doesn't leave a duplicate behind.
        // Drop a silent take before import: capture ran but WASAPI delivered no frames (device held
        // exclusively by another app, muted input, or wrong device), so the WAV is a header-only file
        // with no samples. Importing it would litter the project's Assets and leave the clip pointing at
        // an unreadable file. Delete the temp file and leave the audio clip's path empty — the session is
        // one undo macro, so the leftover empty clip is a single Ctrl+Z away.
        if (!string.IsNullOrEmpty(audioPath) && IsSilentRecording(audioPath))
        {
            Log.Debug($"RecordingSession: discarding silent audio take '{audioPath}'.");
            try
            {
                System.IO.File.Delete(audioPath);
            }
            catch (Exception e)
            {
                Log.Debug($"RecordingSession: could not remove silent recording '{audioPath}': {e.Message}");
            }

            audioPath = null;
        }

        var package = compositionUi.Symbol.SymbolPackage;
        var dataAddress = TryImportRecording(dataPath, package) ?? dataPath;
        var audioAddress = TryImportRecording(audioPath, package) ?? audioPath;

        // Apply FilePath to the LoadDataClip via the standard input-value command so
        // undo restores the empty default. AddSymbolChildCommand.Undo removes the child
        // entirely, so this is only meaningful when the user redoes after an undo.
        if (!string.IsNullOrEmpty(dataAddress)
            && compositionUi.Symbol.Children.TryGetValue(_activeDataClipChildId, out var symbolChild)
            && symbolChild.Inputs.TryGetValue(_loadDataClipFilePathInputId, out var filePathInput))
        {
            var newValue = new InputValue<string>(dataAddress);
            _activeMacro?.AddAndExecCommand(new ChangeInputValueCommand(compositionUi.Symbol,
                                                                        _activeDataClipChildId,
                                                                        filePathInput,
                                                                        newValue));
        }

        // Apply the recorded file to the AudioClip op's Path input via the standard
        // input-value command (same pattern as the data clip above), so undo restores the
        // empty default.
        if (!string.IsNullOrEmpty(audioAddress)
            && compositionUi.Symbol.Children.TryGetValue(_activeAudioClipChildId, out var audioChild)
            && audioChild.Inputs.TryGetValue(_audioClipPathInputId, out var audioPathInput))
        {
            var newAudioValue = new InputValue<string>(audioAddress);
            _activeMacro?.AddAndExecCommand(new ChangeInputValueCommand(compositionUi.Symbol,
                                                                        _activeAudioClipChildId,
                                                                        audioPathInput,
                                                                        newAudioValue));
        }

        if (_activeMacro != null)
            UndoRedoStack.Add(_activeMacro);

        // Mirror the Begin-side flagging so the file-path commit on Stop (which makes the
        // clip's label switch from "rec…" to the real filename) and any other late mutations
        // propagate to the graph view without a click.
        compositionUi.FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);

        ResetSessionState();
    }

    private static void ResetSessionState()
    {
        _activeMacro = null;
        _activeDataClipChildId = Guid.Empty;
        _activeAudioClipChildId = Guid.Empty;
        _compositionSymbolId = Guid.Empty;
        _recordStartBars = 0;
        _recordStartRunSecs = 0;
    }

    /// <summary>
    /// Copies a just-finalised recording into the active project's Assets folder via
    /// <see cref="FileImport.TryImportDroppedFile"/>, registering it with
    /// <see cref="AssetRegistry"/> and triggering an AssetLib refresh. Returns the
    /// resulting <c>Asset.Address</c> (e.g. <c>"project:Assets/audio/rec-001.wav"</c>) so
    /// the clip's FilePath / AssetPath resolves through the registry afterwards. Returns
    /// null on failure — caller falls back to the original absolute path.
    /// </summary>
    private static string? TryImportRecording(string? absolutePath, T3.Core.Resource.IResourcePackage? package)
    {
        if (string.IsNullOrEmpty(absolutePath) || package == null)
            return null;

        // Import into the type's RecordingFolder (audio / dataclips) — the same folder NextIndexScanDirs
        // reads, so storage + index-scan stay driven by the one AssetType field.
        var subfolder = AssetType.TryGetForFilePath(absolutePath, out var assetType, out _) ? assetType.RecordingFolder : null;
        var destinationFolder = string.IsNullOrEmpty(subfolder)
                                    ? null
                                    : System.IO.Path.Combine(package.AssetsFolder, subfolder);

        if (!FileImport.TryImportDroppedFile(absolutePath, package, destinationFolder, out var asset))
        {
            Log.Warning($"RecordingSession: failed to import {absolutePath} into project Assets; clip will reference the original absolute path.");
            return null;
        }

        // The recording now lives in the project's Assets folder (the canonical location the clip
        // references). Remove the temp-staging copy so each session doesn't leave a duplicate behind.
        // Best-effort — only runs after a successful import, so the data is already safe in Assets.
        try
        {
            System.IO.File.Delete(absolutePath);
        }
        catch (Exception e)
        {
            Log.Debug($"RecordingSession: could not remove temp recording {absolutePath}: {e.Message}");
        }

        return asset.Address;
    }

    /// <summary>
    /// True when a finalised recording holds no audio samples — a header-only WAV produced when capture
    /// ran but WASAPI delivered no frames. Such a file is unreadable as a waveform and shouldn't be
    /// imported. A missing or unstattable file is treated as not-silent so the normal import path runs.
    /// </summary>
    private static bool IsSilentRecording(string absolutePath)
    {
        try
        {
            // A PCM WAV with no sample data is exactly its 44-byte RIFF/fmt/data header (see WavFileWriter).
            return new System.IO.FileInfo(absolutePath).Length <= WavHeaderBytes;
        }
        catch (Exception e)
        {
            Log.Debug($"RecordingSession: couldn't stat recording '{absolutePath}': {e.Message}");
            return false;
        }
    }

    private const long WavHeaderBytes = 44;

    /// <summary>
    /// Directories scanned to pick the next session index: the project's imported-recording folders (the
    /// persistent record) plus the temp staging dir (covers a leftover from a failed import). Deleting the
    /// temp copies on import is why the persistent Assets folders must be the source of truth — scanning only
    /// the temp dir would reset the index to 001 every session.
    /// </summary>
    private static string[] NextIndexScanDirs(Instance compositionOp)
    {
        var dirs = new List<string> { RecordingPaths.TempRecordingsDirectory };

        var assetsFolder = compositionOp.Symbol.SymbolPackage?.AssetsFolder;
        if (!string.IsNullOrEmpty(assetsFolder))
        {
            // Each recordable asset type declares its import subfolder via AssetType.RecordingFolder (set in
            // AssetHandling). Scanning those persistent folders is what keeps the session index climbing after
            // the temp copies are deleted on import.
            foreach (var assetType in AssetType.AvailableTypes)
            {
                if (!string.IsNullOrEmpty(assetType.RecordingFolder))
                    dirs.Add(System.IO.Path.Combine(assetsFolder, assetType.RecordingFolder));
            }
        }

        return dirs.ToArray();
    }

    private static void InitClipTimeClip(Symbol composition, Guid childId, float startBars, int layer)
    {
        if (!composition.Children.TryGetValue(childId, out var symbolChild))
        {
            Log.Warning("RecordingSession: newly added recording clip not found in composition; TimeRange not initialised.");
            return;
        }

        foreach (var output in symbolChild.Outputs.Values)
        {
            if (output.OutputData is TimeClip tc)
            {
                // TimeRange is in timeline bars (placement); SourceRange is in file-time
                // bars (anchored at 0). Keeping the two in different spaces is what makes
                // cut / drag-start trim mathematically consistent — splitting a clip just
                // narrows both ranges in their own coordinate space, and LoadDataClip
                // maps file events without rebasing.
                tc.TimeRange = new TimeRange(startBars, startBars);
                tc.SourceRange = new TimeRange(0f, 0f);
                tc.LayerIndex = layer;
                break;
            }
        }
    }

    /// <summary>
    /// Canvas position for a fresh recording-clip op of type <paramref name="anchorSymbolId"/>.
    /// Anchors below the lowest existing op of the same type (so successive recordings stay in a
    /// tidy column), re-centres on the viewport when that anchor is off-screen, then hands the
    /// preferred spot to <see cref="GraphUtils.FindFreePosition"/> for collision-avoidance —
    /// without that last step, repeated recordings that all re-centre to the same viewport pile
    /// up on top of each other.
    /// </summary>
    private static Vector2 FindFreeCanvasPositionForClip(Symbol compositionSymbol, Guid anchorSymbolId)
    {
        const float spacingY = 80f;
        const float defaultX = 0f;
        const float defaultY = 200f;

        var preferred = new Vector2(defaultX, defaultY);

        if (!SymbolUiRegistry.TryGetSymbolUi(compositionSymbol.Id, out var compositionUi))
            return preferred;

        var maxY = float.NegativeInfinity;
        var anchorX = defaultX;
        foreach (var (childId, childUi) in compositionUi.ChildUis)
        {
            if (!compositionSymbol.Children.TryGetValue(childId, out var symbolChild))
                continue;
            if (symbolChild.Symbol.Id != anchorSymbolId)
                continue;

            if (childUi.PosOnCanvas.Y > maxY)
            {
                maxY = childUi.PosOnCanvas.Y;
                anchorX = childUi.PosOnCanvas.X;
            }
        }

        if (!float.IsNegativeInfinity(maxY))
            preferred = new Vector2(anchorX, maxY + spacingY);

        // Re-centre on the visible canvas when the stack-anchor candidate is off-screen,
        // so a new clip doesn't land far outside the viewport where it looks like nothing
        // happened.
        if (ProjectView.Focused?.GraphView is ScalableCanvas canvas)
        {
            var visible = canvas.GetVisibleCanvasArea();
            if (visible.GetWidth() > 0 && visible.GetHeight() > 0 && !visible.Contains(preferred))
                preferred = visible.GetCenter();
        }

        return GraphUtils.FindFreePosition(compositionUi, preferred, SymbolUi.Child.DefaultOpSize);
    }

    /// <summary>
    /// Picks the base layer for a new recording's (data, audio) pair. Occupancy is
    /// evaluated at the recording's start position only — a clip parked far down the
    /// timeline doesn't reserve its row for a recording happening earlier. Preference order:
    /// (1) the previous session's base layer if both rows there are free at start — keeps
    ///     consecutive recordings on the same lane the user already cleared / dedicated;
    /// (2) the lowest pair (i, i+1) that's free at start;
    /// (3) one layer above the highest occupied row anywhere in the composition.
    /// Step (3) only triggers when (1) and (2) both fail, i.e. the timeline is densely
    /// packed at this position — same fallback the original behaviour produced.
    /// </summary>
    private static int FindNextLayerIndex(Instance compositionOp, float startBars)
    {
        _occupiedAtStartScratch.Clear();
        var maxLayerAnywhere = -1;

        // Both data and audio recording clips are now TimeClip-backed ops, so a single
        // GetAllTimeClips pass covers layer occupancy (the main soundtrack lives outside the
        // clip grid as the background image and never reserved a row).
        foreach (var clip in Structure.GetAllTimeClips(compositionOp))
        {
            if (clip.LayerIndex > maxLayerAnywhere)
                maxLayerAnywhere = clip.LayerIndex;
            if (clip.TimeRange.Start <= startBars && startBars < clip.TimeRange.End)
                _occupiedAtStartScratch.Add(clip.LayerIndex);
        }

        // Re-use the previous session's lane when nothing's parked across it at this start
        // position. Makes "record, listen back, record again at a different point" stay on
        // the same dedicated row instead of marching downward each take.
        if (_lastBaseLayerIndex >= 0
            && !_occupiedAtStartScratch.Contains(_lastBaseLayerIndex)
            && !_occupiedAtStartScratch.Contains(_lastBaseLayerIndex + 1))
        {
            return _lastBaseLayerIndex;
        }

        // Otherwise scan from 0 up for the first free pair at this start position.
        for (var i = 0; i <= maxLayerAnywhere + 1; i++)
        {
            if (!_occupiedAtStartScratch.Contains(i) && !_occupiedAtStartScratch.Contains(i + 1))
                return i;
        }

        return maxLayerAnywhere + 1;
    }

    // Reused across calls to keep FindNextLayerIndex allocation-free — Begin is user-
    // triggered (one click) but the helper is cheap to call and we already follow the
    // editor's "no per-frame allocations" habit.
    private static readonly HashSet<int> _occupiedAtStartScratch = new();

    // Last base layer the user actually recorded onto. Persists across sessions so the
    // next recording prefers the same lane when it's free at the new start position.
    // -1 means "no preference yet" — fresh editor launch falls back to the scan path.
    private static int _lastBaseLayerIndex = -1;

    private static readonly Guid _loadDataClipSymbolId = new("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607");
    private static readonly Guid _loadDataClipFilePathInputId = new("70419103-ae5d-4ca0-cf4e-456071829304");
    private static readonly Guid _audioClipSymbolId = new("f0008b50-091d-4e9f-91eb-baa212acfa20");
    private static readonly Guid _audioClipPathInputId = new("625951af-5f99-4171-b5b0-c97413121f56");

    private static Guid _compositionSymbolId;
    private static Guid _activeDataClipChildId;
    private static Guid _activeAudioClipChildId;
    private static MacroCommand? _activeMacro;
    private static double _recordStartBars;
    private static double _recordStartRunSecs;

    // Track which underlying recorders the active session actually started so Stop()
    // only calls EndRecording on the ones with a running session — avoids the underlying
    // recorder logging "no session active" warnings when capture was opted out.
    private static bool _audioCaptureActive;
    private static bool _ioCaptureActive;
}
