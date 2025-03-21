﻿using System.Diagnostics;
using ImGuiNET;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using MagItemMovement = T3.Editor.Gui.MagGraph.Interaction.MagItemMovement;

// ReSharper disable MemberCanBePrivate.Global

namespace T3.Editor.Gui.MagGraph.States;

internal static class GraphStates
{
    internal static State Default
        = new(
              Enter: context =>
                     {
                         // Todo: this should be a reset method in context
                         context.TempConnections.Clear();
                         context.ActiveSourceItem = null;
                         context.ActiveTargetItem = null;
                         context.ActiveTargetInputId = Guid.Empty;
                         
                         context.DraggedPrimaryOutputType = null;

                         // ReSharper disable once ConstantConditionalAccessQualifier
                         // This might not be initialized on startup
                         context.Placeholder.Reset(context);
                         context.DisconnectedInputHashes.Clear();
                     },
              Update: context =>
                      {
                          if (context.ItemWithActiveCustomUi != null)
                              return;

                          // Check keyboard commands if focused...
                          if (context.Canvas.IsFocused
                              && context.Canvas.IsHovered
                              && !ImGui.IsAnyItemActive())
                          {
                              // Tab create placeholder
                              if (ImGui.IsKeyReleased(ImGuiKey.Tab))
                              {
                                  var focusedObject =
                                      context.Selector.Selection.Count == 1 &&
                                      context.Canvas.IsItemVisible(context.Selector.Selection[0])
                                          ? context.Selector.Selection[0]
                                          : null;

                                  if (focusedObject != null
                                      && context.Layout.Items.TryGetValue(focusedObject.Id, out var focusedItem))
                                  {
                                      if (focusedItem.OutputLines.Length > 0)
                                      {
                                          context.Placeholder.OpenForItem(context, focusedItem, focusedItem.OutputLines[0]);
                                      }
                                  }
                                  else
                                  {
                                      var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                                      context.Placeholder.OpenOnCanvas(context, posOnCanvas);
                                  }

                                  context.StateMachine.SetState(Placeholder, context);
                              }

                              // else if (ImGui.IsKeyReleased(ImGuiKey.Delete) || ImGui.IsKeyReleased(ImGuiKey.Backspace))
                              // {
                              //     Modifications.DeleteSelectedOps(context);
                              // }
                          }

                          if (!context.Canvas.IsHovered)
                              return;

                          // Mouse click
                          var clickedDown = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                          if (!clickedDown)
                              return;

                          // Open children or parent component
                          if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ProjectView.Focused != null)
                          {
                              var clickedBackground = context.ActiveItem == null;
                              if (clickedBackground)
                              {
                                  ProjectView.Focused.TrySetCompositionOpToParent();
                              }
                              else
                              {
                                  var isWindowActive = ImGui.IsWindowFocused() || ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup);
                                  if (isWindowActive && context.ActiveItem.Variant == MagGraphItem.Variants.Operator)
                                  {
                                      Debug.Assert(context.ActiveItem.Instance != null);
                                      // TODO: implement lib edit warning popup
                                      // var blocked = false;
                                      // if (UserSettings.Config.WarnBeforeLibEdit && context.ActiveItem.Instance.Symbol.Namespace.StartsWith("Lib."))
                                      // {
                                      //     if (UserSettings.Config.WarnBeforeLibEdit)
                                      //     {
                                      //         var count = Structure.CollectDependingSymbols(instance.Symbol).Count();
                                      //         LibWarningDialog.DependencyCount = count;
                                      //         LibWarningDialog.HandledInstance = instance;
                                      //         _canvas.LibWarningDialog.ShowNextFrame();
                                      //         blocked = true;
                                      //     }
                                      // }
                                      // if (!blocked)
                                      // {
                                      // Until we align the context switching between graphs, this hack applies the current
                                      // MagGraph scope to the legacy graph, so it's correctly saved for the Symbol in the user settings...
                                      //ProjectView.Focused?.GraphCanvas?.SetTargetScope(context.Canvas.GetTargetScope());

                                      if (context.ActiveItem.Instance.Children.Count > 1)
                                      { 
                                          ProjectView.Focused.TrySetCompositionOpToChild(context.ActiveItem.Instance.SymbolChildId);
                                      }
                                      
                                      //ImGui.CloseCurrentPopup(); // ?? 
                                      //}
                                  }
                              }
                          }

