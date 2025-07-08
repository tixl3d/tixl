#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.WithCurves;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.TimeLine;
using T3.Editor.Gui.Windows.TimeLine.Raster;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.InputUi.CombinedInputs;

/// <summary>
/// Handles editing of Curve-Inputs in parameter windows and Graph CustomUi.
/// </summary>
/// <remarks>
/// The view settings (selection, zoom, scale, etc) for each canvas are stored in a dictionary of <see cref="T3.Editor.Gui.InputUi.CombinedInputs.CurveInputEditing.CurveInteraction"/> instances.  
/// </remarks>
public static class CurveInputEditing
{

    public static InputEditStateFlags DrawCanvasForCurve(ref Curve curveRef, Symbol.Child.Input input, bool cloneIfModified, Instance compositionOp,
                                                         T3Ui.EditingFlags flags = T3Ui.EditingFlags.None)
    {
        var keepScale = T3Ui.UiScaleFactor;
        T3Ui.UiScaleFactor = 1;

            
        var imGuiId = ImGui.GetID("");
        var curveForEditing = curveRef;
        if (cloneIfModified)
        {
            if (!_clonedDefaultCurves.TryGetValue(input.DefaultValue, out var clonedCurve))
            {
                clonedCurve = curveRef.TypedClone();
                _clonedDefaultCurves[input.DefaultValue] = clonedCurve;
            }

            curveForEditing = clonedCurve;
        }
            
        _interactionFlags = flags;
        if (!_interactionForCurve.TryGetValue(imGuiId, out var curveInteraction))
        {
            curveInteraction = new CurveInteraction()
                                   {
                                       Curves = new List<Curve>() { curveForEditing }
                                   };

            _interactionForCurve.Add(imGuiId, curveInteraction);
        }
        else if (curveInteraction.Curves.Count != 1 || curveInteraction.Curves[0] != curveForEditing)
        {
            curveInteraction.Curves.Clear();
            curveInteraction.Curves.Add(curveForEditing);
        }
            
        curveInteraction.EditState = InputEditStateFlags.Nothing;
        curveInteraction.Draw(compositionOp, _selectionFence);

        var modified = curveInteraction.EditState != InputEditStateFlags.Nothing;

        if (modified && cloneIfModified)
        {
            curveRef = curveForEditing;
            _clonedDefaultCurves.Remove(input.DefaultValue);
        }
           
            
        T3Ui.UiScaleFactor = keepScale;
        return curveInteraction.EditState;
    }

    /// <summary>
    /// Implement interaction of manipulating the individual keyframes
    /// </summary>
    internal sealed class CurveInteraction : CurveEditing
    {
        internal List<Curve> Curves = new();
        private readonly SingleCurveEditCanvas _singleCurveCanvas = new() { ImGuiTitle = "canvas" + _interactionForCurve.Count };

        //public ScalableCanvas Canvas => _canvas;

        internal InputEditStateFlags EditState { get; set; } = InputEditStateFlags.Nothing;

        internal void Draw(Instance compositionOp, SelectionFence selectionFence)
        {
            _singleCurveCanvas.Draw(Curves[0], this, compositionOp, selectionFence);
        }


        #region implement editing ---------------------------------------------------------------
        protected override IEnumerable<Curve> GetAllCurves()
        {
            return Curves;
        }

        protected override void ViewAllOrSelectedKeys(bool alsoChangeTimeRange = false)
        {
            _singleCurveCanvas.NeedToAdjustScopeAfterFirstRendering = true;
        }



        protected override void DeleteSelectedKeyframes(Instance compositionOp)
        {
            foreach (var curve in GetAllCurves())
            {
                foreach (var keyframe in curve.GetVDefinitions().ToList())
                {
                    if (!SelectedKeyframes.Contains(keyframe))
                        continue;

                    curve.RemoveKeyframeAt(keyframe.U);
                    SelectedKeyframes.Remove(keyframe);
                }
            }

            EditState = InputEditStateFlags.Modified;
        }

