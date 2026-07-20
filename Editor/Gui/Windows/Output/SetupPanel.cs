#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The output window's setup sidebar: setup switcher (panel title), outline tree of the
/// setup's entities with per-section add buttons, and the shared entity selection.
/// </summary>
internal static class SetupPanel
{
    public static void Draw(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            CustomComponents.EmptyWindowMessage("No project focused");
            return;
        }

        DrawSetupSwitcher(setup, selection);

        FormInputs.AddVerticalSpace(4);

        DrawSection("REFERENCE IMAGES", "##addRefImage", selection, AddReferenceImage);
        for (var i = 0; i < setup.ReferenceImages.Count; i++)
        {
            var image = setup.ReferenceImages[i];
            DrawEntityRow(selection, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, image.Name, null);
        }

        // Surfaces are shown nested under the output(s) they map to — a surface used on several
        // outputs (edge blending) appears under each, which makes its usage visible.
        DrawSection("OUTPUTS", "##addOutput", selection, AddOutput);
        for (var i = 0; i < setup.Outputs.Count; i++)
        {
            DrawOutputWithSurfaces(selection, setup, machineConfig, setup.Outputs[i]);
        }

        DrawUnassignedSurfaces(selection, setup);

        DrawSection("PROPS", "##addProp", selection, AddProp);
        for (var i = 0; i < setup.Props.Count; i++)
        {
            var prop = setup.Props[i];
            DrawEntityRow(selection, SetupEntitySelection.EntityKind.Prop, prop.Id, prop.Kind, null);
        }
    }

    private static void DrawOutputWithSurfaces(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig, OutputDefinition output)
    {
        var binding = machineConfig.TryGetBinding(output.Id);
        var status = binding == null ? null : $"Display {binding.DisplayIndex + 1}";
        var outputId = output.Id;
        DrawEntityRow(selection, SetupEntitySelection.EntityKind.Output, output.Id, output.Name, status,
                      onDelete: () => DeleteOutput(setup, machineConfig, outputId));

        ImGui.Indent(12 * T3Ui.UiScaleFactor);
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (!surface.OutputMappings.Exists(m => m.OutputId == output.Id))
                continue;

            var surfaceId = surface.Id;
            DrawEntityRow(selection, SetupEntitySelection.EntityKind.Surface, surface.Id, surface.Name, null,
                          onDelete: () => DeleteSurface(setup, surfaceId),
                          onRemoveFromOutput: () => RemoveMapping(setup, surfaceId, outputId));
        }

        ImGui.PushID(output.Id.GetHashCode());
        ImGui.Indent(8 * T3Ui.UiScaleFactor);
        if (ImGui.SmallButton("+ Surface"))
        {
            AddSurfaceToOutput(setup, output, selection);
            OutputSetupHandling.SaveActive();
        }

        ImGui.Unindent(8 * T3Ui.UiScaleFactor);
        ImGui.PopID();
        ImGui.Unindent(12 * T3Ui.UiScaleFactor);
    }

    private static void DrawUnassignedSurfaces(SetupEntitySelection selection, Setup setup)
    {
        if (!setup.Surfaces.Exists(s => s.OutputMappings.Count == 0))
            return;

        FormInputs.AddVerticalSpace(6);
        CustomComponents.StylizedText("UNASSIGNED SURFACES", Fonts.FontSmall, UiColors.TextMuted);
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.OutputMappings.Count != 0)
                continue;

            var surfaceId = surface.Id;
            DrawEntityRow(selection, SetupEntitySelection.EntityKind.Surface, surface.Id, surface.Name, null,
                          onDelete: () => DeleteSurface(setup, surfaceId));
        }
    }

    /// <summary>Info card shown in the output view for a selected/pinned setup entity.</summary>
    public static void DrawEntityCard(SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        ImGui.SetCursorPos(ImGui.GetWindowSize() * 0.5f - new Vector2(120, 60) * T3Ui.UiScaleFactor);
        ImGui.BeginGroup();
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                var image = setup.ReferenceImages.Find(e => e.Id == id);
                if (image != null)
                {
                    CustomComponents.StylizedText($"Reference Image · {image.Kind}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(image.Name, Fonts.FontLarge, UiColors.Text);
                    var fileInfo = string.IsNullOrEmpty(image.FilePath)
                                       ? "Drop a photo here, or pick an asset"
                                       : $"{image.FilePath}  ({image.Width}×{image.Height})";
                    CustomComponents.StylizedText(fileInfo, Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.Surfaces.Find(e => e.Id == id);
                if (surface != null)
                {
                    CustomComponents.StylizedText($"Surface · {surface.Type}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(surface.Name, Fonts.FontLarge, UiColors.Text);
                    CustomComponents.StylizedText($"{surface.SizeInMeters.X:0.##} × {surface.SizeInMeters.Y:0.##} m · {surface.PixelsPerMeter:0} px/m",
                                                  Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Prop:
            {
                var prop = setup.Props.Find(e => e.Id == id);
                if (prop != null)
                {
                    CustomComponents.StylizedText("Prop", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(prop.Kind, Fonts.FontLarge, UiColors.Text);
                    CustomComponents.StylizedText($"{prop.HeightInMeters:0.##} m", Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.Outputs.Find(e => e.Id == id);
                if (output != null)
                {
                    CustomComponents.StylizedText($"Output · {output.Kind}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(output.Name, Fonts.FontLarge, UiColors.Text);
                    var binding = machineConfig.TryGetBinding(output.Id);
                    var bindingInfo = binding == null ? "unbound" : $"→ Display {binding.DisplayIndex + 1}";
                    CustomComponents.StylizedText($"{output.CanvasResolution.Width}×{output.CanvasResolution.Height} px · {bindingInfo}",
                                                  Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
        }

        ImGui.EndGroup();
    }

    private static void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection)
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##setupSwitcher", setup.Name, ImGuiComboFlags.HeightLargest))
        {
            CustomComponents.MenuGroupHeader("Setups");
            _availableNames.Clear();
            OutputSetupHandling.GetAvailableSetupNames(_availableNames);
            for (var i = 0; i < _availableNames.Count; i++)
            {
                var name = _availableNames[i];
                if (CustomComponents.DrawMenuItem(i, name, isChecked: name == setup.Name) && name != setup.Name)
                {
                    if (OutputSetupHandling.TrySwitchTo(name))
                        selection.Clear();
                }
            }

            CustomComponents.SeparatorLine();
            if (CustomComponents.DrawMenuItem(900, "Duplicate current"))
            {
                OutputSetupHandling.TryDuplicateActive(GetFreeName(setup.Name + " copy"));
            }
            CustomComponents.TooltipForLastItem("Duplicates the setup for another venue.",
                                                "Entity ids are preserved, so operator bindings stay intact.");

            if (CustomComponents.DrawMenuItem(901, "New (empty)"))
            {
                if (OutputSetupHandling.TryCreateNew(GetFreeName("Setup")))
                    selection.Clear();
            }
            CustomComponents.TooltipForLastItem("Creates a fresh setup with new entity ids.",
                                                "Operator bindings into it will be unresolved until re-assigned.");

            if (_availableNames.Count > 1 && CustomComponents.DrawMenuItem(902, "Delete"))
            {
                if (OutputSetupHandling.TryDeleteActive())
                    selection.Clear();
            }

            ImGui.EndCombo();
        }
    }

    private static void DrawSection(string title, string addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection> onAdd)
    {
        FormInputs.AddVerticalSpace(6);
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.TextMuted);
        CustomComponents.RightAlign(20 * T3Ui.UiScaleFactor);
        if (ImGui.SmallButton("+" + addButtonId))
        {
            onAdd(selection);
            OutputSetupHandling.SaveActive();
        }
    }

    private static void DrawEntityRow(SetupEntitySelection selection, SetupEntitySelection.EntityKind kind, Guid id, string name, string? status,
                                      Action? onDelete = null, Action? onRemoveFromOutput = null)
    {
        ImGui.PushID(id.GetHashCode());
        var isSelected = selection.IsSelected(kind, id);
        var label = string.IsNullOrEmpty(name) ? "untitled" : name;
        ImGui.Indent(8 * T3Ui.UiScaleFactor);
        if (ImGui.Selectable(label, isSelected))
        {
            selection.Select(kind, id);
        }

        if (onDelete != null || onRemoveFromOutput != null)
        {
            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    if (onRemoveFromOutput != null && CustomComponents.DrawMenuItem(1, "Remove from output"))
                                                        onRemoveFromOutput();

                                                    if (onDelete != null && CustomComponents.DrawMenuItem(2, "Delete"))
                                                        onDelete();
                                                },
                                                null);
        }

        if (status != null)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() * 0.55f);
            CustomComponents.StylizedText(status, Fonts.FontSmall, UiColors.TextMuted);
        }

        ImGui.Unindent(8 * T3Ui.UiScaleFactor);
        ImGui.PopID();
    }

    private static void AddReferenceImage(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var image = new ReferenceImage { Name = $"Image {setup.ReferenceImages.Count + 1}" };
        setup.ReferenceImages.Add(image);
        selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
    }

    private static void AddSurfaceToOutput(Setup setup, OutputDefinition output, SetupEntitySelection selection)
    {
        var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
        surface.OutputMappings.Add(CreateDefaultMapping(output));
        setup.Surfaces.Add(surface);
        selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
    }

    private static Surface.OutputMapping CreateDefaultMapping(OutputDefinition output)
    {
        float w = Math.Max(1, output.CanvasResolution.Width);
        float h = Math.Max(1, output.CanvasResolution.Height);
        float x0 = w * 0.2f, x1 = w * 0.8f, y0 = h * 0.2f, y1 = h * 0.8f;
        return new Surface.OutputMapping
                   {
                       OutputId = output.Id,
                       Quad = [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)],
                   };
    }

    private static void DeleteSurface(Setup setup, Guid surfaceId)
    {
        setup.Surfaces.RemoveAll(s => s.Id == surfaceId);
        OutputSetupHandling.SaveActive();
    }

    private static void RemoveMapping(Setup setup, Guid surfaceId, Guid outputId)
    {
        setup.Surfaces.Find(s => s.Id == surfaceId)?.OutputMappings.RemoveAll(m => m.OutputId == outputId);
        OutputSetupHandling.SaveActive();
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping fall into the "unassigned" group, not lost.
    private static void DeleteOutput(Setup setup, MachineConfig machineConfig, Guid outputId)
    {
        setup.Outputs.RemoveAll(o => o.Id == outputId);
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        machineConfig.Unbind(outputId);
        if (OutputManager.PresentedOutputId == outputId)
            OutputManager.PresentedOutputId = Guid.Empty;

        OutputSetupHandling.SaveActive();
    }

    private static void AddProp(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var prop = new Prop();
        setup.Props.Add(prop);
        selection.Select(SetupEntitySelection.EntityKind.Prop, prop.Id);
    }

    private static void AddOutput(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = new OutputDefinition
                         {
                             Name = $"P{CountProjectorOutputs(setup) + 1}",
                             Kind = OutputDefinition.Kinds.Projector,
                             CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1200),
                         };
        setup.Outputs.Add(output);
        selection.Select(SetupEntitySelection.EntityKind.Output, output.Id);
    }

    private static int CountProjectorOutputs(Setup setup)
    {
        var count = 0;
        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Projector)
                count++;
        }

        return count;
    }

    private static string GetFreeName(string baseName)
    {
        _availableNames.Clear();
        OutputSetupHandling.GetAvailableSetupNames(_availableNames);
        if (!_availableNames.Contains(baseName))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!_availableNames.Contains(candidate))
                return candidate;
        }

        return baseName + " new";
    }

    private static readonly List<string> _availableNames = [];
}
