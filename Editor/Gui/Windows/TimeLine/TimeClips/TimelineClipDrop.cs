#nullable enable

using System;
using System.Numerics;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.Video;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.IoServices;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Generic timeline-clip drop. Any asset whose <see cref="AssetType.TimelineClipOperator"/> is set
/// (AudioClip, VideoClip, LoadDataClip, …) can be dropped onto the clip area — from the AssetLibrary
/// (<see cref="DragAndDropHandling.DragTypes.FileAsset"/>) or as an external OS file
/// (<see cref="DragAndDropHandling.DragTypes.ExternalFile"/>, imported first). Each drop creates the
/// type's op at the drop time / layer, sets its file-path input (found generically via
/// <see cref="SymbolAnalysis.TryGetFileInputFromInstance"/>), and initialises its TimeClip — all as
/// one <see cref="MacroCommand"/>. New clip types work out of the box once they declare the hint.
/// </summary>
internal static class TimelineClipDrop
{
    public static void Handle(Instance compositionOp, TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex,
                              ImRect dropRegion, int maxLayerIndex)
    {
        // AssetLibrary drag — payload is an asset address; already imported. The drop zone is the whole
        // timeline region (not just the drawn layer rows), so files land as clips wherever they're dropped.
        var assetResult = DragAndDropHandling.TryHandleDropOnRect(DragAndDropHandling.DragTypes.FileAsset, dropRegion, out var address);
        if (assetResult == DragAndDropHandling.DragInteractionResult.Dropped && !string.IsNullOrEmpty(address))
        {
            if (AssetRegistry.TryGetAsset(address, out var asset) && asset.AssetType.TimelineClipOperator is { } opId
                && TryComputeDrop(timeCanvas, layerTopY, minLayerIndex, maxLayerIndex, out var dropBars, out var dropLayer, out var playback))
            {
                var macro = new MacroCommand("Drop timeline clip");
                CreateClipOp(compositionOp, macro, opId, asset.Address, asset.FullPath, dropBars, dropLayer, playback);
                FinishMacro(compositionOp, macro);
            }
            return;
        }

        // While dragging over the timeline, preview where the clip will land and how long it will be.
        if (assetResult == DragAndDropHandling.DragInteractionResult.Hovering && !string.IsNullOrEmpty(address))
        {
            DrawDropPreview(compositionOp, timeCanvas, layerTopY, minLayerIndex, maxLayerIndex, address);
            return;
        }

        // External OS-file drag — import each (only files whose type is timeline-droppable).
        var fileResult = DragAndDropHandling.TryHandleDropOnRect(DragAndDropHandling.DragTypes.ExternalFile, dropRegion, out var data);
        if (fileResult != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(data))
            return;

        var package = compositionOp.Symbol.SymbolPackage;
        if (package == null)
        {
            Log.Warning("Cannot resolve composition's resource package for timeline clip drop.");
            return;
        }
        if (!TryComputeDrop(timeCanvas, layerTopY, minLayerIndex, maxLayerIndex, out var bars, out var layer, out var pb))
            return;

        var fileMacro = new MacroCommand("Drop timeline clips");
        var added = 0;
        foreach (var path in data.Split('|'))
        {
            if (!AssetType.TryGetForFilePath(path, out var assetType, out _) || assetType.TimelineClipOperator is not { } fileOpId)
                continue;
            if (!FileImport.TryImportDroppedFile(path, package, destinationFolder: null, out var asset))
            {
                Log.Warning($"Failed to import dropped file: {path}");
                continue;
            }

            CreateClipOp(compositionOp, fileMacro, fileOpId, asset.Address, asset.FullPath, bars, layer, pb);
            // Stack subsequent files on new layers so a multi-drop doesn't pile clips on top of each other.
            layer++;
            added++;
        }

        if (added > 0)
            FinishMacro(compositionOp, fileMacro);
    }