        protected internal override void HandleCurvePointDragging(in Guid compositionSymbolId, VDefinition vDef, bool isSelected)
        {
            if ((_interactionFlags & T3Ui.EditingFlags.PreventMouseInteractions) != 0)
                return;

            // if (ImGui.IsItemHovered())
            // {
            //     ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            // }

            if (!ImGui.IsItemActive())
            {
                if (ImGui.IsItemDeactivated())
                {
                    if (_changeKeyframesCommand != null)
                    {
                        CompleteDragCommand();
                    }
                    else
                    {
                        // This happens when clicking on keyframes for selection...
                        //Log.Error("Deactivated keyframe dragging without valid command?");
                    }
                }

                return;
            }

            // Sadly, this hotkey interferes with the "Allow manipulation in graph custom ui hot key"
            // if (ImGui.GetIO().KeyCtrl)
            // {
            //     if (isSelected)
            //         SelectedKeyframes.Remove(vDef);
            //
            //     return;
            // }

            if (!isSelected)
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    SelectedKeyframes.Clear();
                }

                SelectedKeyframes.Add(vDef);
            }

            if (!ImGui.IsMouseDragging(0, 2f))
                return;

            if (_changeKeyframesCommand == null)
            {
                StartDragCommand(compositionSymbolId);
            }

            var newDragPosition = _singleCurveCanvas.InverseTransformPositionFloat(ImGui.GetIO().MousePos);
            double u = newDragPosition.X;
            var enableSnapping = ImGui.GetIO().KeyShift;
            if (enableSnapping && _singleCurveCanvas.SnapHandlerForU.TryCheckForSnapping(u, out var snappedUValue, _singleCurveCanvas.Scale.X * 1.5f))
            {
                u = snappedUValue;
            }

            double v = newDragPosition.Y;

            if (enableSnapping && _singleCurveCanvas.SnapHandlerForV.TryCheckForSnapping(v, out var snappedVValue, _singleCurveCanvas.Scale.Y * 1.5f))
            {
                v = snappedVValue;
            }

            UpdateDragCommand(u - vDef.U, v - vDef.Value);

