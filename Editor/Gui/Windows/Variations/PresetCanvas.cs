#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.Variations;

internal class PresetCanvas : VariationBaseCanvas
{
    public virtual void DrawToolbarFunctions()
    {
        var s = ImGui.GetFrameHeight();
        if (VariationHandling.ActivePoolForPresets == null)
            return;
            
        if (CustomComponents.IconButton(Icon.Plus, new Vector2(s, s)))
        {
            CreatePreset();
        }
    }

    protected override string GetTitle()
    {
        if (VariationHandling.ActiveInstanceForPresets == null)
            return "";
            
        return $"...for {VariationHandling.ActiveInstanceForPresets?.Symbol.Name}";
    }

    private protected override Instance? InstanceForBlendOperations => VariationHandling.ActiveInstanceForPresets;
    private protected override SymbolVariationPool? PoolForBlendOperations => VariationHandling.ActivePoolForPresets;

    protected override void DrawAdditionalContextMenuContent(Instance instance)
    {
        ImGui.GetForegroundDrawList().AddRect(_keepWindowPos, _keepWindowPos+ _keepWindowSize, Color.Red);
        
    }

    private void CreatePreset()
    {
        if (VariationHandling.ActivePoolForPresets == null || VariationHandling.ActiveInstanceForPresets == null)
        {
            Log.Warning("Can't create preset without variation pool or active instance");
            return;
        }
        
        var newVariation = VariationHandling.ActivePoolForPresets.CreatePresetForInstanceSymbol(VariationHandling.ActiveInstanceForPresets);
        if(VariationHandling.ActivePoolForPresets.AllVariations.Count > 1) 
            newVariation.PosOnCanvas = FindFreePositionForNewThumbnail(VariationHandling.ActivePoolForPresets.AllVariations);
        
        VariationThumbnail.VariationForRenaming = newVariation;
        VariationHandling.ActivePoolForPresets.SaveVariationsToFile();

        CanvasElementSelection.SetSelection(newVariation);
        _keepWindowPos = ImGui.GetWindowContentRegionMin( ) + ImGui.GetWindowPos();
        _keepWindowSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
        RequestResetView();
        TriggerThumbnailUpdate();
    }

    private Vector2 _keepWindowPos;
    private Vector2 _keepWindowSize;
}