                          if (context.ActiveItem == null)
                          {
                              context.StateMachine.SetState(HoldBackground, context);
                          }
                          else
                          {
                              if (context.ActiveSourceOutputId != Guid.Empty)
                              {
                                  context.StateMachine.SetState(HoldOutput, context);
                                  context.ActiveSourceItem = context.ActiveItem;
                              }
                              else if(context.ActiveTargetInputId != Guid.Empty)
                              {
                                  context.StateMachine.SetState(HoldInput, context);
                                  context.ActiveTargetItem = context.ActiveItem;
                              }
                              else
                              {
                                  context.StateMachine.SetState(HoldItem, context);
                              }
                          }
                      },
              Exit:
              _ => { }
             );

    /// <summary>
    /// Active while long tapping on background for insertion
    /// </summary>
    internal static State HoldBackground
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)
                              || !context.Canvas.IsFocused
                              || ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          const float longTapDuration = 0.3f;
                          var longTapProgress = context.StateMachine.StateTime / longTapDuration;
                          MagItemMovement.UpdateLongPressIndicator(longTapProgress);

                          if (!(longTapProgress > 1))
                              return;

                          // TODO: setting both, state and placeholder, feels awkward.
                          context.StateMachine.SetState(Placeholder, context);
                          var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                          context.Placeholder.OpenOnCanvas(context, posOnCanvas);
                      },
              Exit: _ => { }
             );

    internal static State Placeholder
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (context.Placeholder.PlaceholderItem != null)
                              return;

                          context.Placeholder.Cancel(context);
                          context.StateMachine.SetState(Default, context);
                      },
              Exit: _ => { }
             );

    internal static State HoldItem
        = new(
              Enter: context =>
                     {
                         var item = context.ActiveItem;
                         Debug.Assert(item != null);

                         var selector = context.Selector;

                         var isPartOfSelection = selector.IsSelected(item);
                         if (isPartOfSelection)
                         {
                             context.ItemMovement.SetDraggedItems(selector.Selection);
                         }
                         else
                         {
                             context.ItemMovement.SetDraggedItemIdsToSnappedForItem(item);
                         }
                     },
              Update: context =>
                      {
                          Debug.Assert(context.ActiveItem != null);

                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              MagItemMovement.SelectActiveItem(context);
                              context.ItemMovement.Reset();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(DragItems, context);
                              return;
                          }

                          const float longTapDuration = 0.3f;
                          var longTapProgress = context.StateMachine.StateTime / longTapDuration;
                          MagItemMovement.UpdateLongPressIndicator(longTapProgress);

                          if (!(longTapProgress > 1))
                              return;

                          MagItemMovement.SelectActiveItem(context);
                          context.ItemMovement.SetDraggedItemIds([context.ActiveItem.Id]);
                          context.StateMachine.SetState(HoldItemAfterLongTap, context);
                      },
              Exit: _ => { }
             );

    internal static State HoldItemAfterLongTap
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          Debug.Assert(context.ActiveItem != null);

                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              MagItemMovement.SelectActiveItem(context);
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              context.StateMachine.SetState(DragItems, context);
                          }
                      },
              Exit: _ => { }
             );

    internal static State DragItems
        = new(
              Enter: context =>
                     {
                         context.ItemMovement.PrepareDragInteraction();
                         context.ItemMovement.StartDragOperation(context);
                     },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              context.ItemMovement.CompleteDragOperation(context);

                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          context.ItemMovement.UpdateDragging(context);
                      },
              Exit: context => { context.ItemMovement.StopDragOperation(); }
             );

    /// <summary>
    /// Active while long tapping on background for insertion
    /// </summary>
    internal static State HoldOutput
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          var sourceItem = context.ActiveItem;
                          Debug.Assert(sourceItem != null);
                          Debug.Assert(sourceItem.OutputLines.Length > 0);
                          Debug.Assert(context.ActiveSourceOutputId != Guid.Empty);

                          // Click
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              if (context.TryGetActiveOutputLine(out var outputLine))
                              {
                                  context.Placeholder.OpenForItem(context, sourceItem, outputLine, context.ActiveOutputDirection);
                                  context.StateMachine.SetState(Placeholder, context);
                              }
                              else
                              {
                                  context.StateMachine.SetState(Default, context);
                              }

                              return;
                          }

                          // Start dragging...
                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (!context.TryGetActiveOutputLine(out var outputLine))
                              {
                                  Log.Warning("Output not found?");
                                  context.StateMachine.SetState(Default, context);
                                  return;
                              }

                              //var outputLine = context.GetActiveOutputLine();              
                              var output = outputLine.Output;
                              var posOnCanvas = sourceItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X,
                                                                                     MagGraphItem.GridSize.Y * (1.5f + outputLine.VisibleIndex));

                              var tempConnection = new MagGraphConnection
                                                       {
                                                           Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                           SourcePos = posOnCanvas,
                                                           TargetPos = default,
                                                           SourceItem = sourceItem,
                                                           TargetItem = null,
                                                           SourceOutput = output,
                                                           OutputLineIndex = outputLine.VisibleIndex,
                                                           VisibleOutputIndex = 0,
                                                           ConnectionHash = 0,
                                                           IsTemporary = true,
                                                       };
                              context.TempConnections.Add(tempConnection);
                              context.ActiveSourceItem = sourceItem;
                              context.DraggedPrimaryOutputType = output.ValueType;
                              context.StateMachine.SetState(DragConnectionEnd, context);
                          }
                      },
              Exit: _ => { }
             );

    /// <summary>
    /// Active while long tapping on background for insertion
    /// </summary>
    internal static State HoldInput
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          var sourceItem = context.ActiveItem;
                          Debug.Assert(sourceItem != null);
                          Debug.Assert(sourceItem.OutputLines.Length > 0);
                          Debug.Assert(context.ActiveTargetInputId != Guid.Empty);

                          // Click
                          var released = !ImGui.IsMouseDown(ImGuiMouseButton.Left);
                          if (released)
                          {
                              if (context.TryGetActiveInputLine(out var inputLine))
                              {
                                  //context.Placeholder.OpenForItem(context, sourceItem, inputLine, context.ActiveOutputDirection);
                                  context.Placeholder.OpenForItemInput(context, context.ActiveTargetItem, inputLine.Id);
                                  context.StateMachine.SetState(Placeholder, context);
                              }
                              else
                              {
                                context.StateMachine.SetState(Default, context);
                              }
                              Log.Debug("Click");
                              return;
                          }

                          // // Start dragging...
                          // if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          // {
                          //     if (!context.TryGetActiveOutputLine(out var outputLine))
                          //     {
                          //         Log.Warning("Output not found?");
                          //         context.StateMachine.SetState(Default, context);
                          //         return;
                          //     }
                          //
                          //     //var outputLine = context.GetActiveOutputLine();              
                          //     var output = outputLine.Output;
                          //     var posOnCanvas = sourceItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X,
                          //                                                            MagGraphItem.GridSize.Y * (1.5f + outputLine.VisibleIndex));
                          //
                          //     var tempConnection = new MagGraphConnection
                          //                              {
                          //                                  Style = MagGraphConnection.ConnectionStyles.Unknown,
                          //                                  SourcePos = posOnCanvas,
                          //                                  TargetPos = default,
                          //                                  SourceItem = sourceItem,
                          //                                  TargetItem = null,
                          //                                  SourceOutput = output,
                          //                                  OutputLineIndex = outputLine.VisibleIndex,
                          //                                  VisibleOutputIndex = 0,
                          //                                  ConnectionHash = 0,
                          //                                  IsTemporary = true,
                          //                              };
                          //     context.TempConnections.Add(tempConnection);
                          //     context.ActiveSourceItem = sourceItem;
                          //     context.DraggedPrimaryOutputType = output.ValueType;
                          //     context.StateMachine.SetState(DragConnectionEnd, context);
                          // }
                      },
              Exit: _ => { }
             );    
    
    internal static State DragConnectionEnd
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (ImGui.IsKeyDown(ImGuiKey.Escape))
                          {
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          var posOnCanvas = context.Canvas.InverseTransformPositionFloat(ImGui.GetMousePos());
                          context.PeekAnchorInCanvas = posOnCanvas;

                          var mouseReleased =  context.StateMachine.StateTime > 0 && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                          if (!mouseReleased)
                              return;

                          if (InputSnapper.TryToReconnect(context))
                          {
                              context.Layout.FlagAsChanged();
                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          var hasDisconnections = context.TempConnections.Any(c => c.WasDisconnected);

                          var droppedOnItem = InputPicking.TryInitializeAtPosition(context, posOnCanvas);
                          if (droppedOnItem)
                          {
                              context.StateMachine.SetState(PickInput, context);
                          }
                          else if (hasDisconnections)
                          {
                              // Ripped off input -> Avoid open placeholder
                              //UndoRedoStack.Add(context.MacroCommand);
                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                          }
                          else
                          {
                              // Was dropped on operator or background...
                              context.Placeholder.OpenOnCanvas(context, posOnCanvas, context.DraggedPrimaryOutputType);
                              context.StateMachine.SetState(Placeholder, context);
                          }
                      },
              Exit: _ => { }
             );

    internal static State PickInput
        = new(
              Enter:  InputPicking.Init,
              Update: InputPicking.DrawHiddenInputSelector,
              Exit: InputPicking.Reset
             );

    internal static State PickOutput
        = new(
              Enter:  OutputPicking.Init,
              Update=> {},//OutputPicking.DrawHiddenOutputSelector,
              Exit: OutputPicking.Reset
             );
    
    internal static State HoldingConnectionEnd
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              context.Placeholder.OpenToSplitHoveredConnections(context); // Will change state implicitly
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (context.ConnectionHovering.ConnectionHoversWhenClicked.Count == 0)
                                  return;

                              var connection = context.ConnectionHovering.ConnectionHoversWhenClicked[0].Connection;
                              context.ActiveSourceOutputId = connection.SourceOutput.Id;
                              
                              // Remove existing connection
                              context.StartMacroCommand("Reconnect from input")
                                     .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                                    connection.AsSymbolConnection(),
                                                                                    connection.MultiInputIndex));

                              if (MagItemMovement.DisconnectedInputWouldCollapseLine(connection))
                              {
                                  var collectSnappedItems = MagItemMovement.CollectSnappedItems(connection.TargetItem);
                                  collectSnappedItems.Remove(connection.TargetItem);
                                  MagItemMovement.MoveSnappedItemsVertically(context,
                                                                             collectSnappedItems,
                                                                             connection.TargetItem.PosOnCanvas.Y + MagGraphItem.GridSize.Y * (connection.InputLineIndex + 0.5f),
                                                                             -MagGraphItem.GridSize.Y);
                              }
                              // var deletedLineIndex = 0;
                              // var lines = connection.TargetItem.InputLines;
                              // while ( deletedLineIndex < lines.Length && lines[deletedLineIndex].Id != connection.TargetInput.Id)
                              // {
                              //     deletedLineIndex++;
                              // }                              
                              
                              
                              var tempConnection = new MagGraphConnection
                                                       {
                                                           Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                           SourcePos = connection.SourcePos,
                                                           TargetPos = default,
                                                           SourceItem = connection.SourceItem,
                                                           TargetItem = null,
                                                           SourceOutput = connection.SourceOutput,
                                                           OutputLineIndex = 0,
                                                           VisibleOutputIndex = 0,
                                                           ConnectionHash = 0,
                                                           IsTemporary = true,
                                                           WasDisconnected = true,
                                                       };

                              context.TempConnections.Add(tempConnection);
                              context.ActiveSourceItem = connection.SourceItem;
                              context.DraggedPrimaryOutputType = connection.Type;
                              context.ActiveItem = connection.SourceItem;
                              context.StateMachine.SetState(DragConnectionEnd, context);
                              context.Layout.FlagAsChanged();
                          }
                      },
              Exit: _ => { }
             );

    internal static State HoldingConnectionBeginning
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          var wasClick = !ImGui.IsMouseDown(ImGuiMouseButton.Left);
                          if (wasClick)
                          {
                              context.Placeholder.OpenToSplitHoveredConnections(context); // Will change state implicitly
                              return;
                          }

                          if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                          {
                              if (context.ConnectionHovering.ConnectionHoversWhenClicked.Count == 0)
                                  return;

                              context.StartMacroCommand("Reconnect from output");

                              foreach (var h in context.ConnectionHovering.ConnectionHoversWhenClicked
                                                       .OrderByDescending(h => h.Connection.MultiInputIndex))
                              {
                                  var connection = h.Connection;

                                  context.DisconnectedInputHashes.Add(connection.GetItemInputHash()); // keep input visible until state is complete

                                  // Remove existing connections
                                  context.MacroCommand!
                                         .AddAndExecCommand(new DeleteConnectionCommand(context.CompositionInstance.Symbol,
                                                                                        connection.AsSymbolConnection(),
                                                                                        h.Connection.MultiInputIndex));

                                  //if (connection.MultiInputIndex > 0)
                                  //    continue;

                                  var tempConnection = new MagGraphConnection
                                                           {
                                                               Style = MagGraphConnection.ConnectionStyles.Unknown,
                                                               TargetPos = connection.TargetPos,
                                                               TargetItem = connection.TargetItem,
                                                               InputLineIndex = connection.InputLineIndex,
                                                               MultiInputIndex = connection.MultiInputIndex,
                                                               SourceItem = null,
                                                               SourceOutput = null,
                                                               IsTemporary = true,
                                                               WasDisconnected = true,
                                                           };

                                  // Sadly keeping disconnected multi input slots visible is tricky,
                                  // so, this is only a preparation for a potential later implementation
                                  //Log.Debug("Keep input hash " + connection.GetItemInputHash());
                                  //context.DisconnectedInputsHashes.Add(connection.GetItemInputHash());
                                  context.TempConnections.Add(tempConnection);
                                  context.DraggedPrimaryOutputType = connection.Type;
                              }

                              context.Layout.FlagAsChanged();
                              context.StateMachine.SetState(DragConnectionBeginning, context);
                          }
                      },
              Exit:
              _ => { }
             );

    internal static State DragConnectionBeginning
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                          {
                              if (OutputSnapper.TryToReconnect(context))
                              {
                                  context.Layout.FlagAsChanged();
                              }

                              context.CompleteMacroCommand();
                              context.StateMachine.SetState(Default, context);
                              return;
                          }

                          if (ImGui.IsKeyDown(ImGuiKey.Escape))
                          {
                              context.CancelMacroCommand();
                              context.Layout.FlagAsChanged();
                              context.StateMachine.SetState(Default, context);
                          }
                      },
              Exit: _ => { });
    
    internal static State RenameChild
        = new(
              Enter: _ => { },
              Update: _ => { },
              Exit: _ => { }
             );
    

}