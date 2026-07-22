#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The output window's setup sidebar: setup switcher, then one section per entity kind
/// (CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS). Surfaces form their own tree (nested by
/// <see cref="Surface.ParentId"/>); the relationships between content, surfaces, and outputs are shown
/// per row. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active setup.
/// </summary>
internal static class SetupPanel
{
    /// <summary>Installs the Guid-list parameter hooks so SendToOutput.TargetIds shows target names and a
    /// surface/output picker in the op parameter window. Called from UI registration at startup, so it works
    /// even before the setup sidebar has been drawn (which is what a lazy static ctor would have waited for).</summary>
    internal static void RegisterGuidListHooks()
    {
        GuidListLabels.Resolver = ResolveTargetLabel;
        GuidListLabels.Picker = PickTarget;
    }

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
                // The Default output is the editor's internal preview, not something you present or map — hide it.
                if (output.Kind == OutputDefinition.Kinds.Default)
                    continue;

                var binding = machineConfig.TryGetBinding(output.Id);
                var outputId = output.Id;
                var status = binding == null ? null : $"Display {binding.DisplayIndex + 1}";
                var bindable = output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Output, output.Id, output.Name, status,
                              onDelete: () => DeleteOutput(setup, machineConfig, outputId), leadingIcon: Icon.Projector,
                              drawExtraMenuItems: bindable ? () => DrawOutputBindingSubMenu(output, machineConfig) : null);
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

        DrawPropertiesFooter(selection, setup, machineConfig);

        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    // Properties card for the selected entity, at the bottom of the panel.
    private static void DrawPropertiesFooter(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig)
    {
        if (!selection.TryResolve(setup, out var kind, out var id))
            return;

        FormInputs.AddVerticalSpace(12);
        ImGui.Indent(6 * T3Ui.UiScaleFactor); // 6px margin to the sidebar edges (right reserved inside the inputs).
        // Match FormInputs' field background (the default FrameBg is near-black in the panel).
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                DrawSurfaceCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Output:
                DrawOutputCard(setup, machineConfig, id);
                break;
            case SetupEntitySelection.EntityKind.ContentSource:
                DrawContentCard(setup, id);
                break;
        }

        ImGui.PopStyleColor();
        ImGui.Unindent(6 * T3Ui.UiScaleFactor);
    }

    private static void DrawSurfaceCard(Setup setup, Guid id)
    {
        var surface = setup.Surfaces.Find(s => s.Id == id);
        if (surface == null)
            return;

        FormInputsNarrow.DrawCardHeader("Surface");

        var render = surface.Render;
        if (FormInputsNarrow.DrawCheckbox("Render", ref render, "Skip drawing this surface without removing it."))
        {
            surface.Render = render;
            OutputSetupHandling.SaveActive();
        }

        var name = surface.Name;
        if (FormInputsNarrow.DrawString("Label", ref name, "surface name"))
        {
            surface.Name = name;
            OutputSetupHandling.SaveActive();
        }

        var shortName = surface.ShortName;
        if (FormInputsNarrow.DrawString("Short Name", ref shortName, "Auto", "Empty = auto-abbreviated (e.g. S1)."))
        {
            surface.ShortName = shortName;
            OutputSetupHandling.SaveActive();
        }

        FormInputsNarrow.DrawLabel("Sending to…", "Outputs this surface is mapped to.");
        for (var i = 0; i < surface.OutputMappings.Count; i++)
        {
            var output = setup.Outputs.Find(o => o.Id == surface.OutputMappings[i].OutputId);
            FormInputsNarrow.DrawListItem(output == null ? "?" : output.Name);
        }

        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
        var position = surface.Placement?.Pose.Position ?? Vector3.Zero;
        Span<float> pos = [position.X, position.Y, position.Z];
        var posState = FormInputsNarrow.DrawFloats("Position (m)", pos);
        if ((posState & InputEditStateFlags.Modified) != 0)
        {
            var placement = surface.Placement ??= new Surface.StagePlacement();
            placement.Pose = new Pose(new Vector3(pos[0], pos[1], pos[2]), placement.Pose.Orientation);
        }

        Span<float> size = [surface.SizeInMeters.X, surface.SizeInMeters.Y];
        var sizeState = FormInputsNarrow.DrawFloats("Size (m)", size);
        if ((sizeState & InputEditStateFlags.Modified) != 0)
            surface.SizeInMeters = new Vector2(size[0], size[1]);

        Span<float> anchor = [pivot.X, pivot.Y];
        var anchorState = FormInputsNarrow.DrawFloats("Anchor (0..1)", anchor);
        if ((anchorState & InputEditStateFlags.Modified) != 0)
            (surface.Placement ??= new Surface.StagePlacement()).Pivot = new Vector2(anchor[0], anchor[1]);

        // Value applied live above; persist once when the drag/edit completes.
        if (((posState | sizeState | anchorState) & InputEditStateFlags.Finished) != 0)
            OutputSetupHandling.SaveActive();
    }

    private static void DrawOutputCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        var output = setup.Outputs.Find(o => o.Id == id);
        if (output == null)
            return;

        FormInputsNarrow.DrawCardHeader("Output");

        var send = output.Send;
        if (FormInputsNarrow.DrawCheckbox("Send", ref send, "Pause presenting without dropping the display binding."))
        {
            output.Send = send;
            OutputSetupHandling.SaveActive();
        }

        var name = output.Name;
        if (FormInputsNarrow.DrawString("Label", ref name, "output name"))
        {
            output.Name = name;
            OutputSetupHandling.SaveActive();
        }

        var binding = machineConfig.TryGetBinding(output.Id);
        FormInputsNarrow.DrawLabel("Bound To", "Right-click the OUTPUT row to change the display binding.");
        FormInputsNarrow.DrawListItem(binding == null ? "unbound" : $"Display {binding.DisplayIndex + 1}");

        FormInputsNarrow.DrawLabel("Sending…", "Surfaces and content feeding this output.");
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].OutputMappings.Exists(m => m.OutputId == output.Id))
                FormInputsNarrow.DrawListItem(SurfaceShortLabel(setup.Surfaces[i]));
        }

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && SinkTargets(sinks[i], output.Id))
                FormInputsNarrow.DrawListItem(SinkName(instance));
        }
    }

    private static void DrawContentCard(Setup setup, Guid childId)
    {
        var instance = FindSinkInstance(childId);
        if (instance is not IOutputSink sink)
            return;

        FormInputsNarrow.DrawCardHeader("Content");

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

        // Reset() leaves RequestedResolution at 0×0; pulling the content preview at that size makes the
        // graph's auto-sized RenderTargets bail ("invalid texture size") and stop updating. Preview at
        // the resolution the content would render at when bound.
        _sinkContext.RequestedResolution = ContentPreviewResolution(setup);

        var update = sink.GetUpdateEnabled(_sinkContext);
        if (FormInputsNarrow.DrawCheckbox("Update", ref update, "When off, freezes this content at its last frame."))
            sink.SetUpdateEnabled(update);

        var name = SinkName(instance);
        FormInputsNarrow.DrawString("Label", ref name, "content name", "The op's name — rename the SendToOutput op.", readOnly: true);

        Span<int> resolution = [1, 1];
        var content = sink.GetContent(_sinkContext);
        if (content is { IsDisposed: false })
        {
            resolution[0] = content.Description.Width;
            resolution[1] = content.Description.Height;
        }

        FormInputsNarrow.DrawInts("Resolution (px)", resolution, "Comes from the source texture (read-only).", readOnly: true);
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
                    var targets = sink.GetTargetIds(_sinkContext);
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var targetId = targets[i];
                        if (setup.Surfaces.Exists(s => s.Id == targetId))
                            _referenced.Add((SetupEntitySelection.EntityKind.Surface, targetId));
                        else if (setup.Outputs.Exists(o => o.Id == targetId))
                            _referenced.Add((SetupEntitySelection.EntityKind.Output, targetId));
                    }
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
            if (sinks[i] is Instance instance && SinkTargets(sinks[i], targetId))
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

        // A content send dropped on a surface or output adds it to the send's targets (one content can fan
        // out to several surfaces). Persisted on the op.
        if (dragKind == SetupEntitySelection.EntityKind.ContentSource && FindSinkInstance(dragId) is IOutputSink sink)
        {
            _sinkContext ??= new EvaluationContext();
            _sinkContext.Reset();
            var targets = new List<Guid>(sink.GetTargetIds(_sinkContext));
            if (!targets.Contains(targetId))
            {
                targets.Add(targetId);
                sink.SetTargets(targets);
            }
        }
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

            var (icon, text) = DescribeSinkTargetsGutter(setup, sinks[i].GetTargetIds(_sinkContext));
            DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ContentSource, instance.SymbolChildId, SinkName(instance), text,
                          leadingIcon: Icon.FileImage, trailingIcon: icon);
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

    // Select the content's SendToOutput op in the focused graph and frame it — the sidebar → graph half of
    // the sync (the graph → sidebar highlight is handled by the highlighted-content id).
    private static void RevealContentOpInGraph(Guid childId)
    {
        var instance = FindSinkInstance(childId);
        var parentSymbolUi = instance?.Parent?.GetSymbolUi();
        if (instance == null || parentSymbolUi == null || ProjectView.Focused == null)
            return;

        if (!parentSymbolUi.ChildUis.TryGetValue(instance.SymbolChildId, out var childUi))
            return;

        ProjectView.Focused.NodeSelection.SetSelection(childUi, instance);
        FitViewToSelectionHandling.FitViewToSelection();
    }

    // Out-gutter for a content send: the first target's type icon + short label, "+N" for extra targets.
    private static (Icon? icon, string text) DescribeSinkTargetsGutter(Setup setup, IReadOnlyList<Guid> targets)
    {
        if (targets.Count == 0)
            return (null, "unbound");

        var (icon, name) = DescribeSingleTarget(setup, targets[0]);
        return (icon, targets.Count > 1 ? $"{name} +{targets.Count - 1}" : name);
    }

    private static (Icon? icon, string name) DescribeSingleTarget(Setup setup, Guid targetId)
    {
        var surface = setup.Surfaces.Find(s => s.Id == targetId);
        if (surface != null)
            return (Icon.Grid, SurfaceShortLabel(surface));

        var output = setup.Outputs.Find(o => o.Id == targetId);
        if (output != null)
            return (Icon.Projector, string.IsNullOrEmpty(output.Name) ? "output" : Abbreviate(output.Name));

        return (Icon.Grid, "?");
    }

    private static bool SinkTargets(IOutputSink sink, Guid targetId)
    {
        var targets = sink.GetTargetIds(_sinkContext!);
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i] == targetId)
                return true;
        }

        return false;
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
        var name = firstOutput == null ? "?" : Abbreviate(firstOutput.Name);
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
                    var targets = sink.GetTargetIds(_sinkContext);
                    var (_, targetText) = DescribeSinkTargetsGutter(setup, targets);
                    CustomComponents.StylizedText(targetText, Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
        }

        ImGui.EndGroup();
    }

    private static void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection)
    {
        var scale = T3Ui.UiScaleFactor;
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        if (ImGui.InvisibleButton("##setupSwitcher", new Vector2(ImGui.GetContentRegionAvail().X, height)))
            ImGui.OpenPopup("##setupMenu");

        // Label + chevron drawn over the button so the chevron sits next to the name (not far-right like a combo).
        ImGui.SetCursorScreenPos(new Vector2(pos.X + 2 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(setup.Name, Fonts.FontNormal, UiColors.Text);
        ImGui.SameLine(0, 4 * scale);
        DrawInlineIcon(Icon.ChevronDown, UiColors.TextMuted.Rgba);
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height));

        if (ImGui.BeginPopup("##setupMenu"))
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

            ImGui.EndPopup();
        }
    }

    // A surface's compact label: its explicit ShortName, else the auto-abbreviation.
    /// <summary>Full-width dropdown for a SendToOutput target-id list item: lists the active setup's surfaces
    /// then outputs; picking one returns the new id. The row's ImGui ID stack keeps each item's popup distinct.</summary>
    private static bool PickTarget(Guid current, float width, out Guid picked)
    {
        picked = current;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            ImGui.BeginDisabled();
            ImGui.Button("(no setup)", new Vector2(width, 0));
            ImGui.EndDisabled();
            return false;
        }

        // Stable "###" id so the button keeps its identity as its label changes.
        if (ImGui.Button(ResolveTargetLabel(current) + "###pickTarget", new Vector2(width, 0)))
            ImGui.OpenPopup("##pickTargetPopup");

        var changed = false;
        if (ImGui.BeginPopup("##pickTargetPopup"))
        {
            for (var i = 0; i < setup.Surfaces.Count; i++)
            {
                var surface = setup.Surfaces[i];
                if (ImGui.Selectable($"{surface.Name}##s{i}", surface.Id == current))
                {
                    picked = surface.Id;
                    changed = true;
                }
            }

            if (setup.Surfaces.Count > 0 && setup.Outputs.Count > 0)
                ImGui.Separator();

            for (var i = 0; i < setup.Outputs.Count; i++)
            {
                var output = setup.Outputs[i];
                if (ImGui.Selectable($"{output.Name}##o{i}", output.Id == current))
                {
                    picked = output.Id;
                    changed = true;
                }
            }

            ImGui.EndPopup();
        }

        return changed;
    }

    /// <summary>Names a SendToOutput target id for the parameter-window Guid list: a surface's short label
    /// or an output's name; "(missing)" when it resolves to nothing in the active setup (e.g. a target whose
    /// surface was deleted).</summary>
    private static string ResolveTargetLabel(Guid id)
    {
        if (id == Guid.Empty)
            return "(none)";

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return id.ToString("D")[..8];

        var surface = setup.Surfaces.Find(s => s.Id == id);
        if (surface != null)
            return SurfaceShortLabel(surface);

        var output = setup.Outputs.Find(o => o.Id == id);
        if (output != null)
            return output.Name;

        return "(missing)";
    }

    // A valid render resolution for previewing a content graph: the first output's canvas size, else a
    // 1080p fallback. Never 0×0 (which auto-sized RenderTargets treat as invalid and skip).
    private static T3.Core.DataTypes.Vector.Int2 ContentPreviewResolution(Setup setup)
    {
        for (var i = 0; i < setup.Outputs.Count; i++)
        {
            var r = setup.Outputs[i].CanvasResolution;
            if (r.Width > 0 && r.Height > 0)
                return r;
        }

        return new T3.Core.DataTypes.Vector.Int2(1920, 1080);
    }

    private static string SurfaceShortLabel(Surface surface)
    {
        return string.IsNullOrEmpty(surface.ShortName) ? Abbreviate(surface.Name) : surface.ShortName;
    }

    // Compact gutter form: uppercase letters + digits ("Surface 1" → "S1", "WallFront" → "WF"), falling back
    // to the full name when there's nothing to abbreviate (all-lowercase).
    private static string Abbreviate(string name)
    {
        Span<char> buffer = stackalloc char[6];
        var length = 0;
        foreach (var c in name)
        {
            if ((char.IsUpper(c) || char.IsDigit(c)) && length < buffer.Length)
                buffer[length++] = c;
        }

        return length >= 1 ? new string(buffer[..length]) : name;
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

        // Section top edge: a black divider line + a rounded-NW corner notch, giving the section a rounded top.
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var edgeY = (float)Math.Round(ImGui.GetCursorScreenPos().Y);
        var winMinX = ImGui.GetWindowPos().X;
        dl.AddLine(new Vector2(winMinX, edgeY), new Vector2(winMinX + ImGui.GetWindowWidth(), edgeY), UiColors.BackgroundFull, 1 * scale);
        Icons.DrawIconAtScreenPosition(Icon.RoundingNW, new Vector2(winMinX, edgeY), dl, UiColors.BackgroundFull.Fade(0.5f));

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
                                      Action? onDelete = null, Action? onRemoveFromOutput = null, Icon? leadingIcon = null, Icon? trailingIcon = null,
                                      Action? drawExtraMenuItems = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var rounding = 4 * scale;
        // Odd height so a 15px icon centers exactly ((23-15)/2 = 4).
        var height = (float)Math.Round(23 * scale);

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
            {
                selection.Select(kind, id);
                // A content row is a live op — a plain click selects it in the graph and brings it into view.
                if (kind == SetupEntitySelection.EntityKind.ContentSource)
                    RevealContentOpInGraph(id);
            }
        }

        if (isHovered)
        {
            _pendingHoveredKind = kind;
            _pendingHoveredId = id;
            if (kind == SetupEntitySelection.EntityKind.ContentSource)
                FrameStats.AddHoveredId(id);
        }

        HandleRowDragDrop(setup, kind, id);

        if (onDelete != null || onRemoveFromOutput != null || drawExtraMenuItems != null)
        {
            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    drawExtraMenuItems?.Invoke();

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

        // Content over the background (the selectable is transparent), vertically centered in the fixed row
        // (the -1px nudges the label up so it isn't sitting low).
        var contentY = (float)Math.Round(rowMin.Y + (height - ImGui.GetTextLineHeight()) * 0.5f - 1 * scale);
        var iconY = contentY + 3 * scale; // glyphs render high vs the text baseline — drop them to match.

        var contentX = rowMin.X + 6 * scale;
        if (leadingIcon.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            DrawInlineIcon(leadingIcon.Value, UiColors.TextMuted.Rgba);
            contentX = ImGui.GetItemRectMax().X + 5 * scale;
        }

        ImGui.SetCursorScreenPos(new Vector2(contentX, contentY));
        CustomComponents.StylizedText(string.IsNullOrEmpty(name) ? "untitled" : name, Fonts.FontNormal, UiColors.Text);

        if (trailingIcon.HasValue || status != null)
        {
            var trailX = rowMin.X + (rowMax.X - rowMin.X) * 0.55f;
            if (trailingIcon.HasValue)
            {
                ImGui.SetCursorScreenPos(new Vector2(trailX, iconY));
                DrawInlineIcon(Icon.ArrowRight, UiColors.TextMuted.Fade(0.3f).Rgba);
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X + 2 * scale, iconY));
                DrawInlineIcon(trailingIcon.Value, UiColors.TextMuted.Rgba);
                trailX = ImGui.GetItemRectMax().X + 4 * scale;
            }

            if (status != null)
            {
                // FontSmall is shorter than the row's FontNormal baseline — center it on its own height.
                ImGui.PushFont(Fonts.FontSmall);
                var smallHeight = ImGui.GetTextLineHeight();
                ImGui.PopFont();
                var statusY = (float)Math.Round(rowMin.Y + (height - smallHeight) * 0.5f - 1 * scale);
                ImGui.SetCursorScreenPos(new Vector2(trailX, statusY));
                CustomComponents.StylizedText(status, Fonts.FontSmall, UiColors.TextMuted);
            }
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

        // Prune the deleted surface from every send that targeted it, so no dangling id lingers.
        foreach (var sink in OutputSinkRegistry.Sinks)
            sink.RemoveTarget(surfaceId);

        OutputSetupHandling.SaveActive();
    }

    private static void DrawOutputBindingSubMenu(OutputDefinition output, MachineConfig machineConfig)
    {
        if (CustomComponents.DrawSubMenu(3, "Bind to display"))
        {
            ResolutionHandling.DrawBindingMenuItems(output, machineConfig);
            ImGui.EndMenu();
        }
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping simply have no output — not lost.
    private static void DeleteOutput(Setup setup, MachineConfig machineConfig, Guid outputId)
    {
        setup.Outputs.RemoveAll(o => o.Id == outputId);
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        // Prune the deleted output from every send that targeted it directly (full-frame).
        foreach (var sink in OutputSinkRegistry.Sinks)
            sink.RemoveTarget(outputId);

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
