#nullable enable

using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Commands.Graph;
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
/// <c>TimeRange.End</c> on both the <c>LoadDataClip</c> op and the
/// <c>TimelineAudioClip</c> by the elapsed seconds since record-start (converted to
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

        var startBars = (float)_recordStartBars;
        var baseLayer = FindNextLayerIndex(compositionOp);
        var dataLayer = baseLayer;
        var audioLayer = baseLayer + 1;

        _activeMacro = new MacroCommand("Live recording session");

        // LoadDataClip op — created up-front so its TimeClip is visible on the timeline
        // immediately. FilePath stays empty until Stop fills it in. Stack below any
        // existing LoadDataClip ops on the canvas so successive recordings don't pile up
        // on top of each other at the default (0, 0) position.
        var addCmd = new AddSymbolChildCommand(compositionOp.Symbol, _loadDataClipSymbolId)
                         {
                             PosOnCanvas = FindFreeCanvasPositionForLoadDataClip(compositionOp.Symbol),
                         };
        _activeMacro.AddAndExecCommand(addCmd);
        _activeDataClipChildId = addCmd.AddedChildId;
        InitDataClipTimeClip(compositionOp.Symbol, _activeDataClipChildId, startBars, dataLayer);

        // TimelineAudioClip — appended with empty AssetPath; final path lands on Stop.
        // Sits one layer above the data clip so the two pair stays visually grouped but
        // doesn't overlap.
        _activeAudioClip = new TimelineAudioClip
                               {
                                   Id = Guid.NewGuid(),
                                   AssetPath = string.Empty,
                                   TimeRange = new TimeRange(startBars, startBars),
                                   LayerIndex = audioLayer,
                                   IsMainSoundtrack = false,
                               };
        _activeMacro.AddAndExecCommand(new AddTimelineAudioClipCommand(compositionOp, _activeAudioClip));

        // Recorders last — if a Begin fails, the clips already exist but stay zero-width
        // until Stop. The user sees something went wrong; undo cleanly removes both clips.
        WasapiAudioInput.BeginRecording();
        IoDataSetRecorder.BeginRecording();

        IsActive = true;
    }

    /// <summary>
    /// Called once per frame while a session is active. Extends both clips' TimeRange.End
    /// based on wall-clock elapsed time since Start, converted to bars at the current BPM.
    /// No-op when no session is active.
    /// </summary>
    public static void OnFrame()
    {
        if (!IsActive)
            return;

        var playback = Playback.Current;
        if (playback == null)
            return;

        var elapsedSecs = Playback.RunTimeInSecs - _recordStartRunSecs;
        var elapsedBars = elapsedSecs * playback.Bpm / 240.0;
        var newEnd = (float)(_recordStartBars + elapsedBars);

        // LoadDataClip's TimeClip output data — mutate in place. Animated dirty flag on
        // the op's Clip output ensures the next Update picks up the new range.
        if (SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionUi)
            && compositionUi.Symbol.Children.TryGetValue(_activeDataClipChildId, out var symbolChild))
        {
            foreach (var output in symbolChild.Outputs.Values)
            {
                if (output.OutputData is TimeClip tc)
                {
                    tc.TimeRange.End = newEnd;
                    tc.SourceRange.End = newEnd;
                    break;
                }
            }
        }

        if (_activeAudioClip != null)
        {
            _activeAudioClip.TimeRange.End = newEnd;
        }
    }

    public static void Stop()
    {
        if (!IsActive)
            return;

        IsActive = false;

        var audioPath = WasapiAudioInput.EndRecording();
        var dataPath = IoDataSetRecorder.EndRecording();

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
        // AssetLib UI. The original copy in the recordings dir stays in place as a backup.
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

        // Audio AssetPath is part of the TimelineAudioClip value the macro already owns;
        // mutating it in place is fine because the macro's undo path removes the clip
        // outright (no separate "restore AssetPath" entry needed).
        if (!string.IsNullOrEmpty(audioAddress) && _activeAudioClip != null)
        {
            _activeAudioClip.AssetPath = audioAddress;
        }

        if (_activeMacro != null)
            UndoRedoStack.Add(_activeMacro);

        ResetSessionState();
    }

    private static void ResetSessionState()
    {
        _activeMacro = null;
        _activeDataClipChildId = Guid.Empty;
        _activeAudioClip = null;
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

        if (!FileImport.TryImportDroppedFile(absolutePath, package, subfolder: null, out var asset))
        {
            Log.Warning($"RecordingSession: failed to import {absolutePath} into project Assets; clip will reference the original absolute path.");
            return null;
        }

        return asset.Address;
    }

    private static void InitDataClipTimeClip(Symbol composition, Guid childId, float startBars, int layer)
    {
        if (!composition.Children.TryGetValue(childId, out var symbolChild))
        {
            Log.Warning("RecordingSession: newly added LoadDataClip not found in composition; TimeRange not initialised.");
            return;
        }

        foreach (var output in symbolChild.Outputs.Values)
        {
            if (output.OutputData is TimeClip tc)
            {
                tc.TimeRange = new TimeRange(startBars, startBars);
                tc.SourceRange = new TimeRange(startBars, startBars);
                tc.LayerIndex = layer;
                break;
            }
        }
    }

    /// <summary>
    /// Canvas position for a fresh <c>LoadDataClip</c> op. Stacks below the lowest
    /// existing LoadDataClip child so successive recordings line up vertically instead
    /// of overlapping at the default (0, 0). If no LoadDataClips exist yet, places at a
    /// sensible default offset from origin.
    /// </summary>
    private static Vector2 FindFreeCanvasPositionForLoadDataClip(Symbol compositionSymbol)
    {
        const float spacingY = 80f;
        const float defaultX = 0f;
        const float defaultY = 200f;

        if (!SymbolUiRegistry.TryGetSymbolUi(compositionSymbol.Id, out var compositionUi))
            return new Vector2(defaultX, defaultY);

        var maxY = float.NegativeInfinity;
        var anchorX = defaultX;
        foreach (var (childId, childUi) in compositionUi.ChildUis)
        {
            if (!compositionSymbol.Children.TryGetValue(childId, out var symbolChild))
                continue;
            if (symbolChild.Symbol.Id != _loadDataClipSymbolId)
                continue;

            if (childUi.PosOnCanvas.Y > maxY)
            {
                maxY = childUi.PosOnCanvas.Y;
                anchorX = childUi.PosOnCanvas.X;
            }
        }

        return float.IsNegativeInfinity(maxY)
                   ? new Vector2(defaultX, defaultY)
                   : new Vector2(anchorX, maxY + spacingY);
    }

    /// <summary>
    /// Top layer above any existing TimeClip / TimelineAudioClip, so new recordings land
    /// on a fresh row rather than overlapping pre-existing clips.
    /// </summary>
    private static int FindNextLayerIndex(Instance compositionOp)
    {
        var maxLayer = -1;

        foreach (var clip in Structure.GetAllTimeClips(compositionOp))
        {
            if (clip.LayerIndex > maxLayer)
                maxLayer = clip.LayerIndex;
        }

        foreach (var audioClip in compositionOp.Symbol.CompositionSettings.Playback.AudioClips)
        {
            if (audioClip.LayerIndex > maxLayer)
                maxLayer = audioClip.LayerIndex;
        }

        return maxLayer + 1;
    }

    private static readonly Guid _loadDataClipSymbolId = new("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607");
    private static readonly Guid _loadDataClipFilePathInputId = new("70419103-ae5d-4ca0-cf4e-456071829304");

    private static Guid _compositionSymbolId;
    private static Guid _activeDataClipChildId;
    private static TimelineAudioClip? _activeAudioClip;
    private static MacroCommand? _activeMacro;
    private static double _recordStartBars;
    private static double _recordStartRunSecs;
}
