#nullable enable
using ImGuiNET;
using Newtonsoft.Json;
using T3.Core.Output;
using T3.Core.Settings;
using T3.Editor.Gui.Styling;
using T3.Serialization;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Projector models with the lens values a solve needs: throw ratio and lens shift. Picking one fills an output's
/// lens shift and zoom; the current values can be saved as a new one. The user's own presets are kept in their
/// settings, shared by every project like the output window's resolutions; a researched list of common models ships
/// with the editor, read-only.
/// </summary>
internal static class ProjectorPresets
{
    public sealed class Preset
    {
        public string Manufacturer = string.Empty;
        public string Model = string.Empty;

        /// <summary>Throw distance over picture width at the zoom's wide and tele end; equal for a fixed lens.</summary>
        public float ThrowRatioMin = 1.5f;

        public float ThrowRatioMax = 1.5f;

        /// <summary>The picture's shift off the lens axis in percent of its width and height, right and up.</summary>
        public float LensShiftPercentX;

        public float LensShiftPercentY;

        /// <summary>The highest resolution the projector accepts, which the output's display is best set to; 0 when unknown.</summary>
        public int ResolutionWidth;

        public int ResolutionHeight;

        /// <summary>Where the values come from, for a shipped preset: a spec sheet or manual.</summary>
        public string Source = string.Empty;

        /// <summary>What to know when using it, such as an adjustable lens shift the preset can't know the setting of.</summary>
        public string Note = string.Empty;

        [JsonIgnore]
        public string Title => string.IsNullOrWhiteSpace(Manufacturer) ? Model : $"{Manufacturer} {Model}";

        /** Shown beside the name in the dropdown; built once, as the menu is drawn every frame it's open. */
        [JsonIgnore]
        public string ResolutionLabel => _resolutionLabel ??= ResolutionWidth > 0 && ResolutionHeight > 0 ? $"{ResolutionWidth}×{ResolutionHeight}" : string.Empty;

        private string? _resolutionLabel;
    }

    public static List<Preset> All => _presets ??= JsonUtils.TryLoadingJson<List<Preset>>(_filePath) ?? [];

    /// <summary>The researched list that ships with the editor, sorted by manufacturer; never written.</summary>
    public static List<Preset> BuiltIn => _builtIn ??= LoadBuiltIn();

    /// <summary>The model dropdown of an output's projector section. Call every frame the section is shown.</summary>
    public static void DrawSelector(Setup setup, OutputDefinition output)
    {
        _saveDialog.Draw(setup, output);

        var camera = output.Camera;
        var current = string.IsNullOrEmpty(camera?.ModelName) ? "Unknown" : camera!.ModelName;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 30 * T3Ui.UiScaleFactor);
        if (!ImGui.BeginCombo("##projectorModel", current, ImGuiComboFlags.HeightLargest))
            return;

        if (CustomComponents.DrawMenuItem(SavePresetId, "Save as Preset...", reserveIconColumn: false))
            _saveDialog.Show(output);

        if (All.Count > 0 || BuiltIn.Count > 0)
            CustomComponents.SeparatorLine();

