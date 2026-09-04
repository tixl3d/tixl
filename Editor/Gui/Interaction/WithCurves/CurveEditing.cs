#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Interaction.WithCurves;

/// <summary>
/// Editing of a set of curves and keyframes independent of the actual visualization.
/// </summary>
/// <remarks>This provides basic curve editing functionality outside a timeline context, e.g. for CurveParameters</remarks>
internal abstract class CurveEditing
{
    /// <summary>
    /// Selection of keyframes driven by this editor. May be a set private to this instance
    /// (default constructor) or one shared with sibling editors that should see each other's
    /// selection changes — e.g. DopeSheetArea and TimelineCurveEditor on the same timeline.
    /// </summary>
    protected readonly VersionedKeyframeSet SelectedKeyframes;

    protected CurveEditing() : this(null) { }

    protected CurveEditing(VersionedKeyframeSet? sharedSelection)
    {
        SelectedKeyframes = sharedSelection ?? new VersionedKeyframeSet();
    }

    protected abstract IEnumerable<Curve> GetAllCurves();
    protected abstract IEnumerable<KeyframeCopyAndPasting.CurveWithDetails> GetAllCurvesWithDetails();
    protected abstract void ViewAllOrSelectedKeys(bool alsoChangeTimeRange = false);
    protected abstract void DeleteSelectedKeyframes(Instance composition);
    protected abstract void PasteKeyframes();
    protected internal abstract void HandleCurvePointDragging(in Guid compositionSymbolId, VDefinition vDef, bool isSelected);

    /// <summary>
    /// Helper function to extract vDefs from all or selected UI controls across all curves in CurveEditor
    /// </summary>
    /// <returns>a list of curves with a list of vDefs</returns>
    protected IEnumerable<VDefinition> GetSelectedOrAllPoints()
    {
        if (SelectedKeyframes.Count > 0)
        {
            foreach (var x in SelectedKeyframes)
            {
                yield return x;
            }
        }
        else
        {
            foreach (var curve in GetAllCurves())
            {
                foreach (var x in curve.GetVDefinitions())
                {
                    yield return x;
                }
            }
        }
    }

    /// <summary>
    /// Expose the editor's curves to undo/redo consumers (e.g. tangent-drag command construction
    /// in <see cref="CurvePoint"/>). The base <see cref="GetAllCurves"/> is protected; this is
    /// the safe cross-class hook.
    /// </summary>
    internal IEnumerable<Curve> GetCurvesForUndo() => GetAllCurves();

    /// <summary>
    /// UniqueId of the keyframe that is currently hovered across the timeline's views (e.g.
    /// hovering a CEA keyframe should outline the matching DSA row keyframe, and vice versa).
    /// Default null for standalone editors; <see cref="TimelineCurveEditor"/> overrides to
    /// surface the shared TimeLineCanvas state.
    /// </summary>
    internal virtual int? GetHoveredKeyframeUniqueId() => null;

    internal bool TrySelectKeyFrame(Curve curve, VDefinition vDef)
    {
        foreach (var c in GetAllCurves())
        {
            if (c == curve)
            {
                SelectedKeyframes.Add(vDef);
                return true;
            }
        }

        return false;
    }