    /// <summary>
    /// Ghost of the clip a hovering asset would create: drawn at the prospective drop time/layer and sized to
    /// the asset's real duration (audio/video probed once via the async caches, others a placeholder). Mirrors
    /// what <see cref="CreateClipOp"/> initialises so the drag previews the actual result.
    /// </summary>
    private static void DrawDropPreview(Instance compositionOp, TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex, int maxLayerIndex, string address)
    {
        if (!AssetRegistry.TryGetAsset(address, out var asset) || asset.AssetType.TimelineClipOperator is null
            || !TryComputeDrop(timeCanvas, layerTopY, minLayerIndex, maxLayerIndex, out var dropBars, out var dropLayer, out var playback))
            return;

        // Probe via the async per-asset caches so the drag never blocks on a file open; until the probe lands
        // the preview uses the same placeholder length the drop would.
        double durationSecs = 0;
        if (asset.AssetType.Name == "Video")
        {
            VideoClipDurationCache.TryGetDurationSecs(address, compositionOp, out durationSecs);
        }
        else if (asset.AssetType.Name == "Audio")
        {
            AudioClipDurationCache.TryGetDurationSecs(address, compositionOp, out durationSecs);
        }
        else if (asset.AssetType.Name == "Midi")
        {
            // Synchronous is fine here: the converter caches by (path, last-write), so only the
            // first preview frame pays the parse.
            durationSecs = TryProbeMidiDurationSecs(asset.FullPath);
        }

        var durationBars = durationSecs > 0 ? (float)playback.BarsFromSeconds(durationSecs) : 4f;

        var x0 = timeCanvas.TransformX(dropBars);
        var x1 = timeCanvas.TransformX(dropBars + durationBars);
        var layerOffset = minLayerIndex == int.MaxValue ? 0 : dropLayer - minLayerIndex;
        var y0 = layerTopY + layerOffset * ClipArea.LayerHeight;
        var y1 = y0 + ClipArea.LayerHeight - 2;

        var drawList = ImGui.GetWindowDrawList();
        var color = asset.AssetType.Color;
        drawList.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), color.Fade(0.3f), 4.5f);
        drawList.AddRect(new Vector2(x0, y0), new Vector2(x1, y1), color.Fade(0.8f), 4.5f);

        var label = System.IO.Path.GetFileNameWithoutExtension(asset.FullPath);
        ImGui.PushFont(Fonts.FontSmall);
        drawList.AddText(new Vector2(x0 + 4, y0 + 1), UiColors.ForegroundFull.Fade(0.8f), label);
        ImGui.PopFont();
    }

    private static void CreateClipOp(Instance compositionOp, MacroCommand macro, Guid opSymbolId,
                                     string assetAddress, string assetFullPath, float startBars, int layer, Playback playback)
    {
        var addCmd = new AddSymbolChildCommand(compositionOp.Symbol, opSymbolId)
                         {
                             PosOnCanvas = GraphUtils.FindFreePosition(compositionOp.GetSymbolUi(),
                                                                       new Vector2(0, 200),
                                                                       SymbolUi.Child.DefaultOpSize),
                         };
        macro.AddAndExecCommand(addCmd);
        var childId = addCmd.AddedChildId;

        if (!compositionOp.Symbol.Children.TryGetValue(childId, out var symbolChild))
            return;

        var durationBars = ProbeDurationBars(assetFullPath, playback);

        // Init the op's TimeClip output data in place. AddSymbolChildCommand.Undo removes the child
        // outright, so this mutation doesn't need its own undo entry.
        foreach (var output in symbolChild.Outputs.Values)
        {
            if (output.OutputData is TimeClip tc)
            {
                tc.TimeRange = new TimeRange(startBars, startBars + durationBars);
                tc.SourceRange = new TimeRange(0f, durationBars);
                tc.LayerIndex = layer;
                break;
            }
        }

        // Set the file-path input generically (the input whose UI Usage is FilePath), via the
        // standard input-value command so undo restores the empty default.
        if (compositionOp.Children.TryGetChildInstance(childId, out var instance)
            && SymbolAnalysis.TryGetFileInputFromInstance(instance, out var fileInputSlot, out _)
            && symbolChild.Inputs.TryGetValue(fileInputSlot.Id, out var childInput))
        {
            macro.AddAndExecCommand(new ChangeInputValueCommand(compositionOp.Symbol, childId, childInput,
                                                                new InputValue<string>(assetAddress)));
        }
        else
        {
            Log.Warning($"Dropped timeline clip op {opSymbolId} has no file-path input; clip created without a file.");
        }
    }

    private static bool TryComputeDrop(TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex, int maxLayerIndex,
                                       out float dropBars, out int dropLayer, out Playback playback)
    {
        dropBars = 0;
        dropLayer = 0;
        playback = null!;

        var p = timeCanvas.Playback;
        if (p == null)
            return false;
        playback = p;

        var mousePos = ImGui.GetMousePos();
        dropBars = (float)timeCanvas.InverseTransformX(mousePos.X);
        dropLayer = minLayerIndex == int.MaxValue
                        ? 0
                        : minLayerIndex + (int)Math.Round((mousePos.Y - layerTopY - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight);

        // The drop zone spans the whole timeline, so a drop far below the rows would compute a distant
        // layer — clamp to just below the existing ones instead.
        if (maxLayerIndex != int.MinValue && minLayerIndex != int.MaxValue)
            dropLayer = Math.Clamp(dropLayer, minLayerIndex - 1, maxLayerIndex + 1);
        return true;
    }

    /// <summary>
    /// Initial clip length in bars. Audio, video and MIDI are probed for their real duration so the clip
    /// matches the file; types with no probe (e.g. data) default to a placeholder.
    /// </summary>
    private static float ProbeDurationBars(string absolutePath, Playback playback)
    {
        // MIDI goes first: the audio probe logs a FileFormat warning for non-audio files.
        var durationSecs = TryProbeMidiDurationSecs(absolutePath);

        if (durationSecs <= 0)
        {
            durationSecs = AudioMixerManager.TryProbeAudioDurationSecs(absolutePath);
        }

        if (durationSecs <= 0 && VideoExport.Factory is { } factory
            && factory.TryProbeDurationSeconds(absolutePath, out var videoSecs))
        {
            durationSecs = videoSecs;
        }

        return durationSecs > 0 ? (float)playback.BarsFromSeconds(durationSecs) : 4f;
    }

    /// <summary>
    /// Duration of a .mid/.midi file in seconds, or 0 for non-MIDI paths. The converter caches by
    /// (path, last-write), so the parse cost is paid once and shared with the MidiClip op the
    /// drop creates.
    /// </summary>
    private static double TryProbeMidiDurationSecs(string absolutePath)
    {
        var extension = System.IO.Path.GetExtension(absolutePath);
        var isMidi = string.Equals(extension, ".mid", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase);
        if (!isMidi)
            return 0;

        if (!MidiFileToDataSet.TryGet(absolutePath, out var dataSet, out _))
            return 0;

        return dataSet.Metadata?[MidiFileToDataSet.SourceDurationSecsKey]?.Value<double>() ?? 0;
    }

    private static void FinishMacro(Instance compositionOp, MacroCommand macro)
    {
        UndoRedoStack.Add(macro);
        compositionOp.GetSymbolUi().FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);
    }
}
