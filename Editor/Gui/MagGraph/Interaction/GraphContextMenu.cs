using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.SystemUi;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Exporting;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class GraphContextMenu
{
    internal static void DrawContextMenuContent(GraphUiContext context, ProjectView projectView)
    {
        var clickPosition = ImGui.GetMousePosOnOpeningCurrentPopup();
        var compositionSymbolUi = context.CompositionInstance.GetSymbolUi();

        var nodeSelection = context.Selector;

        var selectedChildUis = nodeSelection.GetSelectedChildUis().ToList();
        var nextUndoTitle = UndoRedoStack.CanUndo ? $" ({UndoRedoStack.GetNextUndoTitle()})" : string.Empty;
        if (ImGui.MenuItem("Undo" + nextUndoTitle,
                           shortcut: UserActions.Undo.ListShortcuts(),
                           selected: false,
                           enabled: UndoRedoStack.CanUndo))
        {
            UndoRedoStack.Undo();
        }

        ImGui.Separator();

        // ------ for selection -----------------------
        var oneOpSelected = selectedChildUis.Count == 1;
        var someOpsSelected = selectedChildUis.Count > 0;
        var snapShotsEnabledFromSomeOps
            = selectedChildUis
               .Any(selectedChildUi => selectedChildUi.EnabledForSnapshots);

        var label = oneOpSelected
                        ? $"{selectedChildUis[0].SymbolChild.ReadableName}..."
                        : $"{selectedChildUis.Count} selected items...";

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Gray.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var allSelectedDisabled = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.SymbolChild.IsDisabled);
        if (ImGui.MenuItem("Disable",
                           UserActions.ToggleDisabled.ListShortcuts(),
                           selected: allSelectedDisabled,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleDisabledForSelectedElements(nodeSelection);
        }

        var allSelectedBypassed = selectedChildUis.TrueForAll(selectedChildUi => selectedChildUi.SymbolChild.IsBypassed);
        if (ImGui.MenuItem("Bypassed",
                           UserActions.ToggleBypassed.ListShortcuts(),
                           selected: allSelectedBypassed,
                           enabled: selectedChildUis.Count > 0))
        {
            NodeActions.ToggleBypassedForSelectedElements(nodeSelection);
        }

        if (ImGui.MenuItem("Rename", oneOpSelected))
        {
            RenamingOperator.OpenForChildUi(selectedChildUis[0]);
            context.StateMachine.SetState(GraphStates.RenameChild, context);
        }

        if (ImGui.MenuItem("Add Comment",
                           UserActions.AddComment.ListShortcuts(),
                           selected: false,
                           enabled: oneOpSelected))
        {
            context.EditCommentDialog.ShowNextFrame();
        }
        
        if (ImGui.MenuItem("Align select left",
                           UserActions.AlignSelectionLeft.ListShortcuts(),
                           selected: false,
                           enabled: context.StateMachine.CurrentState == GraphStates.Default && selectedChildUis.Count > 1))
        {
            Modifications.AlignSelectionToLeft(context);
        }
        

        // if (ImGui.MenuItem("Arrange sub graph",
        //                    KeyboardBinding.ListKeyboardShortcuts(UserActions.LayoutSelection, false),
        //                    selected: false,
        //                    enabled: someOpsSelected))
        // {
        //     _nodeGraphLayouting.ArrangeOps(compositionOp);
        // }

        var canModify = !compositionSymbolUi.Symbol.SymbolPackage.IsReadOnly;
        if (canModify)
        {
            if (ImGui.MenuItem("Enable for snapshots",
                               UserActions.ToggleSnapshotControl.ListShortcuts(),
                               selected: snapShotsEnabledFromSomeOps,
                               enabled: someOpsSelected))
            {            
                // Disable if already enabled for all
                var disableBecauseAllEnabled
                    = selectedChildUis
                       .TrueForAll(c2 => c2.EnabledForSnapshots);

                foreach (var c in selectedChildUis)
                {
                    c.EnabledForSnapshots = !disableBecauseAllEnabled;
                }

                // Add to add snapshots
                var allSnapshots = VariationHandling.ActivePoolForSnapshots?.AllVariations;
                if (allSnapshots != null && allSnapshots.Count > 0)
                {
                    if (disableBecauseAllEnabled)
                    {
                        VariationHandling.RemoveInstancesFromVariations(selectedChildUis.Select(ui => ui.Id), allSnapshots);
                    }
                    // Remove from snapshots
                    else
                    {
                        var selectedInstances = selectedChildUis
                                               .Select(ui => context.CompositionInstance.Children[ui.Id])
                                               .ToList();
                        foreach (var snapshot in allSnapshots)
                        {
                            VariationHandling.ActivePoolForSnapshots.UpdateVariationPropertiesForInstances(snapshot, selectedInstances);
                        }
                    }
                }

                compositionSymbolUi.FlagAsModified();
            }
        }

        if (ImGui.BeginMenu("Display as..."))
        {
            if (ImGui.MenuItem("Small", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Default),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Default;
                }
            }

            if (ImGui.MenuItem("Resizable", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Resizable;
                }
            }

            if (ImGui.MenuItem("Expanded", "",
                               selected: selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable),
                               enabled: someOpsSelected))
            {
                foreach (var childUi in selectedChildUis)
                {
                    childUi.Style = SymbolUi.Child.Styles.Expanded;
                }
            }

            ImGui.Separator();

            // TODO: Implement
            var isImage = oneOpSelected
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions.Count > 0
                          && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions[0].ValueType == typeof(Texture2D);
            if (ImGui.MenuItem("Set image as graph background",
                               UserActions.DisplayImageAsBackground.ListShortcuts(),
                               selected: false,
                               enabled: isImage))
            {
                var instance = context.CompositionInstance.Children[selectedChildUis[0].Id];
                ProjectView.Focused.SetBackgroundOutput(instance);

            }

            // TODO: Implement
            if (ImGui.MenuItem("Pin to output", oneOpSelected))
            {
                if (ProjectView.Focused != null)
                    NodeActions.PinSelectedToOutputWindow(ProjectView.Focused, nodeSelection, context.CompositionInstance);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Copy",
                           UserActions.CopyToClipboard.ListShortcuts(),
                           selected: false,
                           enabled: someOpsSelected))
        {
            NodeActions.CopySelectedNodesToClipboard(nodeSelection, context.CompositionInstance);
        }

        if (ImGui.MenuItem("Paste", UserActions.PasteFromClipboard.ListShortcuts()))
        {
            NodeActions.PasteClipboard(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }
        
        if (ImGui.MenuItem("Paste Values", UserActions.PasteValues.ListShortcuts()))
        {
            NodeActions.PasteValues(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        var selectedInputUis = nodeSelection.GetSelectedNodes<IInputUi>().ToList();
        var selectedOutputUis = nodeSelection.GetSelectedNodes<IOutputUi>().ToList();

        var isSaving = T3Ui.IsCurrentlySaving;

        if (ImGui.MenuItem("Delete",
                           shortcut: "Del", // dynamic assigned shortcut is too long
                           selected: false,
                           enabled: (someOpsSelected || selectedInputUis.Count > 0 || selectedOutputUis.Count > 0) && !isSaving))
        {
            NodeActions.DeleteSelectedElements(nodeSelection, compositionSymbolUi, selectedChildUis, selectedInputUis, selectedOutputUis);
            context.Layout.FlagStructureAsChanged();
        }

        if (ImGui.MenuItem("Duplicate",
                           UserActions.Duplicate.ListShortcuts(),
                           selected: false,
                           enabled: selectedChildUis.Count > 0 && !isSaving))
        {
            NodeActions.CopySelectedNodesToClipboard(nodeSelection, context.CompositionInstance);
            NodeActions.PasteClipboard(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        ImGui.Separator();

        // if (ImGui.MenuItem("Change Symbol", someOpsSelected && !isSaving))
        // {
        //     // var startingSearchString = selectedChildUis[0].SymbolChild.Symbol.Name;
        //     // var position = selectedChildUis.Count == 1 ? selectedChildUis[0].PosOnCanvas : InverseTransformPositionFloat(ImGui.GetMousePos());
        //     // _window.SymbolBrowser.OpenAt(position, null, null, false, startingSearchString,
        //     //                              symbol => { ChangeSymbol.ChangeOperatorSymbol(nodeSelection, context.CompositionOp, selectedChildUis, symbol); });
        // }

        if (projectView.GraphView is MagGraphView canvas)
        {
            if (ImGui.BeginMenu("Symbol definition...", !isSaving))
            {
                if (ImGui.MenuItem("Rename Symbol", oneOpSelected && !selectedChildUis[0].SymbolChild.Symbol.SymbolPackage.IsReadOnly))
                {
                    context.RenameSymbolDialog.ShowNextFrame();
                    context.SymbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name;
                }
                //NodeOperations.RenameSymbol(selectedChildUis[0].SymbolChild.Symbol, "NewName");


                if (ImGui.MenuItem("Duplicate as new type...", oneOpSelected))
                {
                    context.SymbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name ?? string.Empty;
                    context.NameSpaceForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Namespace ?? string.Empty;
                    context.SymbolDescriptionForDialog = "";
                    context.DuplicateSymbolDialog.ShowNextFrame();
                }

                if (ImGui.MenuItem("Combine into new type...", someOpsSelected))
                {
                    context.NameSpaceForDialogEdits = projectView.CompositionInstance.Symbol.Namespace ?? string.Empty;
                    context.SymbolDescriptionForDialog = "";
                    context.CombineToSymbolDialog.ShowNextFrame();
                }

                ImGui.EndMenu();
            }
        }

        var symbolPackage = compositionSymbolUi.Symbol.SymbolPackage;
        if (!symbolPackage.IsReadOnly)
        {
            if (ImGui.BeginMenu("Open folder..."))
            {
                if (ImGui.MenuItem("Project"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.Folder);
                }

                if (ImGui.MenuItem("Resources"))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.ResourcesFolder);
                }

                ImGui.EndMenu();
            }
        }

        if (ImGui.BeginMenu("Add..."))
        {
            if (ImGui.MenuItem("Add Node...", "TAB", false, true))
            {
                var posOnCanvas = context.View.InverseTransformPositionFloat(ImGui.GetMousePos());
                context.Placeholder.OpenOnCanvas(context, posOnCanvas);
            }
        
            if (canModify)
            {
                if (ImGui.MenuItem("Add input parameter..."))
                {
                    context.AddInputDialog.ShowNextFrame();
                }
        
                if (ImGui.MenuItem("Add output..."))
                {
                    context.AddOutputDialog.ShowNextFrame();
                }
            }

            if (ImGui.MenuItem("Add Annotation",
                               shortcut: UserActions.AddAnnotation.ListShortcuts(),
                               selected: false,
                               enabled: true))
            {
                var newAnnotation = NodeActions.AddAnnotation(nodeSelection, context.View, context.CompositionInstance);
                context.ActiveAnnotationId = newAnnotation.Id;
                context.StateMachine.SetState(GraphStates.RenameAnnotation, context);
            }
        
            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Export as Executable", oneOpSelected))
        {
            switch (PlayerExporter.TryExportInstance(context.CompositionInstance, selectedChildUis.Single(), out var reason, out var exportDir))
            {
                case false:
                    Log.Error(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Failed to export {label}");
                    break;
                default:
                    Log.Info(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, $"Exported {label} successfully!");
                    // open export directory in native file explorer
                    CoreUi.Instance.OpenWithDefaultApplication(exportDir);
                    break;
            }
        }

        // TODO: Clarify if required
        // if (oneOpSelected)
        // {
        //     var symbol = selectedChildUis.Single().SymbolChild.Symbol;
        //     CustomComponents.DrawSymbolCodeContextMenuItem(symbol);
        //     var childUi = selectedChildUis.Single();
        //
        //     // get instance that is currently selected
        //     var instance = context.CompositionOp.Children[childUi.Id];
        //
        //     if (NodeActions.TryGetShaderPath(instance, out var filePath, out var owner))
        //     {
        //         var shaderIsReadOnly = owner.IsReadOnly;
        //
        //         if (ImGui.MenuItem("Open in Shader Editor", true))
        //         {
        //             if (shaderIsReadOnly)
        //             {
        //                 CopyToTempShaderPath(filePath, out filePath);
        //                 BlockingWindow.Instance.ShowMessageBox("Warning - viewing a read-only shader. Modifications will not be saved.\n" +
        //                                                        "Following #include directives outside of the temp folder may lead you to read-only files, " +
        //                                                        "and editing those can break operators.\n\nWith great power...", "Warning");
        //             }
        //
        //             EditorUi.Instance.OpenWithDefaultApplication(filePath);
        //         }
        //     }
        // }
        //ImGui.EndMenu();
    }
}