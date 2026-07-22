#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The output window's setup sidebar: setup switcher, then one section per entity kind
/// (CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS). Surfaces form their own tree (nested by
/// <see cref="Surface.ParentId"/>); the relationships between content, surfaces, and outputs are shown
/// per row. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active setup.
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

        // Cross-highlight: what the row hovered last frame references (one-frame lag is imperceptible for hover).
        ComputeReferenced(setup);
        _pendingHoveredKind = SetupEntitySelection.EntityKind.None;
        _pendingHoveredId = Guid.Empty;

        DrawSetupSwitcher(setup, selection);
        FormInputs.AddVerticalSpace(4);

        // CONTENT — live SendToOutput sinks (their targeting lives on the op, so they aren't setup entities).
        if (DrawSectionLabel("CONTENT"))
            DrawContentSinks(selection, setup);

        if (DrawSection("SURFACES", "##addSurface", selection, AddSurface))
            DrawSurfaces(selection, setup);

        if (DrawSection("OUTPUTS", "##addOutput", selection, AddOutput))
        {
            for (var i = 0; i < setup.Outputs.Count; i++)
            {
                var output = setup.Outputs[i];
                var binding = machineConfig.TryGetBinding(output.Id);
                var outputId = output.Id;
                var status = binding == null ? null : $"Display {binding.DisplayIndex + 1}";
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Output, output.Id, output.Name, status,
                              onDelete: () => DeleteOutput(setup, machineConfig, outputId), leadingIcon: Icon.Projector);
            }
        }

        if (DrawSection("REFERENCE IMAGES", "##addRefImage", selection, AddReferenceImage))
        {
            for (var i = 0; i < setup.ReferenceImages.Count; i++)
            {
                var image = setup.ReferenceImages[i];
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, image.Name, null);
            }
        }

        if (DrawSection("PROPS", "##addProp", selection, AddProp))
        {
            for (var i = 0; i < setup.Props.Count; i++)
            {
                var prop = setup.Props[i];
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Prop, prop.Id, prop.Kind, null);
            }
        }

        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    // Fills _referenced with the entities the currently-hovered row points at, along the
    // content → surface → output chain, so those rows can draw the Referenced state.
    private static void ComputeReferenced(Setup setup)
    {
        _referenced.Clear();
        if (_hoveredKind == SetupEntitySelection.EntityKind.None)
            return;

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

        switch (_hoveredKind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.Surfaces.Find(s => s.Id == _hoveredId);
                if (surface != null)
                {
                    foreach (var mapping in surface.OutputMappings)
                        _referenced.Add((SetupEntitySelection.EntityKind.Output, mapping.OutputId));
                }

                AddSinksTargeting(_hoveredId);
                break;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.OutputMappings.Exists(m => m.OutputId == _hoveredId))
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id));
                }

                AddSinksTargeting(_hoveredId);
                break;
            }
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                if (FindSinkInstance(_hoveredId) is IOutputSink sink)
                {
                    var targetId = sink.GetTargetId(_sinkContext);
                    if (setup.Surfaces.Exists(s => s.Id == targetId))
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, targetId));
                    else if (setup.Outputs.Exists(o => o.Id == targetId))
                        _referenced.Add((SetupEntitySelection.EntityKind.Output, targetId));
                }

                break;
            }
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Reference != null && surface.Reference.ImageId == _hoveredId)
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id));
                }

                break;
            }
        }
    }

    private static void AddSinksTargeting(Guid targetId)
    {
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && sinks[i].GetTargetId(_sinkContext!) == targetId)
                _referenced.Add((SetupEntitySelection.EntityKind.ContentSource, instance.SymbolChildId));
        }
    }

    private static bool IsReferenced(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (_referenced[i].kind == kind && _referenced[i].id == id)
                return true;
        }

        return false;
    }

    // Drag-to-map: a surface dropped on an output adds a mapping; a content send dropped on a surface or
    // output retargets it. Call right after a row's Selectable so it acts as that item's source/target.
    private static void HandleRowDragDrop(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (kind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource)
            DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.SetupEntity, $"{(int)kind}:{id}");

        if (kind is not (SetupEntitySelection.EntityKind.Output or SetupEntitySelection.EntityKind.Surface))
            return;

        if (!DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.SetupEntity, out var dragData)
            || !TryParseDrag(dragData, out var dragKind, out var dragId))
            return;

        var accepts = kind == SetupEntitySelection.EntityKind.Output
                          ? dragKind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource
                          : dragKind == SetupEntitySelection.EntityKind.ContentSource;
        if (!accepts || dragId == id)
            return;

        if (DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SetupEntity, out _) == DragAndDropHandling.DragInteractionResult.Dropped)
            ApplyDrop(setup, dragKind, dragId, kind, id);
    }

    private static bool TryParseDrag(string data, out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;
        var separator = data.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(data.AsSpan(0, separator), out var kindInt)
            || !Guid.TryParse(data.AsSpan(separator + 1), out id))
            return false;

        kind = (SetupEntitySelection.EntityKind)kindInt;
        return true;
    }

    private static void ApplyDrop(Setup setup, SetupEntitySelection.EntityKind dragKind, Guid dragId,
                                  SetupEntitySelection.EntityKind targetKind, Guid targetId)
    {
        if (targetKind == SetupEntitySelection.EntityKind.Output && dragKind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.Surfaces.Find(s => s.Id == dragId);
            var output = setup.Outputs.Find(o => o.Id == targetId);
            if (surface != null && output != null && !surface.OutputMappings.Exists(m => m.OutputId == targetId))
            {
                surface.OutputMappings.Add(CreateDefaultMapping(output));
                OutputSetupHandling.SaveActive();
            }

            return;
        }

        // A content send dropped on a surface or output points its target there (persisted on the op).
        if (dragKind == SetupEntitySelection.EntityKind.ContentSource && FindSinkInstance(dragId) is IOutputSink sink)
            sink.SetTarget(targetId);
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

    private static void DrawContentSinks(SetupEntitySelection selection, Setup setup)
    {
        var sinks = OutputSinkRegistry.Sinks;
        if (sinks.Count == 0)
        {
            ImGui.Indent(8 * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText("no SendToOutput ops", Fonts.FontSmall, UiColors.TextMuted.Fade(0.6f));
            ImGui.Unindent(8 * T3Ui.UiScaleFactor);
            return;
        }

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is not Instance instance)
                continue;

            var (icon, text) = DescribeSinkTargetGutter(setup, sinks[i].GetTargetId(_sinkContext));
            DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ContentSource, instance.SymbolChildId, SinkName(instance), text,
                          leadingIcon: Icon.Slice, trailingIcon: icon);
        }
    }

    private static string SinkName(Instance instance)
    {
        var parent = instance.Parent;
        if (parent != null && parent.Symbol.Children.TryGetValue(instance.SymbolChildId, out var child))
            return child.ReadableName;

        return "content";
    }

    private static Instance? FindSinkInstance(Guid childId)
    {
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    private static string DescribeSinkTarget(Setup setup, Guid targetId)
    {
        if (targetId == Guid.Empty)
            return "unbound";

        var surface = setup.Surfaces.Find(s => s.Id == targetId);
        if (surface != null)
            return "→ " + (string.IsNullOrEmpty(surface.Name) ? "surface" : surface.Name);

        var output = setup.Outputs.Find(o => o.Id == targetId);
        if (output != null)
            return "→ " + (string.IsNullOrEmpty(output.Name) ? "output" : output.Name);

        return "→ ?";
    }

    // Out-gutter for a content send: the target's type icon (grid = surface, projector = output) + its name.
    private static (Icon? icon, string text) DescribeSinkTargetGutter(Setup setup, Guid targetId)
    {
        if (targetId != Guid.Empty)
        {
            var surface = setup.Surfaces.Find(s => s.Id == targetId);
            if (surface != null)
                return (Icon.Grid, string.IsNullOrEmpty(surface.Name) ? "surface" : surface.Name);

            var output = setup.Outputs.Find(o => o.Id == targetId);
            if (output != null)
                return (Icon.Projector, string.IsNullOrEmpty(output.Name) ? "output" : output.Name);
        }

        return (null, "unbound");
    }

    // Surfaces as a tree: roots first, each followed by its children (nested by ParentId). The mapped
    // output(s) are shown as the row status until the icon gutters land.
    private static void DrawSurfaces(SetupEntitySelection selection, Setup setup)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == Guid.Empty)
                DrawSurfaceRow(selection, setup, setup.Surfaces[i], 0);
        }
    }

    private static void DrawSurfaceRow(SetupEntitySelection selection, Setup setup, Surface surface, int depth)
    {
        var surfaceId = surface.Id;
        if (depth > 0)
            ImGui.Indent(depth * 12 * T3Ui.UiScaleFactor);

        var (outputIcon, outputText) = DescribeSurfaceOutputGutter(setup, surface);
        DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Surface, surface.Id, surface.Name,
                      outputText,
                      onDelete: () => DeleteSurface(setup, surfaceId), leadingIcon: Icon.Grid, trailingIcon: outputIcon);

        if (depth > 0)
            ImGui.Unindent(depth * 12 * T3Ui.UiScaleFactor);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == surfaceId)
                DrawSurfaceRow(selection, setup, setup.Surfaces[i], depth + 1);
        }
    }

    // Out-gutter for a surface: the projector icon + the mapped output name (+N for edge-blended extras).
    private static (Icon? icon, string? text) DescribeSurfaceOutputGutter(Setup setup, Surface surface)
    {
        if (surface.OutputMappings.Count == 0)
            return (null, null);

        var firstOutput = setup.Outputs.Find(o => o.Id == surface.OutputMappings[0].OutputId);
        var name = firstOutput == null ? "?" : firstOutput.Name;
        var text = surface.OutputMappings.Count > 1 ? $"{name} +{surface.OutputMappings.Count - 1}" : name;
        return (Icon.Projector, text);
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
                    CustomComponents.StylizedText($"Surface · {surface.Kind}", Fonts.FontSmall, UiColors.TextMuted);
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
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                var instance = FindSinkInstance(id);
                if (instance is IOutputSink sink)
                {
                    _sinkContext ??= new EvaluationContext();
                    _sinkContext.Reset();
                    CustomComponents.StylizedText("Content · SendToOutput", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(SinkName(instance), Fonts.FontLarge, UiColors.Text);
                    CustomComponents.StylizedText(DescribeSinkTarget(setup, sink.GetTargetId(_sinkContext)), Fonts.FontNormal, UiColors.TextMuted);
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

    // An icon drawn as a font glyph on the current text line — aligns with AlignTextToFramePadding'd text,
    // unlike DrawAtCursor which adds its own vertical offset.
    private static void DrawInlineIcon(Icon icon, Vector4 rgba)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, rgba);
        ImGui.PushFont(Icons.IconFont);
        ImGui.TextUnformatted(((char)icon).ToString());
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

    // A collapsible section header: chevron toggle + label. Returns whether the section is expanded.
    private static bool DrawSectionLabel(string title)
    {
        FormInputs.AddVerticalSpace(6);
        var expanded = _expandedSections.GetValueOrDefault(title, true);

        ImGui.PushID(title);
        if (CustomComponents.IconButton(expanded ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(ImGui.GetFrameHeight())))
        {
            expanded = !expanded;
            _expandedSections[title] = expanded;
        }

        ImGui.SameLine(0, 2 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.TextMuted);
        ImGui.PopID();
        return expanded;
    }

    private static bool DrawSection(string title, string addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection> onAdd)
    {
        var expanded = DrawSectionLabel(title);
        CustomComponents.RightAlign(ImGui.GetFrameHeight());
        ImGui.PushID(addButtonId);
        if (CustomComponents.IconButton(Icon.Plus, Vector2.Zero))
        {
            onAdd(selection);
            OutputSetupHandling.SaveActive();
        }

        ImGui.PopID();
        return expanded;
    }

    private static void DrawEntityRow(SetupEntitySelection selection, Setup setup, SetupEntitySelection.EntityKind kind, Guid id, string name, string? status,
                                      Action? onDelete = null, Action? onRemoveFromOutput = null, Icon? leadingIcon = null, Icon? trailingIcon = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var rounding = 4 * scale;
        var height = ImGui.GetFrameHeight();

        ImGui.PushID(id.GetHashCode());

        // Rounded row inset 4px from the window edges (so the selection/outline never clips), pixel-snapped
        // to avoid a blurry sub-pixel edge.
        var entryPos = ImGui.GetCursorScreenPos();
        var windowPos = ImGui.GetWindowPos();
        var rowMin = new Vector2((float)Math.Round(windowPos.X + 4 * scale), (float)Math.Round(entryPos.Y));
        var rowMax = new Vector2((float)Math.Round(windowPos.X + ImGui.GetWindowWidth() - 4 * scale), rowMin.Y + height);
        var dl = ImGui.GetWindowDrawList();
        var isSelected = selection.IsSelected(kind, id);

        // Full-row hit test — a selectable spanning the padded row; its own header background is suppressed
        // so we can draw a rounded one instead.
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        ImGui.SetCursorScreenPos(rowMin);
        var clicked = ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(rowMax.X - rowMin.X, height));
        ImGui.PopStyleColor(3);

        var isHovered = ImGui.IsItemHovered();

        if (clicked)
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
                selection.Toggle(kind, id);
            else if (io.KeyShift)
                selection.Add(kind, id);
            else
                selection.Select(kind, id);
        }

        if (isHovered)
        {
            _pendingHoveredKind = kind;
            _pendingHoveredId = id;
        }

        HandleRowDragDrop(setup, kind, id);

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

        if (isSelected)
            dl.AddRectFilled(rowMin, rowMax, UiColors.StatusActivated.Fade(0.3f), rounding);
        else if (isHovered)
            dl.AddRectFilled(rowMin, rowMax, UiColors.ForegroundFull.Fade(0.2f), rounding);

        if (!isSelected && IsReferenced(kind, id))
            dl.AddRect(rowMin, rowMax, UiColors.StatusAutomated.Fade(0.6f), rounding);

        // Content over the background (the selectable is transparent). One AlignTextToFramePadding plus
        // font-glyph icons keeps icon and text on a single vertically-centered baseline.
        ImGui.SetCursorScreenPos(new Vector2(rowMin.X + 6 * scale, rowMin.Y));
        ImGui.AlignTextToFramePadding();
        if (leadingIcon.HasValue)
        {
            DrawInlineIcon(leadingIcon.Value, UiColors.TextMuted.Rgba);
            ImGui.SameLine(0, 5 * scale);
        }

        CustomComponents.StylizedText(string.IsNullOrEmpty(name) ? "untitled" : name, Fonts.FontNormal, UiColors.Text);

        if (trailingIcon.HasValue || status != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + (rowMax.X - rowMin.X) * 0.55f, rowMin.Y));
            ImGui.AlignTextToFramePadding();
            if (trailingIcon.HasValue)
            {
                DrawInlineIcon(Icon.ArrowRight, UiColors.TextMuted.Fade(0.3f).Rgba);
                ImGui.SameLine(0, 2 * scale);
                DrawInlineIcon(trailingIcon.Value, UiColors.TextMuted.Rgba);
                ImGui.SameLine(0, 4 * scale);
            }

            if (status != null)
                CustomComponents.StylizedText(status, Fonts.FontSmall, UiColors.TextMuted);
        }

        // Next row starts a tight 2px below, independent of the content cursor above.
        ImGui.SetCursorScreenPos(new Vector2(entryPos.X, rowMax.Y + 2 * scale));
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

    private static void AddSurface(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
        setup.Surfaces.Add(surface);
        selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
    }

    private static void DeleteSurface(Setup setup, Guid surfaceId)
    {
        // Re-parent orphaned children to the deleted surface's parent so the tree stays connected.
        var parentId = setup.Surfaces.Find(s => s.Id == surfaceId)?.ParentId ?? Guid.Empty;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId == surfaceId)
                surface.ParentId = parentId;
        }

        setup.Surfaces.RemoveAll(s => s.Id == surfaceId);
        OutputSetupHandling.SaveActive();
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping simply have no output — not lost.
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
    private static readonly Dictionary<string, bool> _expandedSections = [];
    private static EvaluationContext? _sinkContext;

    // Cross-highlight: the row hovered this frame (committed at end of Draw), and the entities it references.
    private static SetupEntitySelection.EntityKind _hoveredKind;
    private static Guid _hoveredId;
    private static SetupEntitySelection.EntityKind _pendingHoveredKind;
    private static Guid _pendingHoveredId;
    private static readonly List<(SetupEntitySelection.EntityKind kind, Guid id)> _referenced = [];
}
