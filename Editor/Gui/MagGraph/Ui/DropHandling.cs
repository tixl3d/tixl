#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.MagGraph.Ui;

/// <summary>
/// Handles dropping items onto graph. 
/// </summary>
internal static class DropHandling
{
    /// <summary>
    /// Called once per frame: draws a faint drop preview under the mouse while a symbol or
    /// asset drag is in flight, so the drag reads before a graph window is hovered. The graph's
    /// drop target draws the full-opacity preview on top when dropping is possible.
    /// </summary>
    internal static void DrawDragIndicator()
    {
        if (DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.Symbol, out var symbolData))
        {
            if (Guid.TryParse(symbolData, out var symbolId)
                && SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
            {
                DrawDropPreviewItem(symbolUi.Symbol, fade: 0.2f);
            }
        }
        else if (DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.FileAsset, out var address)
                 && AssetRegistry.TryGetAsset(address, out var asset))
        {
            DrawDropPreviewItem(asset, fade: 0.2f);
        }
    }

    internal static void HandleDropOnWindow(GraphUiContext context)
    {
        if (!DragAndDropHandling.IsDragging)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());

        if (HandleDropSymbol(context))
            return;

        if (HandleDropExternalFile(context))
            return;

        HandleDropAsset(context);
    }

    private static void HandleDropAsset(GraphUiContext context)
    {
        var assetResult= DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.FileAsset, out var address);

        if (assetResult != DragAndDropHandling.DragInteractionResult.Hovering
            && assetResult != DragAndDropHandling.DragInteractionResult.Dropped
            || address == null) return;

        if (!AssetRegistry.TryGetAsset(address, out var asset))
        {
            Log.Warning($"Can't get asset for {address}");
            return;
        }

        if (assetResult == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            DrawDropPreviewItem(asset);
        }
        else
        {
            CreateAssetOperatorOnGraph(context, asset, Vector2.Zero);
        }
    }

    private static bool HandleDropSymbol(GraphUiContext context)
    {
        var result=DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.Symbol, out var data);

        if (result == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            if (Guid.TryParse(data, out var hoveredSymbolId)
                && SymbolUiRegistry.TryGetSymbolUi(hoveredSymbolId, out var hoveredSymbolUi))
            {
                DrawDropPreviewItem(hoveredSymbolUi.Symbol);
            }

            return false;
        }

        if (result != DragAndDropHandling.DragInteractionResult.Dropped)
            return false;

        if (!Guid.TryParse(data, out var symbolId))
        {
            Log.Warning("Invalid data format for drop? " + data);
            return true;
        }

        TryCreateSymbolInstanceOnGraph(context, symbolId, Vector2.Zero, out _);
        return false;
    }

    private static bool HandleDropExternalFile(GraphUiContext context)
    {
        SymbolPackage? package = context.CompositionInstance.Symbol.SymbolPackage;
        if (package == null)
            return false;

        var result=DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.ExternalFile, out var data);

        var packageResourcesFolder = package.AssetsFolder;

        if (result == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            var dl = ImGui.GetForegroundDrawList();
            ReadOnlySpan<char> label = data != null && FileImport.AreAllPathsInLinkedFolders(data)
                                           ? "Reference files from linked folder..."
                                           : $"""
                                              Import files to...
                                              {packageResourcesFolder}
                                              """;
            var labelSize = ImGui.CalcTextSize(label);
            var mousePos = ImGui.GetMousePos() + new Vector2(-30, -40);
            var area = ImRect.RectWithSize(mousePos, labelSize);
            area.Expand(10);
            dl.AddRectFilled(area.Min, area.Max, UiColors.BackgroundFull.Fade(0.7f), 5);

            dl.AddText(mousePos, UiColors.ForegroundFull, label);
            return true;
        }

        if (result != DragAndDropHandling.DragInteractionResult.Dropped
            || data == null)
            return false;

        var filePaths = data.Split("|");

        var dropOffset = Vector2.Zero;

        foreach (var filepath in filePaths)
        {
            if (!FileImport.TryImportDroppedFile(filepath, package,null, out var asset))
                continue;

            if (!CreateAssetOperatorOnGraph(context, asset, dropOffset))
                return false;

            dropOffset += new Vector2(20, 100);
        }

        return false;
    }

    private static void DrawDropPreviewItem(Asset asset, float fade = 1)
    {
        if (asset.AssetType.PrimaryOperators.Count == 0)
            return;

        if (!SymbolUiRegistry.TryGetSymbolUi(asset.AssetType.PrimaryOperators[0], out var mainSymbolUi))
        {
            return;
        }

        DrawDropPreviewItem(mainSymbolUi.Symbol, fade);
    }

    private static void DrawDropPreviewItem(Symbol symbol, float fade = 1)
    {
        var color = symbol.OutputDefinitions.Count > 0
                        ? TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color
                        : UiColors.Gray;

        // Foreground list so the preview isn't clipped at the graph window's edges.
        var dl = ImGui.GetForegroundDrawList();
        var pos = ImGui.GetMousePos();
        dl.AddRectFilled(pos, pos + MagGraphItem.GridSize, color.Fade(fade), 4);
    }

    private static bool CreateAssetOperatorOnGraph(GraphUiContext context,
                                                   Asset asset,
                                                   Vector2 dropOffset)
    {
        if (asset.AssetType.PrimaryOperators.Count == 0)
        {
            Log.Warning($"{asset.Address} of type {asset.AssetType} has no matching operator symbols");
            return false;
        }

        if (!TryCreateSymbolInstanceOnGraph(context, asset.AssetType.PrimaryOperators[0], dropOffset, out var newInstance))
        {
            Log.Warning("Failed to create operator instance");
            return false;
        }

        if (!SymbolAnalysis.TryGetFileInputFromInstance(newInstance, out var stringInput, out _))
        {
            Log.Warning("Failed to get file path parameter from op");
            return false;
        }

        Log.Debug($"Created {newInstance} with {asset.Address}", newInstance);

        stringInput.TypedInputValue.Assign(new InputValue<string>(asset.Address));
        stringInput.DirtyFlag.ForceInvalidate();
        stringInput.Parent.Parent?.Symbol.InvalidateInputInAllChildInstances(stringInput);
        stringInput.Input.IsDefault = false;

        var parent = stringInput.Parent.Parent;
        if (parent == null)
            return false;
        
        AssetRegistry.AddAssetReference(asset,
                                        symbolId: parent.Symbol.Id,
                                        stringInput.Parent.SymbolChildId,
                                        stringInput.Id);

        return true;
    }

    private static bool TryCreateSymbolInstanceOnGraph(GraphUiContext context,
                                                       Guid guid,
                                                       Vector2 offsetInScreen,
                                                       [NotNullWhen(true)] out Instance? newInstance)
    {
        newInstance = null;
        if (SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
        {
            var symbol = symbolUi.Symbol;
            var posOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos() + offsetInScreen);
            if (!SymbolUiRegistry.TryGetSymbolUi(context.CompositionInstance.Symbol.Id, out var compositionOpSymbolUi))
            {
                Log.Warning("Failed to get symbol id for " + context.CompositionInstance.SymbolChildId);
                return false;
            }

            var childUi = GraphOperations.AddSymbolChild(symbol, compositionOpSymbolUi, posOnCanvas);
            newInstance = context.CompositionInstance.Children[childUi.Id];
            context.Selector.SetSelection(childUi, newInstance);
            context.Layout.FlagStructureAsChanged();
            return true;
        }

        Log.Warning($"Symbol {guid} not found in registry");
        return false;
    }
}