#nullable enable
using ImGuiNET;
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
        //ImGui.GetForegroundDrawList().AddRect(_keepWindowPos, _keepWindowPos+ _keepWindowSize, Color.Red);
    }

    /// <summary>
    /// Creates a preset from the active instance's current values, places it on a free canvas spot
    /// and activates it — shared by the Variations canvas and the parameter window, so both entry
    /// points behave identically (the canvas doesn't need to be visible).
    /// </summary>
    internal static Variation? CreatePresetForActivePool()
    {
        var pool = VariationHandling.ActivePoolForPresets;
        var instance = VariationHandling.ActiveInstanceForPresets;
        if (pool == null || instance == null)
        {
            Log.Warning("Can't create preset without variation pool or active instance");
            return null;
        }

        var newVariation = pool.CreatePresetForInstanceSymbol(instance);
        if (pool.AllVariations.Count > 1)
            newVariation.PosOnCanvas = FindFreePositionForNewThumbnail(pool.AllVariations);

        // The preset was built from the current values, so applying it would only push a no-op
        // parameter command onto the undo stack.
        pool.SetActiveVariationWithoutApply(newVariation);
        pool.SaveVariationsToFile();
        return newVariation;
    }

    private void CreatePreset()
    {
        var newVariation = CreatePresetForActivePool();
        if (newVariation == null)
            return;

        VariationThumbnail.VariationForRenaming = newVariation;
        CanvasElementSelection.SetSelection(newVariation);

        _keepWindowPos = ImGui.GetWindowPos();
        _keepWindowSize = ImGui.GetWindowSize();
        RequestResetView();
        TriggerThumbnailSave();
    }

    private Vector2 _keepWindowPos;
    private Vector2 _keepWindowSize;
}