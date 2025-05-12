﻿using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphCanvas
{
    public void DrawGraph(ImDrawListPtr drawList, float graphOpacity)
    {
        
        IsFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        IsHovered = ImGui.IsWindowHovered();

        var result = _context.DrawDialogs(_projectView);

        if (result == ChangeSymbol.SymbolModificationResults.Nothing)
        {
            result |= KeyboardActions.HandleKeyboardActions(_context);
        }
        
        // General pre-update
        if ((result & ChangeSymbol.SymbolModificationResults.ProjectViewDiscarded) != 0)
        {
            Log.Debug("Skip graph draw after composition update...");
            return;
        }

        if (_context.ProjectView.InstView is not { IsValid: true })
        {
            Log.Warning("Failed to draw graph view without valid composition instance");
            return;
        }
        
        
        HandleSymbolDropping(_context);

        // Update view scope if required
        if (FitViewToSelectionHandling.FitViewToSelectionRequested)
        {
            FocusViewToSelection(_context);
        }
        

        // Keep visible canvas area to cull non-visible objects later
        _visibleCanvasArea = GetVisibleCanvasArea();

        var editingFlags = T3Ui.EditingFlags.None;
        if (T3Ui.IsAnyPopupOpen)
        {
            if(!ImGui.IsWindowHovered())
                editingFlags |= T3Ui.EditingFlags.PreventMouseInteractions;
        }
        
        UpdateCanvas(out _, editingFlags);

        // Prepare UiModel for frame
        _context.Layout.ComputeLayout(_context);
        _context.ItemMovement.PrepareFrame();
        
        if (_context.StateMachine.CurrentState == GraphStates.Default)
        {
            _context.ActiveItem = null;
            _context.ItemWithActiveCustomUi = null;
            _context.ActiveSourceOutputId = Guid.Empty;
            _context.ActiveTargetInputId = Guid.Empty;
        }

        if(!UserSettings.Config.FocusMode) 
        {
            DrawBackgroundGrids(drawList);
        } 

        // Selection fence...
        {
            HandleFenceSelection(_context, _selectionFence);
        }
        
        // Draw annotations
        foreach (var a in _context.Layout.Annotations.Values)
        {
            DrawAnnotation(a, drawList, _context);
        }

        // Draw items
        foreach (var item in _context.Layout.Items.Values)
        {
            DrawNode(item, drawList, _context);
        }

        Fonts.FontSmall.Scale = 1; // WTF. Some of the drawNode seems to spill out fontSize

        // Update active or hovered item
        // Doing this after rendering will add slight frame delay but will
        // keep drag operations more consistent.
        if (_context.ActiveItem != null)
        {
            if (_context.ActiveItem.Id != _lastHoverId)
            {
                _hoverStartTime = ImGui.GetTime();
                _lastHoverId = _context.ActiveItem.Id;
            }

            _context.Selector.HoveredIds.Add(_context.ActiveItem.Id);
        }
        else
        {
            _hoverStartTime = ImGui.GetTime();
            _lastHoverId = Guid.Empty;
        }

        HighlightSplitInsertionPoints(drawList, _context);

        // Draw connections
        foreach (var connection in _context.Layout.MagConnections)
        {
            DrawConnection(connection, drawList, _context);
        }

        // Draw special overlays
        if (_context.StateMachine.CurrentState == GraphStates.RenameChild)
        {
            RenamingOperator.Draw(_projectView);
            if (!RenamingOperator.IsOpen)
            {
                _context.StateMachine.SetState(GraphStates.Default, _context);
            }
        }
        else if (_context.StateMachine.CurrentState == GraphStates.RenameAnnotation)
        {
            AnnotationRenaming.Draw(_context);
        }
        else if (_context.StateMachine.CurrentState == GraphStates.DragAnnotation)
        {
            AnnotationDragging.Draw(_context);
        }
        
        
        
        // Draw temp connections
        foreach (var tc in _context.TempConnections)
        {
            var mousePos = ImGui.GetMousePos();

            var sourcePosOnScreen = mousePos;
            var targetPosOnScreen = mousePos;

            // Dragging end to new target input...
            if (tc.SourceItem != null)
            {
                var sourcePos = new Vector2(tc.SourceItem.Area.Max.X,
                                            tc.SourceItem.Area.Min.Y + MagGraphItem.GridSize.Y * (0.5f + tc.OutputLineIndex));

                sourcePosOnScreen = TransformPosition(sourcePos);

                if (_context.StateMachine.CurrentState == GraphStates.DragConnectionEnd
                    && InputSnapper.BestInputMatch.Item != null)
                {
                    targetPosOnScreen = InputSnapper.BestInputMatch.PosOnScreen;
                }
            }

            // Dragging beginning to new source output...
            if (tc.TargetItem != null)
            {
                var targetPos = new Vector2(tc.TargetItem.Area.Min.X,
                                            tc.TargetItem.Area.Min.Y + MagGraphItem.GridSize.Y * (0.5f + tc.InputLineIndex));
                targetPosOnScreen = TransformPosition(targetPos);

                if (_context.StateMachine.CurrentState == GraphStates.DragConnectionBeginning
                    && OutputSnapper.BestOutputMatch.Item != null)
                {
                    sourcePosOnScreen = TransformPosition(OutputSnapper.BestOutputMatch.Anchor.PositionOnCanvas);
                }

                var isDisconnectedMultiInput = tc.InputLineIndex >= tc.TargetItem.InputLines.Length;
                if (isDisconnectedMultiInput)
                    continue;
            }
            else
            {
                if (_context.StateMachine.CurrentState == GraphStates.Placeholder)
                {
                    if (_context.Placeholder.PlaceholderItem != null)
                    {
                        targetPosOnScreen = TransformPosition(_context.Placeholder.PlaceholderItem.PosOnCanvas);
                    }
                }
            }

            var typeColor = TypeUiRegistry.GetPropertiesForType(tc.Type).Color;
            var d = Vector2.Distance(sourcePosOnScreen, targetPosOnScreen) / 2;

            drawList.AddBezierCubic(sourcePosOnScreen,
                                    sourcePosOnScreen + new Vector2(d, 0),
                                    targetPosOnScreen - new Vector2(d, 0),
                                    targetPosOnScreen,
                                    typeColor.Fade(0.6f),
                                    2);
        }

        OutputSnapper.Update(_context);
        InputSnapper.Update(_context);

        _context.ConnectionHovering.PrepareNewFrame(_context);
        _context.Placeholder.Update(_context);

        // Draw animated Snap indicator
        {
            var timeSinceSnap = ImGui.GetTime() - _context.ItemMovement.LastSnapTime;
            var progress = ((float)timeSinceSnap).RemapAndClamp(0, 0.4f, 1, 0);
            if (progress < 1)
            {
                drawList.AddCircle(TransformPosition(_context.ItemMovement.LastSnapTargetPositionOnCanvas),
                                   progress * 50,
                                   UiColors.ForegroundFull.Fade(progress * 0.2f));
            }
        }

        if (FrameStats.Current.OpenedPopUpName == string.Empty)
            CustomComponents.DrawContextMenuForScrollCanvas(() => GraphContextMenu.DrawContextMenuContent(_context, _projectView), ref _contextMenuIsOpen);

        SmoothItemPositions();

        _context.StateMachine.UpdateAfterDraw(_context);
    }

    /// <summary>
    /// This a very simple proof-of-concept implementation to test it's fidelity.
    /// A simple optimization could be to only do this for some time after a drag manipulation and then apply
    /// the correct position. Also, this animation does not affect connection lines.
    ///
    /// It still helps to understand what's going on and feels satisfying. So we're keeping it for now.
    /// </summary>
    private void SmoothItemPositions()
    {
        foreach (var i in _context.Layout.Items.Values)
        {
            var dampAmount = _context.ItemMovement.DraggedItems.Contains(i)
                                 ? 0.0f
                                 : 0.7f;
            i.DampedPosOnCanvas = Vector2.Lerp(i.PosOnCanvas, i.DampedPosOnCanvas, dampAmount);
        }
        
        foreach (var a in _context.Layout.Annotations.Values)
        {
            // var dampAmount = _context.ItemMovement.DraggedItems.Contains(i)
            //                      ? 0.0f
            //                      : 0.7f;
            var dampAmount = 0.7f;
            a.DampedPosOnCanvas = Vector2.Lerp(a.Annotation.PosOnCanvas, a.DampedPosOnCanvas, dampAmount);
            a.DampedSize = Vector2.Lerp(a.Annotation.Size, a.DampedSize, dampAmount);
        }
        
    }

    private bool _contextMenuIsOpen;

    private void HighlightSplitInsertionPoints(ImDrawListPtr drawList, GraphUiContext context)
    {
        foreach (var sp in context.ItemMovement.SpliceSets)
        {
            var inputItem = context.ItemMovement.DraggedItems.FirstOrDefault(i => i.Id == sp.InputItemId);
            if (inputItem == null)
                continue;

            var center = TransformPosition(inputItem.PosOnCanvas + sp.AnchorOffset);

            var type = inputItem.InputLines.Length > 0 ? inputItem.InputLines[0].Type : null;
            var typeColor = TypeUiRegistry.GetPropertiesForType(type).Color;

            if (sp.Direction == MagGraphItem.Directions.Vertical)
            {
                var offset = MagGraphItem.GridSize.X / 16 * CanvasScale;
                // var offset = sp.Direction == MagGraphItem.Directions.Vertical
                //                  ? new Vector2(MagGraphItem.GridSize.X / 16 * CanvasScale, 0)
                //                  : new Vector2(0, MagGraphItem.GridSize.Y / 8 * CanvasScale);

                drawList.AddRectFilled(center+ new Vector2(-offset, -1),
                                       center+ new Vector2(offset, 1),
                                       ColorVariations.ConnectionLines.Apply(typeColor).Fade(Blink)   
                                      );
            }
            else
            {
                center.X += sp.DragPositionWithinBlock.X * CanvasScale;
                var offset = MagGraphItem.GridSize.Y *0.25f * CanvasScale;
                drawList.AddRectFilled(center+ new Vector2(0, -offset),
                                       center+ new Vector2(2, offset),
                                       ColorVariations.ConnectionLines.Apply(typeColor).Fade(Blink)   
                                      );
            }
        }
    }

    private void DrawBackgroundGrids(ImDrawListPtr drawList)
    {
        var minSize = MathF.Min(MagGraphItem.GridSize.X, MagGraphItem.GridSize.Y);
        var gridSize = Vector2.One * minSize;
        var maxOpacity = 0.25f;

        var fineGrid = MathUtils.RemapAndClamp(Scale.X, 0.5f, 2f, 0.0f, maxOpacity);
        if (fineGrid > 0.01f)
        {
            var color = UiColors.BackgroundFull.Fade(fineGrid);
            DrawBackgroundGrid(drawList, gridSize, color);
        }

        var roughGrid = MathUtils.RemapAndClamp(Scale.X, 0.1f, 2f, 0.0f, maxOpacity);
        if (roughGrid > 0.01f)
        {
            var color = UiColors.BackgroundFull.Fade(roughGrid);
            DrawBackgroundGrid(drawList, gridSize * 5, color);
        }
    }

    private void DrawBackgroundGrid(ImDrawListPtr drawList, Vector2 gridSize, Color color)
    {
        var window = new ImRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());

        var topLeftOnCanvas = InverseTransformPositionFloat(ImGui.GetWindowPos());
        var alignedTopLeftCanvas = new Vector2((int)(topLeftOnCanvas.X / gridSize.X) * gridSize.X,
                                               (int)(topLeftOnCanvas.Y / gridSize.Y) * gridSize.Y);

        var topLeftOnScreen = TransformPosition(alignedTopLeftCanvas);
        var screenGridSize = TransformDirection(gridSize);

        var count = new Vector2(window.GetWidth() / screenGridSize.X, window.GetHeight() / screenGridSize.Y);

        for (int ix = 0; ix < 200 && ix <= count.X + 1; ix++)
        {
            var x = (int)(topLeftOnScreen.X + ix * screenGridSize.X);
            drawList.AddRectFilled(new Vector2(x, window.Min.Y),
                                   new Vector2(x + 1, window.Max.Y),
                                   color);
        }

        for (int iy = 0; iy < 200 && iy <= count.Y + 1; iy++)
        {
            var y = (int)(topLeftOnScreen.Y + iy * screenGridSize.Y);
            drawList.AddRectFilled(new Vector2(window.Min.X, y),
                                   new Vector2(window.Max.X, y + 1),
                                   color);
        }
    }

    [Flags]
    private enum Borders
    {
        None = 0,
        Up = 1,
        Right = 2,
        Down = 4,
        Left = 8,
    }

    private static readonly ImDrawFlags[] _borderRoundings =
        {
            ImDrawFlags.RoundCornersAll, //        0000      
            ImDrawFlags.RoundCornersBottom, //     0001                 up
            ImDrawFlags.RoundCornersLeft, //       0010           right
            ImDrawFlags.RoundCornersBottomLeft, // 0011           right up
            ImDrawFlags.RoundCornersTop, //        0100      down
            ImDrawFlags.RoundCornersNone, //       0101      down       up
            ImDrawFlags.RoundCornersTopLeft, //    0110      down right  
            ImDrawFlags.RoundCornersNone, //       0111      down right up  

            ImDrawFlags.RoundCornersRight, //      1000 left
            ImDrawFlags.RoundCornersBottomRight, //1001 left            up
            ImDrawFlags.RoundCornersNone, //       1010 left      right
            ImDrawFlags.RoundCornersNone, //       1011 left      right up
            ImDrawFlags.RoundCornersTopRight, //   1100 left down
            ImDrawFlags.RoundCornersNone, //       1101 left down       up
            ImDrawFlags.RoundCornersNone, //       1110 left down right  
            ImDrawFlags.RoundCornersNone, //       1111 left down right up  
        };

    internal static float Blink => MathF.Sin((float)ImGui.GetTime() * 10) * 0.5f + 0.5f;

}