            EditState = InputEditStateFlags.Modified;
        }

        private ICommand StartDragCommand(Guid symbolId)
        {
            _changeKeyframesCommand = new ChangeKeyframesCommand(SelectedKeyframes, GetAllCurves());
            return _changeKeyframesCommand;
        }

        private void UpdateDragCommand(double dt, double dv)
        {
            foreach (var vDefinition in SelectedKeyframes)
            {
                vDefinition.U += dt;
                vDefinition.Value += dv;
            }

            RebuildCurveTables();
            EditState = InputEditStateFlags.Modified;
        }

        // FIXME: This needs to be called
        private void CompleteDragCommand()
        {
            if (_changeKeyframesCommand == null)
                return;

            _changeKeyframesCommand.StoreCurrentValues();
            UndoRedoStack.Add(_changeKeyframesCommand);
            _changeKeyframesCommand = null;
            EditState = InputEditStateFlags.Finished;
        }

        private static ChangeKeyframesCommand? _changeKeyframesCommand;
        #endregion

        #region handle selection ----------------------------------------------------------------
        private void HandleFenceSelection(SelectionFence selectionFence)
        {
            switch (selectionFence.UpdateAndDraw(out _))
            {
                case SelectionFence.States.Updated:
                    var boundsInCanvas = _singleCurveCanvas.InverseTransformRect(selectionFence.BoundsInScreen).MakePositive();
                    SelectedKeyframes.Clear();
                    foreach (var point in GetAllKeyframes())
                    {
                        if (boundsInCanvas.Contains(new Vector2((float)point.U, (float)point.Value)))
                            SelectedKeyframes.Add(point);
                    }

                    break;

                case SelectionFence.States.CompletedAsClick:
                    SelectedKeyframes.Clear();
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Implement canvas for showing and manipulating curve
        /// </summary>
        internal sealed class SingleCurveEditCanvas : CurveEditCanvas
        {
            public SingleCurveEditCanvas()
            {
                SnapHandlerForU.AddSnapAttractor(_standardRaster);
                SnapHandlerForV.AddSnapAttractor(_horizontalRaster);
            }

            public void Draw(Curve curve, CurveInteraction interaction, Instance compositionOp, SelectionFence selectionFence)
            {
                var height = _interactionFlags.HasFlag(T3Ui.EditingFlags.ExpandVertically)
                                 ? ImGui.GetContentRegionAvail().Y
                                 : DefaultCurveParameterHeight;

                var interactionFlags = T3Ui.EditingFlags.PreventZoomWithMouseWheel
                                       | T3Ui.EditingFlags.PreventMouseInteractions
                                       | T3Ui.EditingFlags.PreventPanningWithMouse;
                    
                var interactionDisabled = (_interactionFlags & interactionFlags) != T3Ui.EditingFlags.None;
                if (interactionDisabled)
                {
                    var reenableThroughHotkey = ImGui.GetIO().KeyCtrl;
                    if (reenableThroughHotkey)
                    {
                        _interactionFlags &= ~interactionFlags;
                    }
                }

                var graphCanvas = ProjectView.Focused?.GraphCanvas;
                DrawCurveCanvas(DrawCanvasContent, selectionFence, height, _interactionFlags);
                return;

                void DrawCanvasContent(InteractionState interactionState)
                {
                    var compositionSymbolId = compositionOp.Symbol.Id;
                    _standardRaster.Draw(this);
                    _horizontalRaster.Draw(this);
                    curve.UpdateTangents();
                    TimelineCurveEditArea.DrawCurveLine(curve, this, UiColors.Gray);

                    foreach (var keyframe in interaction.GetAllKeyframes().ToArray())
                    {
                        CurvePoint.Draw(compositionSymbolId, keyframe, this, interaction.SelectedKeyframes.Contains(keyframe), interaction);
                    }

                    //var min = ImGui.GetWindowPos() ;
                    //ImGui.GetWindowDrawList().AddText(min, Color.Green, " Curve #" + _objectIdGenerator.GetId(curve, out _));

                    interaction.HandleFenceSelection(selectionFence);

                    // Handle keyboard interaction 
                    if (ImGui.IsWindowHovered() && KeyActionHandling.Triggered(UserActions.FocusSelection))
                    {
                        interaction.ViewAllOrSelectedKeys();
                    }

                    if (ImGui.IsWindowHovered() && KeyActionHandling.Triggered(UserActions.DeleteSelection))
                    {
                        interaction.DeleteSelectedKeyframes(compositionOp);
                    }

                    interaction.DrawContextMenu(compositionOp);
                    HandleCreateNewKeyframes(curve);
                    if (NeedToAdjustScopeAfterFirstRendering)
                    {
                        TryGetBoundsOnCanvas(interaction.GetAllKeyframes(), out var bounds);
                        SetScopeToCanvasArea(bounds, flipY: true, ProjectView.Focused?.GraphCanvas, 30, 15);
                        NeedToAdjustScopeAfterFirstRendering = false;
                    }
                }
            }

            private const float DefaultCurveParameterHeight = 130;
            private readonly StandardValueRaster _standardRaster = new() { EnableSnapping = true };
            private readonly HorizontalRaster _horizontalRaster = new();
            public bool NeedToAdjustScopeAfterFirstRendering = true;
        }
    }

    private static readonly Dictionary<uint, CurveInteraction> _interactionForCurve = new(); 
    private static readonly Dictionary<InputValue, Curve> _clonedDefaultCurves = new();
    private static readonly SelectionFence _selectionFence = new();

    private static T3Ui.EditingFlags _interactionFlags;

    internal enum MoveDirections
    {
        Undecided = 0,
        Vertical,
        Horizontal,
        Both
    }

    internal static MoveDirections MoveDirection = MoveDirections.Undecided;
    internal const float MoveDirectionThreshold = 2;
}