    protected void DrawContextMenu(Instance composition)
    {
        CustomComponents.DrawContextMenuForScrollCanvas(ContextMenuContentAction, ref _contextMenuIsOpen);
        return;

        void ContextMenuContentAction()
        {
            var selectedInterpolations = GetSelectedKeyframeInterpolationTypes();

            var modes = selectedInterpolations as VDefinition.KeyInterpolation[] ?? selectedInterpolations.ToArray();

            var hasSelection = SelectedKeyframes.Count > 0;
            var changed = false;
            if (hasSelection)
            {
                CustomComponents.DrawMenuGroupLabel("Interpolation");

                if (DrawKeyframeMenuItem(_linearId, "Linear", modes.Contains(VDefinition.KeyInterpolation.Linear)))
                {
                    OnLinear();
                    UpdateAllTangents();
                    changed = true;
                }

                if (DrawKeyframeMenuItem(_smoothClampedId, "Smooth (Clamped)", modes.Contains(VDefinition.KeyInterpolation.Smooth)))
                {
                    OnSmooth();
                    UpdateAllTangents();
                    changed = true;
                }

                if (DrawKeyframeMenuItem(_smoothId, "Smooth", modes.Contains(VDefinition.KeyInterpolation.Cubic)))
                {
                    OnCubic();
                    UpdateAllTangents();
                    changed = true;
                }

                if (DrawKeyframeMenuItem(_horizontalId, "Horizontal", modes.Contains(VDefinition.KeyInterpolation.Horizontal)))
                {
                    OnHorizontal();
                    UpdateAllTangents();
                    changed = true;
                }

                if (DrawKeyframeMenuItem(_constantId, "Constant", modes.Contains(VDefinition.KeyInterpolation.Constant)))
                {
                    OnConstant();
                    UpdateAllTangents();
                    changed = true;
                }

                CustomComponents.SeparatorLine();

                {
                    var allMirrored = GetSelectedOrAllPoints().All(v => !v.BrokenTangents);
                    if (DrawKeyframeMenuItem(_mirrorTangentsId, "Mirror Tangents", allMirrored))
                    {
                        ForSelectedOrAllPointsDo((vDef) =>
                                                 {
                                                     vDef.BrokenTangents = allMirrored; // Toggle
                                                     if (!vDef.BrokenTangents)
                                                     {
                                                         // Sync out angle to mirror in
                                                         vDef.OutTangentAngle = vDef.InTangentAngle + Math.PI;
                                                     }
                                                 });
                        UpdateAllTangents();
                        changed = true;
                    }
                }

                {
                    var anyWeighted = GetSelectedOrAllPoints().Any(v => v.Weighted);
                    if (DrawKeyframeMenuItem(_weightedTensionsId, "Weighted Tensions", anyWeighted))
                    {
                        ForSelectedOrAllPointsDo((vDef) =>
                                                 {
                                                     vDef.Weighted = !anyWeighted; // Toggle
                                                     if (!vDef.Weighted)
                                                     {
                                                         vDef.TensionIn = 1.0f;
                                                         vDef.TensionOut = 1.0f;
                                                     }
                                                 });
                        changed = true;
                    }
                }

                CustomComponents.SeparatorLine();

                if (CustomComponents.DrawSubMenu(_beforeCurveId, "Before Curve"))
                {
                    for (var index = 0; index < OutsideCurveBehaviors.Length; index++)
                    {
                        if (DrawKeyframeMenuItem(_beforeCurveId + 1 + index, OutsideCurveBehaviorLabels[index], false))
                        {
                            ApplyPreCurveMapping(OutsideCurveBehaviors[index]);
                            changed = true;
                        }
                    }

                    ImGui.EndMenu();
                }

                if (CustomComponents.DrawSubMenu(_afterCurveId, "After Curve"))
                {
                    for (var index = 0; index < OutsideCurveBehaviors.Length; index++)
                    {
                        if (DrawKeyframeMenuItem(_afterCurveId + 1 + index, OutsideCurveBehaviorLabels[index], false))
                        {
                            ApplyPostCurveMapping(OutsideCurveBehaviors[index]);
                            changed = true;
                        }
                    }

                    ImGui.EndMenu();
                }

                CustomComponents.SeparatorLine();

                if (DrawKeyframeMenuItem(_copyKeyframesId, "Copy Keyframes", false))
                {
                    CopySelectedKeyframes();
                    changed = true;
                }

                if (DrawKeyframeMenuItem(_duplicateKeyframesId, "Duplicate Keyframes", false, TimeLineCanvas.Current != null))
                {
                    DuplicateSelectedKeyframes();
                    changed = true;
                }
            }

            if (DrawKeyframeMenuItem(_pasteKeyframesId, "Paste Keyframes", false, KeyframeCopyAndPasting.HasValidClipboard))
            {
                PasteKeyframes();
                changed = true;
            }

            if (hasSelection)
            {
                CustomComponents.SeparatorLine();

                if (DrawKeyframeMenuItem(_spaceEvenlyId, "Space Evenly", false, SelectedKeyframes.Count > 2))
                {
                    EvenlySpaceKeyframes();
                    changed = true;
                }

                if (CustomComponents.DrawSubMenu(_sequentialValuesId, "Set Sequential Values", SelectedKeyframes.Count > 1))
                {
                    if (DrawKeyframeMenuItem(_startAtZeroId, "Start at 0", false))
                    {
                        var value = 0;
                        ForSelectedOrAllPointsDo((vDef) =>
                                                 {
                                                     vDef.Value = value;
                                                     value++;
                                                 });

                        changed = true;
                    }

                    if (DrawKeyframeMenuItem(_startAtFirstId, "Start at First", false))
                    {
                        var selectedOrAllPoints = GetSelectedOrAllPoints().OrderBy(v => v.U).ToList();
                        if (selectedOrAllPoints.Count > 0)
                        {
                            var value = selectedOrAllPoints[0].Value;
                            ForSelectedOrAllPointsDo((vDef) =>
                                                     {
                                                         vDef.Value = value;
                                                         value++;
                                                     });
                        }

                        changed = true;
                    }

                    ImGui.EndMenu();
                }

                if (DrawKeyframeMenuItem(_deleteKeyframesId, "Delete Keyframes", false))
                {
                    DeleteSelectedKeyframes(composition);
                    changed = true;
                }
            }

            CustomComponents.SeparatorLine();

            if (CustomComponents.DrawMenuItem(_viewKeyframesId,
                                              hasSelection ? "View Selected" : "View All",
                                              UserActions.FocusSelection.ListShortcuts(),
                                              reserveIconColumn: false))
            {
                ViewAllOrSelectedKeys();
            }

            if (changed)
            {
                composition.GetSymbolUi().FlagAsModified();
            }
        }
    }

