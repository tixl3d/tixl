#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Ui;

/// <summary>
/// Handles dropping items onto graph. 
/// </summary>
internal static class DropHandling
{
    internal static void HandleDropping(GraphUiContext context)
    {
        if (!DragAndDropHandling.IsDragging)
            return;

        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.InvisibleButton("## drop", ImGui.GetWindowSize());

        if (HandleDropSymbol(context))
            return;

        if (HandleDropExternalFile(context))
            return;

        HandleDropFileAsset(context);
    }

    private static void HandleDropFileAsset(GraphUiContext context)
    {
        DragAndDropHandling.TryHandleItemDrop(DragAndDropHandling.DragTypes.FileAsset, out var aliasPath, out var assetResult);

        if ((assetResult != DragAndDropHandling.DragInteractionResult.Hovering
             && assetResult != DragAndDropHandling.DragInteractionResult.Dropped)
            || aliasPath == null) return;

        if (!AssetLibrary.GetAssetFromAliasPath(aliasPath, out var asset))
        {
            Log.Warning($"Can't get asset for {aliasPath}");
            return;
        }

        var assetType = asset.AssetType;
        if (assetType == null)
        {
            Log.Warning($"{aliasPath} has no asset type");
            return;
        }

        if (assetResult == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            DrawDropPreviewItem(asset);
        }
        else
        {
            CreateAssetOperator(context, assetType, aliasPath, Vector2.Zero);
        }
    }



    private static bool HandleDropSymbol(GraphUiContext context)
    {
        DragAndDropHandling.TryHandleItemDrop(DragAndDropHandling.DragTypes.Symbol, out var data, out var result);

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
        DragAndDropHandling.TryHandleItemDrop(DragAndDropHandling.DragTypes.ExternalFile, out var data, out var result);

        var packageResourcesFolder = ProjectView.Focused?.OpenedProject.Package.ResourcesFolder;

        if (result == DragAndDropHandling.DragInteractionResult.Hovering)
        {
            var dl = ImGui.GetForegroundDrawList();
            ReadOnlySpan<char> label = $"""
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

        if (result == DragAndDropHandling.DragInteractionResult.Dropped
            && data != null
            && packageResourcesFolder != null)
        {
            var filePaths = data.Split("|");
            var fileCount = filePaths.Length;

            var dropOffset = Vector2.Zero;
            
            foreach (var filepath in filePaths)
            {
                if (!Path.Exists(filepath))
                    continue;

                var fileName = Path.GetFileName(filepath);
                var destFilepath = Path.Combine(packageResourcesFolder, fileName);

                // 
                if (!File.Exists(destFilepath))
                {
                    // Copy to project first...
                    //var fileName = Path.GetFileName(filepath);
                    //var destFileName = Path.Combine(packageResourcesFolder, fileName);
                    try
                    {
                        File.Copy(filepath, destFilepath);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to copy to {destFilepath}");
                        continue;
                    }

                    Log.Debug($"Copied {fileName} to {packageResourcesFolder}");


                }
                else
                {
                    Log.Debug("Already project asset: " + filepath);

                    // var composition = context.CompositionInstance;
                    // if (!ResourceManager.TryConstructAssetAddress(filepath, composition, out var address))
                    // {
                    //     Log.Warning($"Can't address for {filepath}");
                    //     return false;
                    // }
                    //
                    // //ResourceManager.TryResolveRelativePath(destFilepath, ProjectView.Focused!.CompositionInstance, out var absolutePath, out _);
                    //
                    // if (!AssetLibrary.GetAssetFromAliasPath(address, out var asset))
                    // {
                    //     Log.Warning($"Can't get asset for {address}");
                    //     return false;
                    // }
                    //
                    // if (asset.AssetType == null)
                    // {
                    //     Log.Warning($"{filepath} has no asset type");
                    //     return true;
                    // }
                    //
                    // Log.Debug("! Would create op " + asset);
                    // return true;
                }
                
                if (!AssetTypeRegistry.TryGetFromFilePath(destFilepath, out var assetType))
                {
                    Log.Warning("Can't find this asset type.");
                    continue;
                }

                if (!ResourceManager.TryConstructAssetAddress(destFilepath, context.CompositionInstance, out var uri))
                {
                    Log.Warning($"Can't construct uri for {destFilepath}");
                    continue;
                }

                if (!CreateAssetOperator(context, assetType, uri, dropOffset))
                    continue;
                    
                dropOffset += new Vector2(20, 100);                
            }
        }
        return false;
    }

    private static void DrawDropPreviewItem(AssetItem asset)
    {
        if (asset.AssetType == null)
            return;

        if (asset.AssetType.PrimaryOperators.Count == 0)
            return;

        if (!SymbolUiRegistry.TryGetSymbolUi(asset.AssetType.PrimaryOperators[0], out var mainSymbolUi))
        {
            return;
        }

        var color = mainSymbolUi.Symbol.OutputDefinitions.Count > 0
                        ? TypeUiRegistry.GetPropertiesForType(mainSymbolUi.Symbol.OutputDefinitions[0]?.ValueType).Color
                        : UiColors.Gray;

        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetMousePos();
        dl.AddRectFilled(pos, pos + MagGraphItem.GridSize, color, 4);
    }

        
    private static bool CreateAssetOperator(GraphUiContext context, AssetTypeRegistry.AssetType assetType, string aliasPath, Vector2 dropOffset)
    {
        if (assetType.PrimaryOperators.Count == 0)
        {
            Log.Warning($"{aliasPath} of type {assetType} has no matching operator symbols");
            return false;
        }

        if (!TryCreateSymbolInstanceOnGraph(context, assetType.PrimaryOperators[0], dropOffset,  out var newInstance))
        {
            Log.Warning("Failed to create operator instance");
            return false;
        }

        if (!AssetLibrary.TryGetFileInputFromInstance(newInstance, out var stringInput, out _))
        {
            Log.Warning("Failed to get file path parameter from op");
            return false;
        }

        Log.Debug($"Created {newInstance} with {aliasPath}", newInstance);

        stringInput.TypedInputValue.Assign(new InputValue<string>(aliasPath));
        stringInput.DirtyFlag.ForceInvalidate();
        stringInput.Parent.Parent?.Symbol.InvalidateInputInAllChildInstances(stringInput);
        stringInput.Input.IsDefault = false;
        return true;
    }    
    
    private static bool TryCreateSymbolInstanceOnGraph(GraphUiContext context, Guid guid, Vector2 offsetInScreen, [NotNullWhen(true)] out Instance? newInstance)
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