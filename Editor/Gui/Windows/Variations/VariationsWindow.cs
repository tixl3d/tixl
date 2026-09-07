using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Variations;

[HelpUiID("VariationWindow")]
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

    private int _selectedNodeCount;
    
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
        
        if (FrameStats.SelectionChanged)
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

        if (FrameStats.SelectionChanged || FrameStats.WindowLayoutChanged)
        {
            _presetCanvas.RefreshView();
            _snapshotCanvas.RefreshView();
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
                                 ImGuiChildFlags.None,
                                 ImGuiWindowFlags.NoScrollbar);
                
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

                DrawHeaderIcons();

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

    /// <summary>
    /// Right-aligned cluster with the preview mode toggles and the documentation button.
    /// </summary>
    private void DrawHeaderIcons()
    {
        var iconSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        CustomComponents.RightAlign(iconSize.X * 3 + spacing * 2);

        CustomComponents.ToggleTwoIconsButton(ref UserSettings.Config.VariationHoverPreview,
                                              iconOff:Icon.HoverScrub,
                                              iconOn:Icon.HoverScrub,
                                              CustomComponents.ButtonStates.Activated,
                                              CustomComponents.ButtonStates.Default,
                                              noBackground:true);

        CustomComponents.TooltipForLastItem("Preview on hover",
                                            "Hovering a thumbnail temporarily applies the variation to the output.");

        ImGui.SameLine();
        var liveThumbnails = UserSettings.Config.VariationLiveThumbnails;
        if (CustomComponents.ToggleTwoIconsButton(ref liveThumbnails,
                                                  iconOff:Icon.AutoRefresh,
                                                  iconOn:Icon.AutoRefresh,
                                                  CustomComponents.ButtonStates.Activated,
                                                  CustomComponents.ButtonStates.Default,
                                                  noBackground:true))
        {
            _presetCanvas.EnableLiveThumbnails(liveThumbnails);
            _snapshotCanvas.EnableLiveThumbnails(liveThumbnails);
        }

        CustomComponents.TooltipForLastItem("Live render previews",
                                            "Continuously renders thumbnails for the pinned output.\nDisabling restores the default thumbnails.");

        ImGui.SameLine();
        DocumentationButton.Draw("Variations", DocumentationWikiUrl, iconSize);
    }

    private const string DocumentationWikiUrl = "https://github.com/tixl3d/tixl/wiki/help.PresetsAndSnapshots";

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