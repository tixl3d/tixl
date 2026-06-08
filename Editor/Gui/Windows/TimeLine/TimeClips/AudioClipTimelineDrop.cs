#nullable enable

using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Handles dropping audio files (.wav / .mp3 / .ogg) onto the timeline clip area: imports each
/// into the project, then creates an <c>[AudioClip]</c> op placed at the drop time / layer with
/// its file path and duration set. The whole drop lands as one <see cref="MacroCommand"/> so it
/// undoes together. Called from <see cref="ClipArea"/> after the layer group closes.
/// </summary>
internal static class AudioClipTimelineDrop
{
    public static void Handle(Instance compositionOp, TimeLineCanvas timeCanvas, float layerTopY, int minLayerIndex)
    {
        var result = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.ExternalFile, out var data);
        if (result != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(data))
            return;

        var playback = timeCanvas.Playback;
        if (playback == null)
            return;

        var package = compositionOp.Symbol.SymbolPackage;
        if (package == null)
        {
            Log.Warning("Cannot resolve composition's resource package for audio drop.");
            return;
        }

        var mousePos = ImGui.GetMousePos();
        var dropTimeBars = (float)timeCanvas.InverseTransformX(mousePos.X);
        var dropLayerIndex = minLayerIndex == int.MaxValue
                                 ? 0
                                 : minLayerIndex + (int)Math.Round((mousePos.Y - layerTopY - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight);

        var macro = new MacroCommand("Drop audio clips");
        var added = 0;

        foreach (var path in data.Split('|'))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".wav" && ext != ".mp3" && ext != ".ogg")
                continue;

            if (!FileImport.TryImportDroppedFile(path, package, subfolder: null, out var asset))
            {
                Log.Warning($"Failed to import audio file: {path}");
                continue;
            }

            var durationSecs = AudioMixerManager.TryProbeAudioDurationSecs(asset.FullPath);
            var durationBars = durationSecs > 0 ? (float)playback.BarsFromSeconds(durationSecs) : 4f;

            CreateAudioClipOp(compositionOp, macro, asset.Address, dropTimeBars, durationBars, dropLayerIndex);
            // Stack subsequent files on new layers so a multi-drop doesn't pile clips on top of each other.
            dropLayerIndex++;
            added++;
        }

        if (added == 0)
            return;

        UndoRedoStack.Add(macro);
        compositionOp.GetSymbolUi().FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);
    }

    private static void CreateAudioClipOp(Instance compositionOp, MacroCommand macro, string assetAddress,
                                          float startBars, float durationBars, int layer)
    {
        var addCmd = new AddSymbolChildCommand(compositionOp.Symbol, _audioClipSymbolId)
                         {
                             PosOnCanvas = GraphUtils.FindFreePosition(compositionOp.GetSymbolUi(),
                                                                       new Vector2(0, 200),
                                                                       SymbolUi.Child.DefaultOpSize),
                         };
        macro.AddAndExecCommand(addCmd);
        var childId = addCmd.AddedChildId;

        if (!compositionOp.Symbol.Children.TryGetValue(childId, out var symbolChild))
            return;

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

        if (symbolChild.Inputs.TryGetValue(_audioClipPathInputId, out var pathInput))
        {
            macro.AddAndExecCommand(new ChangeInputValueCommand(compositionOp.Symbol, childId, pathInput,
                                                                new InputValue<string>(assetAddress)));
        }
    }

    private static readonly Guid _audioClipSymbolId = new("f0008b50-091d-4e9f-91eb-baa212acfa20");
    private static readonly Guid _audioClipPathInputId = new("625951af-5f99-4171-b5b0-c97413121f56");
}
