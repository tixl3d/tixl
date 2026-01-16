using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Variations;

internal sealed class VariationsWindow : Window
{
    public VariationsWindow()
    {
        _presetCanvas = new PresetCanvas();
        _snapshotCanvas = new SnapshotCanvas();
        Config.Title = "Variations";
        MenuTitle = "Presets";
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    protected override void DrawContent()
    {
        DrawWindowContent();
    }

    private InteractionModes _interactionMode = InteractionModes.Presets;

    private int _selectedNodeCount = 0;

    public void DrawWindowContent(bool hideHeader = false)
    {
        // Delete actions need be deferred to prevent collection modification during iteration
        if (_variationsToBeDeletedNextFrame.Count > 0)
        {
            _poolWithVariationToBeDeleted.DeleteVariations(_variationsToBeDeletedNextFrame);
            _variationsToBeDeletedNextFrame.Clear();
        }

        var components = ProjectView.Focused;
        if (components == null)
            return;

        var nodeSelection = components.NodeSelection;

        var compositionHasVariations = VariationHandling.ActivePoolForSnapshots != null && VariationHandling.ActivePoolForSnapshots.AllVariations.Count > 0;
        var oneChildSelected = nodeSelection.Selection.Count == 1;
        var selectionChanged = nodeSelection.Selection.Count != _selectedNodeCount;

        if (selectionChanged)
        {
            _selectedNodeCount = nodeSelection.Selection.Count;

            if (oneChildSelected)
            {
                _interactionMode = InteractionModes.Presets;
            }
            else if (compositionHasVariations && _selectedNodeCount == 0)
            {
                _interactionMode = InteractionModes.Snapshots;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        var topLeftCorner = ImGui.GetCursorScreenPos();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        {
            if (!hideHeader)
            {
                ImGui.BeginChild("header",
                                 new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                                 false,
                                 ImGuiWindowFlags.NoScrollbar);

                // var viewModeIndex = (int)_interactionMode;

                //if(CustomComponents.DrawSegmentedToggle())

                if (FormInputs.SegmentedButton(ref _interactionMode))
                {
                    // _interactionMode = (InteractionModes)viewModeIndex;
                    //_presetCanvas.RefreshView();
                    //_snapshotCanvas.RefreshView();
                }

                ImGui.SameLine();
                ImGui.Dummy(new Vector2(10, 10));
                ImGui.SameLine();
                switch (_interactionMode)
                {
                    case InteractionModes.Presets:
                        _presetCanvas.DrawToolbarFunctions();
                        break;

                    case InteractionModes.Snapshots:
                        _snapshotCanvas.DrawToolbarFunctions();
                        break;
                }

                ImGui.EndChild();
            }
        }

        drawList.ChannelsSetCurrent(0);
        {
            switch (_interactionMode)
            {
                case InteractionModes.Presets:
                    ImGui.SetCursorScreenPos(topLeftCorner);

                    if (VariationHandling.ActivePoolForPresets == null
                        || VariationHandling.ActiveInstanceForPresets == null
                        || VariationHandling.ActivePoolForPresets.AllVariations.Count == 0)
                    {
                        CustomComponents.EmptyWindowMessage("No presets yet.");
                    }
                    else
                    {
                        _presetCanvas.DrawBaseCanvas(drawList, hideHeader);
                    }

                    break;
                case InteractionModes.Snapshots:
                    ImGui.SetCursorScreenPos(topLeftCorner);

                    if (VariationHandling.ActivePoolForSnapshots == null
                        || VariationHandling.ActiveInstanceForSnapshots == null
                        || VariationHandling.ActivePoolForSnapshots.AllVariations.Count == 0)
                    {
                        var additionalHint = "";
                        if (VariationHandling.ActiveInstanceForSnapshots != null
                            && SymbolUiRegistry.TryGetSymbolUi(VariationHandling.ActiveInstanceForSnapshots.Symbol.Id, out var childUi)
                            && !childUi.ChildUis.Values.Any(s => s.SnapshotGroupIndex > 0))
                        {
                            additionalHint = "Use the graph window context menu\nto activate snapshots for operators.";
                        }

                        if (CustomComponents
                           .EmptyWindowMessage("No Snapshots yet.\n\nWith snapshots you can switch or blend\nbetween parameter sets in your composition.\n\n"
                                               + additionalHint, "Learn More"))
                        {
                            var url = "https://github.com/tixl3d/tixl/wiki/help.PresetsAndSnapshots";
                            CoreUi.Instance.OpenWithDefaultApplication(url);
                        }
                        // }
                    }
                    else
                    {
                        _snapshotCanvas.DrawBaseCanvas(drawList);
                    }

                    break;
            }
        }

        drawList.ChannelsMerge();
    }

    private enum InteractionModes
    {
        Presets,
        Snapshots,
    }

    private static readonly List<string> _options = new() { "Presets", "Snapshots" };

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }

    public static void DeleteVariationsFromPool(SymbolVariationPool pool, IEnumerable<Variation> selectionSelection)
    {
        _poolWithVariationToBeDeleted = pool;
        _variationsToBeDeletedNextFrame.AddRange(selectionSelection); // TODO: mixing Snapshots and variations in same list is dangerous
        pool.StopHover();
        pool.SaveVariationsToFile();
    }

    private static readonly List<Variation> _variationsToBeDeletedNextFrame = new(20);
    private static SymbolVariationPool _poolWithVariationToBeDeleted;
    private readonly PresetCanvas _presetCanvas;
    private readonly SnapshotCanvas _snapshotCanvas;
}