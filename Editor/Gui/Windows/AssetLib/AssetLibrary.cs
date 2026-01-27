#nullable enable

using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Shows a tree of all defined symbols sorted by namespace 
/// </summary>
internal sealed partial class AssetLibrary : Window
{
    internal AssetLibrary()
    {
        _state.SearchString = "";
        Config.Title = "Assets";
    }

    internal override List<Window> GetInstances()
    {
        return [];
    }

    protected override void DrawContent()
    {
        // Init current frame
        UpdateAssetsIfRequired();
        if (_state.Composition == null)
            return;

        if (!NodeSelection.TryGetSelectedInstanceOrInput(out var selectedInstance, out _, out var selectionChanged))
        {
            selectedInstance = _state.Composition;
        }

        UpdateActiveSelection(selectedInstance);

        // Draw
        DrawLibContent();
    }

    private void UpdateAssetsIfRequired()
    {
        _state.Composition = ProjectView.Focused?.CompositionInstance;
        if (_state.Composition == null)
            return;

        var needsUpdate = _state.LastFileWatcherState != ResourceFileWatcher.FileStateChangeCounter
                || Core.Utils.Utilities.HasObjectChanged(_state.Composition, ref _lastCompositionObjId)
                || _state.FilteringNeedsUpdate
                || _state.SearchStringChanged;
        
        if (!needsUpdate)
            return;

        _state.TreeHandler.Reset();
        _state.SearchStringChanged = false;
        _state.LastFileWatcherState = ResourceFileWatcher.FileStateChangeCounter;

        _state.AllAssets.Clear();
        AssetTypeUseCounter.ClearMatchingFileCounts();

        var unfilteredAssets = AssetRegistry.AllAssets
                                            .Where(MatchFilters);

        // Sorting by Address ensures they are grouped by Package then by Path
        var sortedAssets = unfilteredAssets.OrderBy(a => a.Address).ToList();

        foreach (var asset in sortedAssets)
        {
            _state.AllAssets.Add(asset);
            AssetTypeUseCounter.IncrementUseCount(asset.AssetType);
        }

        AssetFolder.PopulateCompleteTree(_state, filterAction: null);
    }

    private static bool MatchFilters(Asset asset)
    {
        // Apply filters (Search, Compatibility, etc.)
        if (asset.IsDirectory)
            return true;
        
        return 
               string.IsNullOrEmpty(_state.SearchString) ||
               asset.Address.Contains(_state.SearchString, StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateActiveSelection(Instance selectedInstance)
    {
        _state.HasActiveInstanceChanged = selectedInstance != _state.ActiveInstance;
        var needsUpdate = _state.HasActiveInstanceChanged || _state.SearchStringChanged;
        if (!needsUpdate)
            return;

        _state.TimeActiveInstanceChanged = ImGui.GetTime();
        
        _state.ActiveInstance = selectedInstance;
        _state.ActivePathInput = null;
        _state.ActiveAssetAddress = null;
        _state.SearchStringChanged = false;
        _state.CompatibleExtensionIds.Clear();

        // Check if active instance has asset reference...
        if (SymbolAnalysis.TryGetFileInputFromInstance(selectedInstance, out _state.ActivePathInput, out var stringInputUi))
        {
            _state.ActiveAssetAddress = _state.ActivePathInput.GetCurrentValue();
            _state.CompatibleExtensionIds = AssetRegistry.TryGetAsset(_state.ActiveAssetAddress, out _state.ActiveAsset) 
                                                ? _state.ActiveAsset.AssetType.ExtensionIds.ToList()// Copy to prevent accident changes   
                                                : FileExtensionRegistry.GetExtensionIdsFromExtensionSetString(stringInputUi.FileFilter);

            if (!UserSettings.Config.SyncWithOperatorSelection) 
                return;
            
            _state.ActiveTypeFilters.Clear();
            foreach (var assetType in AssetType.AvailableTypes)
            {
                foreach (var extId in _state.CompatibleExtensionIds)
                {
                    if (!assetType.ExtensionIds.Contains(extId))
                        continue;

                    _state.ActiveTypeFilters.Add(assetType);
                    break;
                }
            }
        }
        else
        {
            _state.ActiveAsset = null;
            _state.ActiveAssetAddress = null;
            _state.ActivePathInput = null;
            _state.ActiveTypeFilters.Clear();
        }
        _state.RootFolder.UpdateMatchingAssetCounts(_state.CompatibleExtensionIds, _state.SearchString);
    }

    private int? _lastCompositionObjId = 0;

    private static readonly AssetLibState _state = new();
}