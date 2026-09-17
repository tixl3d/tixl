#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The canvas' header: the mode tabs and their contextual actions, hosted in the outliner strip while it shows.
/// </summary>
internal sealed partial class SetupOutputView
{
    private enum HeaderKinds { None, Modes, Return, Reference }

    /// <summary>Set by the window: the outliner strip is shown and hosts the toolbar (see <see cref="DeferHeader"/>).</summary>
    public bool IsHeaderHostedByStrip;

    /// <summary>
    /// The strip hosts the canvas' toolbar while it is shown: the canvas records what its header would be and
    /// draws nothing at its top; the strip's header row calls <see cref="DrawHostedHeader"/> in its place.
    /// Returns true when deferred (the caller skips its own header).
    /// </summary>
    private bool DeferHeader(HeaderKinds kind, Guid outputId = default, string title = "", Guid imageId = default, Guid subjectId = default)
    {
        _pendingHeaderKind = kind;
        _pendingHeaderOutputId = outputId;
        _pendingHeaderTitle = title;
        _pendingHeaderImageId = imageId;
        _pendingHeaderSubjectId = subjectId;
        return IsHeaderHostedByStrip;
    }

    /// <summary>The header the last drawn canvas asked for, in the strip's header row (see <see cref="DeferHeader"/>).</summary>
    public void DrawHostedHeader()
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        switch (_pendingHeaderKind)
        {
            case HeaderKinds.Modes:
                DrawHeader(setup, setup.FindOutput(_pendingHeaderOutputId), _pendingHeaderOutputId);
                break;
            case HeaderKinds.Return:
                DrawBoardReturnHeader(_pendingHeaderTitle);
                break;
            case HeaderKinds.Reference:
                if (setup.FindReferenceImage(_pendingHeaderImageId) is { } image)
                    DrawReferenceHeader(image, setup.FindSurface(_pendingHeaderSubjectId));

                break;
        }
    }

    /// <param name="output">Null while the Board is shown without any output focused.</param>
    private void DrawHeader(Setup setup, OutputDefinition? output, Guid outputId)
    {
        // The Board and its two cameras as one segmented control. Straightening rectifies a single surface,
        // so it is only usable when the focused entity resolves to one mapped to this output (for a Layout
        // child, its parent) or traced on a photo; the projector needs an output. Those segments show disabled
        // rather than vanishing, so the toolbar keeps its shape.
        var straightCarrier = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId);
        var hasOutput = output != null;
        var hasStraightSubject = straightCarrier != null || TracedImageOf(setup, _shownSurfaceId) != null;

        // A camera whose subject this view doesn't hold may still be *reachable* from the selection along the
        // routing — with a slice selected, the projector it ends up on is unambiguous. Offer the tab then, and
        // let picking it select that subject, rather than making the user walk the columns to it by hand.
        var selection = GlobalSelectionHandling.SetupEntities;
        var reachedOutputId = Guid.Empty;
        var reachedSurfaceId = Guid.Empty;
        if (selection.TryResolve(setup, out var primaryKind, out var primaryId))
        {
            if (!hasOutput)
                SetupRelations.TryGetOutputOf(setup, primaryKind, primaryId, out reachedOutputId);

            if (!hasStraightSubject)
                SetupRelations.TryGetSurfaceOf(setup, primaryKind, primaryId, out reachedSurfaceId);

            // Reaching a surface is not enough: one that is neither mapped to an output nor traced on a photo has
            // nothing to straighten against, and the tab would bounce straight back to the Board.
            if (reachedSurfaceId != Guid.Empty
                && setup.FindMappedAncestor(reachedSurfaceId) is not { OutputMappings.Count: > 0 }
                && TracedImageOf(setup, reachedSurfaceId) == null)
            {
                reachedSurfaceId = Guid.Empty;
            }
        }

        var canOutput = hasOutput || reachedOutputId != Guid.Empty;
        var canStraight = hasStraightSubject || reachedSurfaceId != Guid.Empty;

        if (CustomComponents.SegmentedButton(ref _editMode,
                                             isItemDisabled: mode => mode switch
                                                                         {
                                                                             EditModes.Board => false,
                                                                             EditModes.Straight => !canStraight,
                                                                             _ => !canOutput,
                                                                         },
                                             tooltipForItem: mode => mode switch
                                                                         {
                                                                             EditModes.Board =>
                                                                                 "Everything at once: content, surfaces and outputs as cards you can arrange and route.",
                                                                             EditModes.Straight when canStraight =>
                                                                                 "One surface seen head-on, its keystone taken out, so you can place content on it without fighting the perspective.",
                                                                             EditModes.Straight =>
                                                                                 "Needs a surface. Select one that is mapped to an output or traced on a reference photo — straightening rectifies a single surface.",
                                                                             _ when canOutput =>
                                                                                 "The output canvas as the projector sees it: patches, mapped surfaces and their handles in its pixels.",
                                                                             _ =>
                                                                                 "Needs an output. Select one, or anything routed into one.",
                                                                         }))
        {
            // Picked a camera the selection only leads to: select its subject so the next frame frames it.
            if (_editMode == EditModes.Output && reachedOutputId != Guid.Empty)
                selection.Select(SetupEntityKinds.Output, reachedOutputId);
            else if (_editMode == EditModes.Straight && reachedSurfaceId != Guid.Empty)
                selection.Select(SetupEntityKinds.Surface, reachedSurfaceId);
        }

        // Isolate: locks the canvas to the focused frame — the others stay visible and keep snapping, but
        // can't be selected or edited from the canvas, so you can work one frame without nudging its
        // neighbours. Only meaningful with a frame selected, and auto-clears when that lapses. Rendered in
        // StatusAttention so the locked state reads as deliberate rather than a glitch.
        var canIsolate = straightCarrier != null;
        if (!canIsolate)
            _isolatesFocusedSurface = false;

        // How much of the surfaces' content shows over their photos, on the Board and the traced quads: a
        // drag-edit field in percent, like the parameter fields.
        ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Overlay", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        var previewPercent = UserSettings.Config.OutputSetupContentPreviewOpacity * 100f;
        ImGui.PushID("contentPreview");
        var previewState = SingleValueEdit.Draw(ref previewPercent, new Vector2(60 * T3Ui.UiScaleFactor, ImGui.GetFrameHeight()),
                                                0f, 100f, clampMin: true, clampMax: true, scale: 0.5f, format: "{0:0}%", defaultValue: 65f);
        ImGui.PopID();
        if ((previewState & InputEditStateFlags.Modified) != 0)
            UserSettings.Config.OutputSetupContentPreviewOpacity = previewPercent / 100f;

        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Overlay opacity", "Opacity of each surface's content over its photo — on the traced quads and the surface cards. Drag, or double-click to type.");

        // Isolate is an Output-canvas affair: it locks the corner-pin editing to one frame. Elsewhere it has
        // nothing to lock, so it isn't offered (and clears).
        if (_editMode == EditModes.Output)
        {
            ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
            ImGui.BeginDisabled(!canIsolate);
            if (CustomComponents.StateButton("Isolate", _isolatesFocusedSurface ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Emphasized) && canIsolate)
                _isolatesFocusedSurface = !_isolatesFocusedSurface;

            ImGui.EndDisabled();
            if (canIsolate && ImGui.IsItemHovered())
                ImGui.SetTooltip("Lock the canvas to the selected frame.\nOthers stay visible and snap, but change selection in the sidebar.");
        }
        else
        {
            _isolatesFocusedSurface = false;
        }

        // Calibrating against the photo: the straightened photo is projected in place of the content, and the
        // reference points become handles on the projector canvas — drag one until its crosshair sits on the
        // real feature; the pin re-solves so every placed point stays exactly where it was aimed.
        var photoCarrier = straightCarrier is { Trace: not null } ? straightCarrier : null;
        if (photoCarrier == null)
            _projectsPhoto = false;

        if (_editMode is EditModes.Output or EditModes.Straight && photoCarrier != null)
        {
            ImGui.SameLine();
            if (CustomComponents.StateButton("Project photo", _projectsPhoto ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Emphasized))
                _projectsPhoto = !_projectsPhoto;

            if (ImGui.IsItemHovered())
                CustomComponents.TooltipForLastItem("Project the photo", "Projects a disc of the straightened photo around each reference point, with a crosshair. Drag a point on the canvas until the photo's feature lands on the real one; that activates it (green) and the pin is solved through every activated point. Double-click a point to reset it.");

            // The disc size, as a share of the canvas height — small enough to isolate one feature, large enough
            // to recognise it.
            ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
            var radiusPercent = UserSettings.Config.OutputSetupPhotoDiscRadius * 100f;
            ImGui.PushID("photoDiscRadius");
            var radiusState = SingleValueEdit.Draw(ref radiusPercent, new Vector2(52 * T3Ui.UiScaleFactor, ImGui.GetFrameHeight()),
                                                   1f, 50f, clampMin: true, clampMax: true, scale: 0.2f, format: "{0:0}%", defaultValue: 5f);
            ImGui.PopID();
            if ((radiusState & InputEditStateFlags.Modified) != 0)
                UserSettings.Config.OutputSetupPhotoDiscRadius = radiusPercent / 100f;

            if (ImGui.IsItemHovered())
                CustomComponents.TooltipForLastItem("Disc radius", "Radius of the photo disc around each reference point, in percent of the canvas height. Drag, or double-click to type.");

            if (_projectsPhoto && _pinResidualPx > 0.5f)
            {
                ImGui.SameLine();
                CustomComponents.StylizedText($"points miss by up to {_pinResidualPx:0.0} px", Fonts.FontSmall, UiColors.TextMuted);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("More than four points over-determine a corner pin; the solve averages them.\nA large miss means a point is misplaced, or the wall isn't flat.\nEach point's arrow shows which way its own miss goes.");
            }
        }

        if (_projectsPhoto && photoCarrier != null && TryGetTracedFragment(setup, photoCarrier, out var photoSrv, out var photoUvMin, out var photoUvMax))
            CalibrationOverlay.SetCalibrationPhoto(photoCarrier.Id, photoSrv!, photoUvMin, photoUvMax, UserSettings.Config.OutputSetupPhotoDiscRadius);

        // Measuring only makes sense against the straightened surface — on the projector canvas the
        // lengths would be perspective-foreshortened and mean nothing.
        // The line tool serves both Straight flows: on the photo it refines the trace, on the projector the pin.
        var tracedForLines = _editMode == EditModes.Straight ? TracedImageOf(setup, _shownSurfaceId) : null;
        var lineSubject = tracedForLines != null ? setup.FindSurface(_shownSurfaceId) : straightCarrier;
        if (_editMode == EditModes.Straight && lineSubject != null)
        {
            ImGui.SameLine();
            if (CustomComponents.StateButton("+ Line", _isLineToolArmed ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _isLineToolArmed = !_isLineToolArmed;

            if (_isLineToolArmed)
                _isPointToolArmed = false;

            // Reference points are placed on the photo — a physical feature the projector will be aimed at.
            if (tracedForLines != null)
            {
                ImGui.SameLine();
                if (CustomComponents.StateButton("+ Point", _isPointToolArmed ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                {
                    _isPointToolArmed = !_isPointToolArmed;
                    if (_isPointToolArmed)
                        _isLineToolArmed = false;
                }
            }

            // Nothing else on the canvas says a click or drag is now expected, and the tools disarm after
            // one use — so say what to do with them while armed.
            if (_isLineToolArmed)
            {
                ImGui.SameLine();
                CustomComponents.StylizedText("drag along something straight in reality", Fonts.FontSmall, UiColors.StatusAnimated);
            }
            else if (_isPointToolArmed)
            {
                ImGui.SameLine();
                CustomComponents.StylizedText("click a feature you can find on the real wall", Fonts.FontSmall, UiColors.StatusAnimated);
            }

            // Straighten first (it fixes the keystone but cannot know the aspect), lengths second. Both
            // stay visible and disabled rather than appearing once they happen to qualify — a button that
            // isn't there yet can't explain what it wants.
            var canStraighten = SurfaceMetrics.CountLines(lineSubject) >= MinLinesToStraighten;
            ImGui.SameLine();
            ImGui.BeginDisabled(!canStraighten);
            if (ImGui.SmallButton("Straighten") && canStraighten)
            {
                if (tracedForLines != null)
                    SetupUndo.RunUndoable("Straighten trace from lines", setup, () => TryStraightenTraceFromLines(lineSubject));
                else
                    SetupUndo.RunUndoable("Straighten from lines", setup, () => TryStraightenFromLines(setup, lineSubject, outputId));
            }

            ImGui.EndDisabled();
            if (!canStraighten && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Trace at least {MinLinesToStraighten} reference lines along features that are straight in reality.");

            var canApply = SurfaceMetrics.HasMeasuredLine(lineSubject);
            ImGui.SameLine();
            ImGui.BeginDisabled(!canApply);
            if (ImGui.SmallButton("Apply lengths") && canApply)
                SetupUndo.RunUndoable("Apply lengths", setup, () => TryApplyLengths(setup, lineSubject));

            ImGui.EndDisabled();
            if (!canApply && ImGui.IsItemHovered())
                ImGui.SetTooltip("Double-click a line to give it a real length first.");
        }
        else
        {
            _isLineToolArmed = false;
            _isPointToolArmed = false;
        }

        // "+ <surface>" maps a surface onto this output — an Output-canvas action; the Board has no output to map to.
        if (output == null || _editMode == EditModes.Board)
            return;

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.HasMapping(outputId))
                continue;

            // A Layout child rides its parent's corner pin — offering it one of its own would detach it.
            if (surface.Kind == Surface.Kinds.Layout && surface.ParentId != Guid.Empty)
                continue;

            ImGui.SameLine();
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            if (ImGui.SmallButton("+ " + label))
            {
                SetupUndo.RunUndoable("Map surface", setup, () => SetupActions.AddMapping(surface, output, outputId));
            }

            CustomComponents.TooltipForLastItem("Map this surface onto the output",
                                                "Drops a centered corner-pin quad you can then drag into place.");
            ImGui.PopID();
        }
    }

    // The header the last drawn canvas deferred to the strip.
    private HeaderKinds _pendingHeaderKind;
    private Guid _pendingHeaderOutputId;
    private string _pendingHeaderTitle = string.Empty;
    private Guid _pendingHeaderImageId;
    private Guid _pendingHeaderSubjectId;
}