    /// <summary>
    /// This menu has no icons, so the icon column stays unreserved and rows line up with
    /// <see cref="CustomComponents.DrawMenuGroupLabel"/>.
    /// </summary>
    private static bool DrawKeyframeMenuItem(int id, string label, bool isChecked, bool isEnabled = true)
    {
        return CustomComponents.DrawMenuItem(id, label, null, isChecked, isEnabled, reserveIconColumn: false);
    }

    private void UpdateAllTangents()
    {
        foreach (var curve in GetAllCurves())
        {
            curve.UpdateTangents();
        }
    }

    private bool _contextMenuIsOpen;

    private delegate void DoSomethingWithKeyframeDelegate(VDefinition v);

    private void CopySelectedKeyframes()
    {
        var newCurves = new List<KeyframeCopyAndPasting.CurveWithDetails>();
        foreach (var curveWithDetails in GetAllCurvesWithDetails())
        {
            Curve? newCurve = null;
            
            foreach (var keyframe in curveWithDetails.Curve.GetVDefinitions())
            {
                if (!SelectedKeyframes.Contains(keyframe))
                    continue;

                newCurve ??= new Curve();

                newCurve.AddOrUpdateV(keyframe.U, keyframe.Clone());                
            }

            if (newCurve == null) 
                continue;
            
            var newCurveWithDetails = curveWithDetails; // Copies struct
            newCurveWithDetails.Curve = newCurve;
            newCurves.Add(newCurveWithDetails);
        }

        KeyframeCopyAndPasting.SetClipboard(newCurves);
    }
    

    
    