        for (var index = 0; index < All.Count; index++)
        {
            var preset = All[index];
            ImGui.PushID(index);
            if (CustomComponents.DrawMenuItem(index, preset.Title, preset.ResolutionLabel, isChecked: preset.Title == camera?.ModelName, reserveIconColumn: false))
                Apply(setup, output, preset);

            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    if (CustomComponents.DrawMenuItem(RemovePresetId, "Remove", reserveIconColumn: false))
                                                    {
                                                        All.Remove(preset);
                                                        Save();
                                                    }
                                                },
                                                "##presetMenu");
            ImGui.PopID();
        }

        DrawBuiltInMenus(setup, output, camera);
        ImGui.EndCombo();
    }

    /** One submenu per manufacturer for the shipped presets, each model with its source in a tooltip. */
    private static void DrawBuiltInMenus(Setup setup, OutputDefinition output, OutputDefinition.ProjectorCamera? camera)
    {
        if (BuiltIn.Count == 0)
            return;

        if (All.Count > 0)
            CustomComponents.SeparatorLine();

        var index = 0;
        while (index < BuiltIn.Count)
        {
            var manufacturer = BuiltIn[index].Manufacturer;
            var isOpen = CustomComponents.DrawSubMenu(BuiltInMenuId + index, manufacturer, reserveCheckmarkColumn: false);
            for (; index < BuiltIn.Count && BuiltIn[index].Manufacturer == manufacturer; index++)
            {
                if (!isOpen)
                    continue;

                var preset = BuiltIn[index];
                if (CustomComponents.DrawMenuItem(BuiltInMenuId + 1000 + index, preset.Model, preset.ResolutionLabel, isChecked: preset.Title == camera?.ModelName,
                                                  reserveIconColumn: false))
                    Apply(setup, output, preset);

                if (ImGui.IsItemHovered() && (preset.Note.Length > 0 || preset.Source.Length > 0))
                    CustomComponents.TooltipForLastItem(preset.Note.Length > 0 ? preset.Note : preset.Title, preset.Source);
            }

            if (isOpen)
                ImGui.EndMenu();
        }
    }

    private static List<Preset> LoadBuiltIn()
    {
        var presets = JsonUtils.TryLoadingJson<List<Preset>>(_builtInFilePath) ?? [];
        presets.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return presets;
    }

    /// <summary>Sets the output's lens shift and zoom range from a model, as one undo step, with the zoom in the middle of it.</summary>
    public static void Apply(Setup setup, OutputDefinition output, Preset preset)
    {
        var aspect = AspectOf(output);
        SetupUndo.RunUndoable($"Use {preset.Title}", setup, () =>
                                                           {
                                                               var camera = output.Camera ??= new OutputDefinition.ProjectorCamera();
                                                               camera.ModelName = preset.Title;
                                                               camera.LensAxis = new Vector2(preset.LensShiftPercentX, preset.LensShiftPercentY) / -50;
                                                               camera.ZoomRangeDegrees = new Vector2(VerticalFieldOfViewDegrees(preset.ThrowRatioMin, aspect),
                                                                                                     VerticalFieldOfViewDegrees(preset.ThrowRatioMax, aspect));
                                                               SetZoomPosition(camera, 0.5f, aspect);
                                                           });
    }

    public static float AspectOf(OutputDefinition output) => output.CanvasSize.X / MathF.Max(output.CanvasSize.Y, 1);

    /// <summary>
    /// Where the zoom sits between the wide (0) and the tele (1) end, measured in throw ratio, which is how a zoom ring
    /// scales the picture. 0 for a fixed lens.
    /// </summary>
    public static float ZoomPosition(OutputDefinition.ProjectorCamera camera, float aspect)
    {
        var wide = ThrowRatio(camera.ZoomRangeDegrees.X, aspect);
        var tele = ThrowRatio(camera.ZoomRangeDegrees.Y, aspect);
        if (MathF.Abs(tele - wide) < 0.0001f)
            return 0;

        return Math.Clamp((ThrowRatio(camera.ManualFovYDegrees, aspect) - wide) / (tele - wide), 0, 1);
    }

    /// <summary>Orders the zoom range wide end first (the larger field of view, the smaller throw ratio).</summary>
    public static void SortZoomRange(OutputDefinition.ProjectorCamera camera)
    {
        var range = camera.ZoomRangeDegrees;
        if (range.X < range.Y)
            camera.ZoomRangeDegrees = new Vector2(range.Y, range.X);
    }

    /// <summary>Puts the zoom at a position between the wide (0) and the tele (1) end; the inverse of <see cref="ZoomPosition"/>.</summary>
    public static void SetZoomPosition(OutputDefinition.ProjectorCamera camera, float position, float aspect)
    {
        var wide = ThrowRatio(camera.ZoomRangeDegrees.X, aspect);
        var tele = ThrowRatio(camera.ZoomRangeDegrees.Y, aspect);
        camera.ManualFovYDegrees = VerticalFieldOfViewDegrees(wide + (tele - wide) * Math.Clamp(position, 0, 1), aspect);
    }

    /// <summary>Vertical field of view of a picture of <paramref name="aspect"/> thrown at <paramref name="throwRatio"/> (distance over width).</summary>
    public static float VerticalFieldOfViewDegrees(float throwRatio, float aspect)
    {
        var halfWidthPerMetre = 0.5f / MathF.Max(throwRatio, 0.01f);
        return 2 * MathF.Atan(halfWidthPerMetre / MathF.Max(aspect, 0.01f)) * 180 / MathF.PI;
    }

    /// <summary>The throw ratio a vertical field of view gives for a picture of <paramref name="aspect"/>: the inverse of <see cref="VerticalFieldOfViewDegrees"/>.</summary>
    public static float ThrowRatio(float verticalFieldOfViewDegrees, float aspect)
    {
        var halfHeightPerMetre = MathF.Tan(verticalFieldOfViewDegrees * MathF.PI / 360);
        return 0.5f / MathF.Max(halfHeightPerMetre * aspect, 0.0001f);
    }

    public static void Save()
    {
        JsonUtils.TrySaveJson(All, _filePath);
    }

    private static List<Preset>? _presets;
    private static List<Preset>? _builtIn;
    private static readonly string _builtInFilePath = System.IO.Path.Combine(T3.Core.Resource.SharedResources.EditorResourcesDirectory, "projector-presets.json");
    private static readonly int BuiltInMenuId = nameof(BuiltInMenuId).GetHashCode();
    private static readonly string _filePath = System.IO.Path.Combine(FileLocations.SettingsDirectory, "projector-presets.json");
    private static readonly SaveProjectorPresetDialog _saveDialog = new();
    private static readonly int SavePresetId = nameof(SavePresetId).GetHashCode();
    private static readonly int RemovePresetId = nameof(RemovePresetId).GetHashCode();
}
