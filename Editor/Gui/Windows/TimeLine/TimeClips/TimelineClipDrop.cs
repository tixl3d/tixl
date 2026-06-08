#nullable enable

using System;
using System.Numerics;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;

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
    public static void Handle(Instance compositionOp, TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex)
    {
        // AssetLibrary drag — payload is an asset address; already imported.
        var assetResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.FileAsset, out var address);
        if (assetResult == DragAndDropHandling.DragInteractionResult.Dropped && !string.IsNullOrEmpty(address))
        {
            if (AssetRegistry.TryGetAsset(address, out var asset) && asset.AssetType.TimelineClipOperator is { } opId
                && TryComputeDrop(timeCanvas, layerTopY, minLayerIndex, out var dropBars, out var dropLayer, out var playback))
            {
                var macro = new MacroCommand("Drop timeline clip");
                CreateClipOp(compositionOp, macro, opId, asset.Address, asset.FullPath, dropBars, dropLayer, playback);
                FinishMacro(compositionOp, macro);
            }
            return;
        }

        // External OS-file drag — import each (only files whose type is timeline-droppable).
        var fileResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.ExternalFile, out var data);
        if (fileResult != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(data))
            return;

        var package = compositionOp.Symbol.SymbolPackage;
        if (package == null)
        {
            Log.Warning("Cannot resolve composition's resource package for timeline clip drop.");
            return;
        }
        if (!TryComputeDrop(timeCanvas, layerTopY, minLayerIndex, out var bars, out var layer, out var pb))
            return;

        var fileMacro = new MacroCommand("Drop timeline clips");
        var added = 0;
        foreach (var path in data.Split('|'))
        {
            if (!AssetType.TryGetForFilePath(path, out var assetType, out _) || assetType.TimelineClipOperator is not { } fileOpId)
                continue;
            if (!FileImport.TryImportDroppedFile(path, package, subfolder: null, out var asset))
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

    private static bool TryComputeDrop(TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex,
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
        return true;
    }

    /// <summary>
    /// Initial clip length in bars. Audio is probed for its real duration so the clip matches the file;
    /// other types (video, data) default to a placeholder until a per-type probe is wired in.
    /// </summary>
    private static float ProbeDurationBars(string absolutePath, Playback playback)
    {
        var durationSecs = AudioMixerManager.TryProbeAudioDurationSecs(absolutePath);
        return durationSecs > 0 ? (float)playback.BarsFromSeconds(durationSecs) : 4f;
    }

    private static void FinishMacro(Instance compositionOp, MacroCommand macro)
    {
        UndoRedoStack.Add(macro);
        compositionOp.GetSymbolUi().FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);
    }
}