    private void EvenlySpaceKeyframes()
    {
        var selectedOrAllPoints = GetSelectedOrAllPoints().OrderBy(v => v.U).ToList();
        if (selectedOrAllPoints.Count <= 2)
        {
            Log.Debug("You need to select 3 or more keyframes to distribute timing");
            return;
        }
        
        var startTime = selectedOrAllPoints[0].U;
        var endTime = selectedOrAllPoints[^1].U;
        var duration = endTime - startTime;
        if (duration < 0.00001)
        {
            Log.Warning("Keyframes overlap");
            return;
        }
        
        var cmd = new ChangeKeyframesCommand(selectedOrAllPoints, GetAllCurves());
        
        var groups = selectedOrAllPoints
                    .GroupBy(k => k.U)
                    .OrderBy(g => g.Key)
                    .ToList();
        
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var f = groups.Count > 1
                        ? i / (double)(groups.Count - 1)
                        : 0.0;
            
            foreach (var key in g)
            {
                key.U = MathUtils.Lerp(startTime, endTime, f);
            } 
        }

        cmd.StoreCurrentValues();
        UndoRedoStack.Add(cmd);
    }

    
    private void ForSelectedOrAllPointsDo(DoSomethingWithKeyframeDelegate doFunc)
    {
        var selectedOrAllPoints = GetSelectedOrAllPoints().OrderBy(v => v.U).ToList();
        var cmd = new ChangeKeyframesCommand(selectedOrAllPoints, GetAllCurves());

        foreach (var keyframe in selectedOrAllPoints)
        {
            doFunc(keyframe);
        }

        cmd.StoreCurrentValues();
        UndoRedoStack.Add(cmd);
    }

    private void OnSmooth()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;
                                     vDef.InInterpolation = VDefinition.KeyInterpolation.Smooth;
                                     vDef.OutInterpolation = VDefinition.KeyInterpolation.Smooth;
                                     vDef.TensionIn = 1.0f;
                                     vDef.TensionOut = 1.0f;
                                     vDef.Weighted = false;
                                 });
        
    }

    private void OnCubic()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;
                                     vDef.InInterpolation = VDefinition.KeyInterpolation.Cubic;
                                     vDef.OutInterpolation = VDefinition.KeyInterpolation.Cubic;
                                     vDef.TensionIn = 1.0f;
                                     vDef.TensionOut = 1.0f;
                                     vDef.Weighted = false;
                                 });
    }

    private void OnHorizontal()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;
                                     vDef.InInterpolation = VDefinition.KeyInterpolation.Horizontal;
                                     vDef.InTangentAngle = 0;
                                     vDef.OutInterpolation = VDefinition.KeyInterpolation.Horizontal;
                                     vDef.OutTangentAngle = Math.PI;
                                     vDef.TensionIn = 1.0f;
                                     vDef.TensionOut = 1.0f;
                                     vDef.Weighted = false;
                                 });
    }

    private void OnConstant()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = true;
                                     vDef.OutInterpolation = VDefinition.KeyInterpolation.Constant;
                                     vDef.TensionIn = 1.0f;
                                     vDef.TensionOut = 1.0f;
                                     vDef.Weighted = false;
                                 });
    }

    private void OnLinear()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = true;
                                     vDef.InInterpolation = VDefinition.KeyInterpolation.Linear;
                                     vDef.OutInterpolation = VDefinition.KeyInterpolation.Linear;
                                     vDef.TensionIn = 1.0f;
                                     vDef.TensionOut = 1.0f;
                                     vDef.Weighted = false;
                                 });
    }

    private void ApplyPostCurveMapping(CurveUtils.OutsideCurveBehavior mapping)
    {
        foreach (var curve in GetAllCurves())
        {
            curve.PostCurveMapping = mapping;
        }
    }

    private void ApplyPreCurveMapping(CurveUtils.OutsideCurveBehavior mapping)
    {
        foreach (var curve in GetAllCurves())
        {
            curve.PreCurveMapping = mapping;
        }
    }

    private IEnumerable<VDefinition.KeyInterpolation> GetSelectedKeyframeInterpolationTypes()
    {
        var checkedInterpolationTypes = new HashSet<VDefinition.KeyInterpolation>();
        foreach (var point in GetSelectedOrAllPoints())
        {
            checkedInterpolationTypes.Add(point.OutInterpolation);
            checkedInterpolationTypes.Add(point.InInterpolation);
        }

        return checkedInterpolationTypes;
    }

    protected IEnumerable<VDefinition> GetAllKeyframes()
    {
        return from curve in GetAllCurves()
               from keyframe in curve.GetVDefinitions()
               select keyframe;
    }

    protected void DuplicateSelectedKeyframes()
    {
        CopySelectedKeyframes();
        PasteKeyframes();
    }

    /// <summary>
    /// Align curve table-structure aligned with position stored in key definitions.
    /// </summary>
    protected void RebuildCurveTables()
    {
        foreach (var curve in GetAllCurves())
        {
            curve.BeginBatchEdit();
            foreach (var (u, vDef) in curve.Table.ToList()) // Copy before modifications
            {
                if (Math.Abs(u - vDef.U) > 0.001f)
                {
                    curve.MoveKey(u, vDef.U);
                }
            }
            curve.EndBatchEdit();
        }
    }

    protected static bool TryGetBoundsOnCanvas(IEnumerable<VDefinition> keyframes, out ImRect bounds)
    {
        var foundOneOrMoreKeys = false;
        
        bounds = new ImRect(-Vector2.One, Vector2.One);
        var isFirst = true;
        foreach (var k in keyframes)
        {
            foundOneOrMoreKeys = true;
            var p = new Vector2((float)k.U, (float)k.Value);

            if (isFirst)
            {
                bounds = new ImRect(p, p);
                isFirst = false;
            }
            else
            {
                bounds.Add(p);
            }
        }

        return foundOneOrMoreKeys;
    }

    // Parallel arrays instead of Enum.GetValues + ToString(): the menu redraws every frame while open,
    // and enum-to-string allocates. The labels also need spacing that the enum names don't carry.
    private static readonly CurveUtils.OutsideCurveBehavior[] OutsideCurveBehaviors =
        [
            CurveUtils.OutsideCurveBehavior.Constant,
            CurveUtils.OutsideCurveBehavior.Cycle,
            CurveUtils.OutsideCurveBehavior.CycleWithOffset,
            CurveUtils.OutsideCurveBehavior.Oscillate,
        ];

    private static readonly string[] OutsideCurveBehaviorLabels = ["Constant", "Cycle", "Cycle with Offset", "Oscillate"];

    private static readonly int _linearId = nameof(_linearId).GetHashCode();
    private static readonly int _smoothClampedId = nameof(_smoothClampedId).GetHashCode();
    private static readonly int _smoothId = nameof(_smoothId).GetHashCode();
    private static readonly int _horizontalId = nameof(_horizontalId).GetHashCode();
    private static readonly int _constantId = nameof(_constantId).GetHashCode();
    private static readonly int _mirrorTangentsId = nameof(_mirrorTangentsId).GetHashCode();
    private static readonly int _weightedTensionsId = nameof(_weightedTensionsId).GetHashCode();
    private static readonly int _copyKeyframesId = nameof(_copyKeyframesId).GetHashCode();
    private static readonly int _duplicateKeyframesId = nameof(_duplicateKeyframesId).GetHashCode();
    private static readonly int _pasteKeyframesId = nameof(_pasteKeyframesId).GetHashCode();
    private static readonly int _spaceEvenlyId = nameof(_spaceEvenlyId).GetHashCode();
    private static readonly int _startAtZeroId = nameof(_startAtZeroId).GetHashCode();
    private static readonly int _startAtFirstId = nameof(_startAtFirstId).GetHashCode();
    private static readonly int _deleteKeyframesId = nameof(_deleteKeyframesId).GetHashCode();
    private static readonly int _viewKeyframesId = nameof(_viewKeyframesId).GetHashCode();
    private static readonly int _sequentialValuesId = nameof(_sequentialValuesId).GetHashCode();

    // The rows inside these submenus derive their ids by offsetting the submenu's own id.
    private static readonly int _beforeCurveId = nameof(_beforeCurveId).GetHashCode();
    private static readonly int _afterCurveId = nameof(_afterCurveId).GetHashCode();
}