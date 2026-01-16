#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
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
public abstract class CurveEditing
{
    protected internal readonly HashSet<VDefinition> SelectedKeyframes = new();
    protected abstract IEnumerable<Curve> GetAllCurves();
    protected abstract void ViewAllOrSelectedKeys(bool alsoChangeTimeRange = false);
    protected abstract void DeleteSelectedKeyframes(Instance composition);
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

            var editModes = selectedInterpolations as VDefinition.EditMode[] ?? selectedInterpolations.ToArray();

            bool changed = false;
            if (SelectedKeyframes.Count > 0)
            {
                CustomComponents.HintLabel("Interpolation...");

                if (ImGui.MenuItem("Smooth", null, editModes.Contains(VDefinition.EditMode.Smooth)))
                {
                    OnSmooth();
                    UpdateAllTangents();
                    changed = true;
                }

                if (ImGui.MenuItem("Cubic", null, editModes.Contains(VDefinition.EditMode.Cubic)))
                {
                    OnCubic();
                    UpdateAllTangents();
                    changed = true;
                }

                if (ImGui.MenuItem("Horizontal", null, editModes.Contains(VDefinition.EditMode.Horizontal)))
                {
                    OnHorizontal();
                    UpdateAllTangents();
                    changed = true;
                }

                if (ImGui.MenuItem("Constant", null, editModes.Contains(VDefinition.EditMode.Constant)))
                {
                    OnConstant();
                    UpdateAllTangents();
                    changed = true;
                }

                if (ImGui.MenuItem("Linear", null, editModes.Contains(VDefinition.EditMode.Linear)))
                {
                    OnLinear();
                    UpdateAllTangents();
                    changed = true;
                }

                ImGui.Separator();

                if (ImGui.BeginMenu("Before curve...", SelectedKeyframes.Count > 0))
                {
                    foreach (CurveUtils.OutsideCurveBehavior mapping in Enum.GetValues(typeof(CurveUtils.OutsideCurveBehavior)))
                    {
                        if (ImGui.MenuItem(mapping.ToString(), null))
                        {
                            ApplyPreCurveMapping(mapping);
                            changed = true;
                        }
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("After curve...", SelectedKeyframes.Count > 0))
                {
                    foreach (CurveUtils.OutsideCurveBehavior mapping in Enum.GetValues(typeof(CurveUtils.OutsideCurveBehavior)))
                    {
                        if (ImGui.MenuItem(mapping.ToString(), null))
                        {
                            ApplyPostCurveMapping(mapping);
                            changed = true;
                        }
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem("Delete keyframes", SelectedKeyframes.Count > 0))
                {
                    DeleteSelectedKeyframes(composition);
                    changed = true;
                }

                if (ImGui.MenuItem("Recount values", SelectedKeyframes.Count > 0))
                {
                    var value = 0;
                    ForSelectedOrAllPointsDo((vDef) =>
                                             {
                                                 vDef.Value = value;
                                                 value++;
                                             });

                    changed = true;
                }

                if (TimeLineCanvas.Current != null && ImGui.MenuItem("Duplicate keyframes", SelectedKeyframes.Count > 0))
                {
                    DuplicateSelectedKeyframes(TimeLineCanvas.Current.Playback.TimeInBars);
                    changed = true;
                }
            }

            if (ImGui.MenuItem(SelectedKeyframes.Count > 0 ? "View Selected" : "View All", UserActions.FocusSelection.ListShortcuts()))
                ViewAllOrSelectedKeys();

            if (changed)
            {
                composition.GetSymbolUi().FlagAsModified();
            }
        }
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
                                     vDef.InEditMode = VDefinition.EditMode.Smooth;
                                     vDef.InType = VDefinition.Interpolation.Spline;
                                     vDef.OutEditMode = VDefinition.EditMode.Smooth;
                                     vDef.OutType = VDefinition.Interpolation.Spline;
                                 });
    }

    private void OnCubic()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;
                                     vDef.InEditMode = VDefinition.EditMode.Cubic;
                                     vDef.InType = VDefinition.Interpolation.Spline;
                                     vDef.OutEditMode = VDefinition.EditMode.Cubic;
                                     vDef.OutType = VDefinition.Interpolation.Spline;
                                 });
    }

    private void OnHorizontal()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;

                                     vDef.InEditMode = VDefinition.EditMode.Horizontal;
                                     vDef.InType = VDefinition.Interpolation.Spline;
                                     vDef.InTangentAngle = 0;

                                     vDef.OutEditMode = VDefinition.EditMode.Horizontal;
                                     vDef.OutType = VDefinition.Interpolation.Spline;
                                     vDef.OutTangentAngle = Math.PI;
                                 });
    }

    private void OnConstant()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = true;
                                     vDef.OutType = VDefinition.Interpolation.Constant;
                                     vDef.OutEditMode = VDefinition.EditMode.Constant;
                                 });
    }

    private void OnLinear()
    {
        ForSelectedOrAllPointsDo((vDef) =>
                                 {
                                     vDef.BrokenTangents = false;
                                     vDef.InEditMode = VDefinition.EditMode.Linear;
                                     vDef.InType = VDefinition.Interpolation.Linear;
                                     vDef.OutEditMode = VDefinition.EditMode.Linear;
                                     vDef.OutType = VDefinition.Interpolation.Linear;
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

    private IEnumerable<VDefinition.EditMode> GetSelectedKeyframeInterpolationTypes()
    {
        var checkedInterpolationTypes = new HashSet<VDefinition.EditMode>();
        foreach (var point in GetSelectedOrAllPoints())
        {
            checkedInterpolationTypes.Add(point.OutEditMode);
            checkedInterpolationTypes.Add(point.InEditMode);
        }

        return checkedInterpolationTypes;
    }

    protected IEnumerable<VDefinition> GetAllKeyframes()
    {
        return from curve in GetAllCurves()
               from keyframe in curve.GetVDefinitions()
               select keyframe;
    }

    protected void DuplicateSelectedKeyframes(double targetTime)
    {
        if (!SelectedKeyframes.Any())
        {
            Log.Debug("Select keyframes to duplicate to current time");
            return;
        }

        var minTime = float.PositiveInfinity;
        foreach (var key in SelectedKeyframes)
        {
            minTime = Math.Min((float)key.U, minTime);
        }

        var newSelection = new HashSet<VDefinition>();

        foreach (var curve in GetAllCurves())
        {
            foreach (var key in curve.GetVDefinitions().ToList())
            {
                if (!SelectedKeyframes.Contains(key))
                    continue;

                var timeOffset = key.U - minTime;
                var newKey = key.Clone();
                curve.AddOrUpdateV(targetTime + timeOffset, newKey);
                newSelection.Add(newKey);
            }
        }

        RebuildCurveTables();
        SelectedKeyframes.Clear();
        SelectedKeyframes.UnionWith(newSelection);
    }

    /// <summary>
    /// Align curve table-structure aligned with position stored in key definitions.
    /// </summary>
    protected void RebuildCurveTables()
    {
        foreach (var curve in GetAllCurves())
        {
            foreach( var (u,vDef) in curve.Table.ToList()) // Copy before modifications
            {
                if (Math.Abs(u - vDef.U) > 0.001f)
                {
                    curve.MoveKey(u, vDef.U);
                }
            }
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
}