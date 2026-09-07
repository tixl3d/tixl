using System.IO;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Exporting;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using MagGraphView = T3.Editor.Gui.MagGraph.Ui.MagGraphView;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class GraphContextMenu
{
    // Stable ids for CustomComponents.DrawMenuItem. Enum casts are compile-time constants, so they
    // survive hot reload (unlike static-field id initializers, which don't re-run and collapse to 0).
    private enum MenuItemIds
    {
        Undo = 1,
        SelectMenu, SelectCustomOps, SelectConnected, SelectConnectedInputs,
        Disabled, Bypassed, Disconnect, ControlSnapshots, Rename, AddComment, AlignLeft, LayoutInputs,
        DisplayMenu, DisplaySmall, DisplayResizable, DisplayExpanded, DisplayBackground, DisplayPin,
        GenerateProxy,
        Copy, Duplicate, Paste, Delete, RenameOutput,
        MoreMenu, CopyKeyframes, CopySymbolName, CopyDependencies, DuplicateConnected, PasteValues,
        OpenFolderMenu, OpenProjectFolder, OpenResourcesFolder,
        AddNode, AddSection, AddInput, AddOutput,
        SymbolDefMenu, RenameSymbol, DuplicateAsNewType, CombineIntoNewType, SetThumbnail,
    }

    internal static void DrawContextMenuContent(GraphUiContext context, ProjectView projectView)
    {
        const CustomComponents.ButtonStates emphasized = CustomComponents.ButtonStates.Emphasized;
        const CustomComponents.ButtonStates muted = CustomComponents.ButtonStates.Default;

        var compositionSymbolUi = context.CompositionInstance.GetSymbolUi();
        var nodeSelection = context.Selector;
        var selectedChildUis = nodeSelection.GetSelectedChildUis().ToList();

        var oneOpSelected = selectedChildUis.Count == 1;
        var someOpsSelected = selectedChildUis.Count > 0;
        var canModify = !compositionSymbolUi.Symbol.SymbolPackage.IsReadOnly;
        var isSaving = T3Ui.IsCurrentlySaving;

        // ----- Undo --------------------------------------------------------
        var nextUndoTitle = UndoRedoStack.CanUndo ? $" ({UndoRedoStack.GetNextUndoTitle()})" : string.Empty;
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.Undo, Icon.None, "Undo" + nextUndoTitle,
                                          UserActions.Undo.ListShortcuts(), isChecked: false, isEnabled: UndoRedoStack.CanUndo,
                                          reserveIconColumn: false, state: muted))
        {
            UndoRedoStack.Undo();
        }

        // ----- Selection state, toggles & per-op actions -------------------
        // Hidden entirely (not greyed) when nothing is selected — these only make sense for a selection.
        if (someOpsSelected)
        {
            CustomComponents.SeparatorLine();

            var selectionLabel = oneOpSelected
                                     ? selectedChildUis[0].SymbolChild.ReadableName
                                     : $"{selectedChildUis.Count} Selected Items";
            CustomComponents.DrawMenuGroupLabel(selectionLabel);

            var allSelectedDisabled = selectedChildUis.TrueForAll(c => c.SymbolChild.IsDisabled);
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.Disabled, Icon.None, "Disabled",
                                              UserActions.ToggleDisabled.ListShortcuts(), isChecked: allSelectedDisabled,
                                              reserveIconColumn: false, state: allSelectedDisabled ? emphasized : muted))
            {
                NodeActions.ToggleDisabledForSelectedElements(nodeSelection);
            }

            var allSelectedBypassed = selectedChildUis.TrueForAll(c => c.SymbolChild.IsBypassed);
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.Bypassed, Icon.None, "Bypassed",
                                              UserActions.ToggleBypassed.ListShortcuts(), isChecked: allSelectedBypassed,
                                              reserveIconColumn: false, state: allSelectedBypassed ? emphasized : muted))
            {
                NodeActions.ToggleBypassedForSelectedElements(nodeSelection);
            }

            var selectedIds = new HashSet<Guid>(selectedChildUis.Select(c => c.Id));
            var canDisconnect = selectedChildUis.Exists(c => context.Layout.Items.TryGetValue(c.Id, out var item)
                                                             && (Array.Exists(item.InputLines, l => l.ConnectionIn != null
                                                                                                    && !selectedIds.Contains(l.ConnectionIn.SourceItem.Id))
                                                                 || Array.Exists(item.OutputLines, l => l.ConnectionsOut.Exists(o => !selectedIds.Contains(o.TargetItem.Id)))));

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.Disconnect, Icon.None, "Disconnect",
                                              UserActions.Disconnect.ListShortcuts(), isChecked: false, isEnabled: canDisconnect,
                                              reserveIconColumn: false, state: muted))
            {
                NodeActions.DisconnectNodes(context.CompositionInstance, nodeSelection.Selection.ToList());
                context.Layout.FlagStructureAsChanged();
            }

            if (canModify)
            {
                var snapshotsEnabled = selectedChildUis.Any(c => c.EnabledForSnapshots);
                if (CustomComponents.DrawMenuItem((int)MenuItemIds.ControlSnapshots, Icon.None, "Control with Snapshots",
                                                  UserActions.ToggleSnapshotControl.ListShortcuts(), isChecked: snapshotsEnabled,
                                                  reserveIconColumn: false, state: snapshotsEnabled ? emphasized : muted))
                {
                    ToggleSnapshotControlForSelection(context, compositionSymbolUi, selectedChildUis);
                }
            }

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.Rename, Icon.None, "Rename",
                                              isChecked: false, isEnabled: oneOpSelected, reserveIconColumn: false, state: muted))
            {
                RenamingOperator.OpenForChildUi(selectedChildUis[0]);
                context.StateMachine.SetState(GraphStates.RenameChild, context);
            }

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.AddComment, Icon.None, "Add Comment...",
                                              UserActions.AddComment.ListShortcuts(), isChecked: false, isEnabled: oneOpSelected,
                                              reserveIconColumn: false, state: muted))
            {
                context.EditCommentDialog.ShowNextFrame();
            }

            var canAlign = context.StateMachine.CurrentState == GraphStates.Default && selectedChildUis.Count > 1;
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.AlignLeft, Icon.None, "Align Select Left",
                                              UserActions.AlignSelectionLeft.ListShortcuts(), isChecked: false, isEnabled: canAlign,
                                              reserveIconColumn: false, state: muted))
            {
                Modifications.AlignSelectionToLeft(context);
            }

            var canLayout = context.StateMachine.CurrentState == GraphStates.Default && selectedChildUis.Count > 0;
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.LayoutInputs, Icon.None, "Layout Inputs",
                                              UserActions.LayoutSelection.ListShortcuts(), isChecked: false, isEnabled: canLayout,
                                              reserveIconColumn: false, state: muted))
            {
                TreeLayouting.LayoutInputsOfSelection(context);
            }

            DrawDisplayAsSubMenu(context, nodeSelection, selectedChildUis, oneOpSelected, someOpsSelected);
        }

        CustomComponents.SeparatorLine();

        // Top-level "Generate proxy" for a selected op whose file-path input points at a video.
        if (oneOpSelected
            && context.CompositionInstance.Children.TryGetChildInstance(selectedChildUis[0].Id, out var proxyOpInstance)
            && TryGetVideoProxySource(proxyOpInstance, out var proxySource))
        {
            var proxyState = ProxyGenerationService.Jobs.TryGetValue(proxySource, out var status)
                                 ? status.State
                                 : (ProxyGenerationService.JobState?)null;
            var (proxyLabel, proxyBusy) = proxyState switch
                                              {
                                                  ProxyGenerationService.JobState.Queued     => ("Proxy Queued...", true),
                                                  ProxyGenerationService.JobState.Generating  => ($"Generating Proxy... {status.Progress * 100:0}%", true),
                                                  ProxyGenerationService.JobState.Done         => ("Regenerate Proxy", false),
                                                  _                                            => ("Generate Proxy Video", false),
                                              };
            // Block generation (but not the busy/queued states) when the target drive is low on space.
            var hasDisk = ProxyGenerationService.HasEnoughFreeDisk(proxySource, out var proxyFreeBytes, out var proxyReqBytes);
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.GenerateProxy, Icon.None, proxyLabel,
                                              isChecked: false, isEnabled: !proxyBusy && hasDisk, reserveIconColumn: false, state: muted))
            {
                ProxyGenerationService.Enqueue(proxySource);
            }

            if (!proxyBusy && !hasDisk)
                CustomComponents.TooltipForLastItem(
                    $"Not enough free disk to generate a proxy ({proxyFreeBytes / 1e9:0.#} GB free, {proxyReqBytes / 1e9:0.#} GB required).");

            CustomComponents.SeparatorLine();
        }

        // ----- Select / clipboard / duplication ----------------------------
        if (CustomComponents.DrawSubMenu((int)MenuItemIds.SelectMenu, "Select"))
        {
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.SelectCustomOps, Icon.None, "Custom Operators",
                                              reserveCheckmarkColumn: false, reserveIconColumn: false))
            {
                var foundAny = false;
                var childUis = selectedChildUis.Count > 0
                                   ? selectedChildUis.ToList()
                                   : context.CompositionInstance.Children.Values.Select(c => c.GetChildUi());

                if (selectedChildUis.Count > 0)
                    nodeSelection.Clear();

                foreach (var item in childUis)
                {
                    if (item == null)
                        continue;

                    if (SymbolAnalysis.TryGetOperatorType(item.SymbolChild.Symbol, out var type)
                        && type != SymbolAnalysis.OperatorClassification.Unknown)
                    {
                        continue;
                    }

                    if (context.CompositionInstance.Children.TryGetChildInstance(item.Id, out var instance))
                    {
                        nodeSelection.AddSelection(item, instance);
                    }

                    foundAny = true;
                }

                if (foundAny)
                {
                    context.ProjectView.FocusViewToSelection();
                }
            }

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.SelectConnected, Icon.None, "Select Connected",
                                              isEnabled: someOpsSelected, reserveCheckmarkColumn: false, reserveIconColumn: false))
            {
                var connectedIds = new HashSet<Guid>();

                var selectedChildren = new List<Symbol.Child>();
                foreach (var child in selectedChildUis)
                {
                    selectedChildren.Add(child.SymbolChild);
                }

                Structure.CollectConnectedChildIds(context.CompositionInstance.Symbol, selectedChildren, connectedIds);

                if (connectedIds.Count > 0)
                {
                    context.Selector.Clear();
                    foreach (var id in connectedIds)
                    {
                        if (context.CompositionInstance.Children.TryGetChildInstance(id, out var instance))
                        {
                            var node = instance.GetChildUi();
                            if (node != null)
                            {
                                context.Selector.AddSelection(node, instance);
                            }
                        }
                    }
                }
            }

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.SelectConnectedInputs, Icon.None, "Select Connected Inputs",
                                              isEnabled: someOpsSelected, reserveCheckmarkColumn: false, reserveIconColumn: false))
            {
                var connectedIds = new HashSet<Guid>();
                foreach (var child in selectedChildUis)
                {
                    Structure.CollectConnectedChildren(child.SymbolChild,
                                                       context.CompositionInstance.Symbol,
                                                       connectedIds);
                }

                if (connectedIds.Count > 0)
                {
                    context.Selector.Clear();
                    foreach (var id in connectedIds)
                    {
                        if (context.CompositionInstance.Children.TryGetChildInstance(id, out var instance))
                        {
                            var node = instance.GetChildUi();
                            if (node != null)
                            {
                                context.Selector.AddSelection(node, instance);
                            }
                        }
                    }
                }
            }

            ImGui.EndMenu();
        }

        // ----- Clipboard / duplication --------------------------------------
        // Copy / Duplicate / Paste stay flat so they're one scan away with their shortcut on display;
        // the rarer variants live under "more...".
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.Copy, Icon.None, "Copy",
                                          UserActions.CopyToClipboard.ListShortcuts(), isChecked: false, isEnabled: someOpsSelected,
                                          reserveIconColumn: false, state: muted))
        {
            NodeActions.CopySelectedNodesToClipboard(nodeSelection, context.CompositionInstance);
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.Duplicate, Icon.None, "Duplicate",
                                          UserActions.Duplicate.ListShortcuts(), isChecked: false, isEnabled: someOpsSelected && !isSaving,
                                          reserveIconColumn: false, state: muted))
        {
            NodeActions.CopySelectedNodesToClipboard(nodeSelection, context.CompositionInstance);
            NodeActions.PasteClipboard(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.Paste, Icon.None, "Paste",
                                          UserActions.PasteFromClipboard.ListShortcuts(), reserveIconColumn: false, state: muted))
        {
            NodeActions.PasteClipboard(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        var selectedInputUis = nodeSelection.GetSelectedNodes<IInputUi>().ToList();
        var selectedOutputUis = nodeSelection.GetSelectedNodes<IOutputUi>().ToList();

        var canDelete = (someOpsSelected || selectedInputUis.Count > 0 || selectedOutputUis.Count > 0) && !isSaving;
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.Delete, Icon.None, "Delete",
                                          "Del", isChecked: false, isEnabled: canDelete, reserveIconColumn: false, state: muted))
        {
            NodeActions.DeleteSelectedElements(nodeSelection, compositionSymbolUi, selectedChildUis, selectedInputUis, selectedOutputUis);
            context.Layout.FlagStructureAsChanged();
        }

        DrawEditMoreSubMenu(context, nodeSelection, selectedChildUis, oneOpSelected, someOpsSelected, isSaving);

        if (canModify && selectedOutputUis.Count == 1
            && CustomComponents.DrawMenuItem((int)MenuItemIds.RenameOutput, Icon.None, "Rename Output...",
                                             reserveIconColumn: false, state: muted))
        {
            context.RenameOutputDialog.ShowNextFrame(compositionSymbolUi.Symbol, selectedOutputUis[0].Id);
        }

        CustomComponents.SeparatorLine();

        // ----- Add ----------------------------------------------------------
        CustomComponents.DrawMenuGroupLabel("Add");

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.AddNode, Icon.None, "Operator",
                                          "TAB", reserveIconColumn: false, state: muted))
        {
            var posOnCanvas = context.View.InverseTransformPositionFloat(CustomComponents.ScreenPosOnOpeningContextMenu);
            context.Placeholder.OpenOnCanvas(context, posOnCanvas);
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.AddSection, Icon.None, "Section",
                                          UserActions.AddSection.ListShortcuts(), reserveIconColumn: false, state: muted))
        {
            var newSection = NodeActions.AddSection(nodeSelection, context.View, context.CompositionInstance, CustomComponents.ScreenPosOnOpeningContextMenu);
            context.ActiveSectionId = newSection.Id;
            context.StateMachine.SetState(GraphStates.RenameSection, context);
        }

        if (canModify)
        {
            if (CustomComponents.DrawMenuItem((int)MenuItemIds.AddInput, Icon.None, "Input Parameter...",
                                              reserveIconColumn: false, state: muted))
            {
                var posOnCanvas = context.View.InverseTransformPositionFloat(CustomComponents.ScreenPosOnOpeningContextMenu);
                context.AddInputDialog.ShowNextFrame(posOnCanvas);
            }

            if (CustomComponents.DrawMenuItem((int)MenuItemIds.AddOutput, Icon.None, "Output...",
                                              reserveIconColumn: false, state: muted))
            {
                var posOnCanvas = context.View.InverseTransformPositionFloat(CustomComponents.ScreenPosOnOpeningContextMenu);
                context.AddOutputDialog.ShowNextFrame(posOnCanvas);
            }
        }

        // ----- Symbol definition --------------------------------------------
        if (projectView.GraphView is MagGraphView)
        {
            CustomComponents.SeparatorLine();
            DrawSymbolDefinitionSubMenu(context, projectView, selectedChildUis, oneOpSelected, someOpsSelected, isSaving);
        }

        // The scroll-canvas popup uses a tighter window padding than the app menu, so the last row sits
        // flush against the bottom edge; nudge a little breathing room in.
        FormInputs.AddVerticalSpace(2);
    }

    private static void DrawDisplayAsSubMenu(GraphUiContext context, NodeSelection nodeSelection, List<SymbolUi.Child> selectedChildUis,
                                             bool oneOpSelected, bool someOpsSelected)
    {
        const CustomComponents.ButtonStates emphasized = CustomComponents.ButtonStates.Emphasized;
        const CustomComponents.ButtonStates muted = CustomComponents.ButtonStates.Default;

        if (!CustomComponents.DrawSubMenu((int)MenuItemIds.DisplayMenu, "Display"))
            return;

        var isSmall = selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Default);
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DisplaySmall, Icon.None, "Small",
                                          isChecked: isSmall, isEnabled: someOpsSelected, reserveIconColumn: false,
                                          state: isSmall ? emphasized : muted))
        {
            foreach (var childUi in selectedChildUis)
            {
                childUi.Style = SymbolUi.Child.Styles.Default;
            }
        }

        var isResizable = selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Resizable);
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DisplayResizable, Icon.None, "Resizable",
                                          isChecked: isResizable, isEnabled: someOpsSelected, reserveIconColumn: false,
                                          state: isResizable ? emphasized : muted))
        {
            foreach (var childUi in selectedChildUis)
            {
                childUi.Style = SymbolUi.Child.Styles.Resizable;
            }
        }

        var isExpanded = selectedChildUis.Any(child => child.Style == SymbolUi.Child.Styles.Expanded);
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DisplayExpanded, Icon.None, "Expanded",
                                          isChecked: isExpanded, isEnabled: someOpsSelected, reserveIconColumn: false,
                                          state: isExpanded ? emphasized : muted))
        {
            foreach (var childUi in selectedChildUis)
            {
                childUi.Style = SymbolUi.Child.Styles.Expanded;
            }
        }

        CustomComponents.SeparatorLine();

        var isImage = oneOpSelected
                      && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions.Count > 0
                      && selectedChildUis[0].SymbolChild.Symbol.OutputDefinitions[0].ValueType == typeof(Texture2D);
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DisplayBackground, Icon.None, "Set Image as Graph Background",
                                          UserActions.DisplayImageAsBackground.ListShortcuts(), isChecked: false, isEnabled: isImage,
                                          reserveIconColumn: false, state: muted))
        {
            var instance = context.CompositionInstance.Children[selectedChildUis[0].Id];
            ProjectView.Focused.SetBackgroundOutput(instance);
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DisplayPin, Icon.None, "Pin to Output",
                                          isChecked: false, isEnabled: oneOpSelected, reserveIconColumn: false, state: muted))
        {
            if (ProjectView.Focused != null)
                NodeActions.PinSelectedToOutputWindow(ProjectView.Focused, nodeSelection, context.CompositionInstance);
        }

        ImGui.EndMenu();
    }

    private static void DrawEditMoreSubMenu(GraphUiContext context, NodeSelection nodeSelection, List<SymbolUi.Child> selectedChildUis,
                                            bool oneOpSelected, bool someOpsSelected, bool isSaving)
    {
        const CustomComponents.ButtonStates muted = CustomComponents.ButtonStates.Default;

        if (!CustomComponents.DrawSubMenu((int)MenuItemIds.MoreMenu, "More"))
            return;

        // Copy variants. Keyframes and Dependencies are placeholders until the backing actions exist.
        CustomComponents.DrawMenuItem((int)MenuItemIds.CopyKeyframes, Icon.None, "Copy Keyframes",
                                      isChecked: false, isEnabled: false, reserveCheckmarkColumn: false, reserveIconColumn: false);
        CustomComponents.TooltipForLastItem("Not implemented yet");

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.CopySymbolName, Icon.None, "Copy Symbol Name",
                                          isChecked: false, isEnabled: oneOpSelected, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            ImGui.SetClipboardText("[" + selectedChildUis[0].SymbolChild.ReadableName + "]");
        }

        CustomComponents.DrawMenuItem((int)MenuItemIds.CopyDependencies, Icon.None, "Copy Dependencies",
                                      isChecked: false, isEnabled: false, reserveCheckmarkColumn: false, reserveIconColumn: false);
        CustomComponents.TooltipForLastItem("Not implemented yet");

        CustomComponents.SeparatorLine();

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DuplicateConnected, Icon.None, "Duplicate Connected",
                                          UserActions.DuplicateWithConnections.ListShortcuts(), isChecked: false,
                                          isEnabled: someOpsSelected && !isSaving, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            Modifications.DuplicateWithConnections(context);
            context.Layout.FlagStructureAsChanged();
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.PasteValues, Icon.None, "Paste Values",
                                          UserActions.PasteValues.ListShortcuts(), reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            NodeActions.PasteValues(nodeSelection, context.View, context.CompositionInstance);
            context.Layout.FlagStructureAsChanged();
        }

        var symbolPackage = context.CompositionInstance.GetSymbolUi().Symbol.SymbolPackage;
        if (!symbolPackage.IsReadOnly)
        {
            CustomComponents.SeparatorLine();
            if (CustomComponents.DrawSubMenu((int)MenuItemIds.OpenFolderMenu, "Open Folder", reserveCheckmarkColumn: false))
            {
                if (CustomComponents.DrawMenuItem((int)MenuItemIds.OpenProjectFolder, Icon.None, "Project",
                                                  reserveCheckmarkColumn: false, reserveIconColumn: false))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.Folder);
                }

                if (CustomComponents.DrawMenuItem((int)MenuItemIds.OpenResourcesFolder, Icon.None, "Resources",
                                                  reserveCheckmarkColumn: false, reserveIconColumn: false))
                {
                    CoreUi.Instance.OpenWithDefaultApplication(symbolPackage.AssetsFolder);
                }

                ImGui.EndMenu();
            }
        }

        ImGui.EndMenu();
    }

    private static void DrawSymbolDefinitionSubMenu(GraphUiContext context, ProjectView projectView, List<SymbolUi.Child> selectedChildUis,
                                                    bool oneOpSelected, bool someOpsSelected, bool isSaving)
    {
        const CustomComponents.ButtonStates muted = CustomComponents.ButtonStates.Default;

        if (!CustomComponents.DrawSubMenu((int)MenuItemIds.SymbolDefMenu, "Symbol Definition", isEnabled: !isSaving))
            return;

        var canRename = oneOpSelected && !selectedChildUis[0].SymbolChild.Symbol.SymbolPackage.IsReadOnly;
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.RenameSymbol, Icon.None, "Rename Symbol...",
                                          isChecked: false, isEnabled: canRename, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            context.RenameSymbolDialog.ShowNextFrame();
            context.SymbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name;
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.DuplicateAsNewType, Icon.None, "Duplicate as New Type...",
                                          isChecked: false, isEnabled: oneOpSelected, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            context.SymbolNameForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Name ?? string.Empty;
            context.NameSpaceForDialogEdits = selectedChildUis[0].SymbolChild.Symbol.Namespace ?? string.Empty;
            context.SymbolDescriptionForDialog = selectedChildUis[0].SymbolChild.Symbol.GetSymbolUi().Description;
            context.DuplicateSymbolDialog.ShowNextFrame();
        }

        if (CustomComponents.DrawMenuItem((int)MenuItemIds.CombineIntoNewType, Icon.None, "Combine into New Type...",
                                          isChecked: false, isEnabled: someOpsSelected, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            context.NameSpaceForDialogEdits = projectView.CompositionInstance.Symbol.Namespace ?? string.Empty;
            context.SymbolDescriptionForDialog = "";
            context.CombineToSymbolDialog.ShowNextFrame();
        }

        CustomComponents.SeparatorLine();

        var canSetThumbnail = oneOpSelected && !selectedChildUis[0].SymbolChild.Symbol.SymbolPackage.IsReadOnly
                              && RenderProcess.MainOutputTexture != null;
        if (CustomComponents.DrawMenuItem((int)MenuItemIds.SetThumbnail, Icon.None, "Set Thumbnail",
                                          isChecked: false, isEnabled: canSetThumbnail, reserveCheckmarkColumn: false, reserveIconColumn: false,
                                          state: muted))
        {
            ThumbnailManager.SaveThumbnail(selectedChildUis[0].SymbolChild.Symbol.Id,
                                           selectedChildUis[0].SymbolChild.Symbol.SymbolPackage,
                                           RenderProcess.MainOutputTexture,
                                           ThumbnailManager.Categories.PackageMeta);
        }

        ImGui.EndMenu();
    }

    private static void ToggleSnapshotControlForSelection(GraphUiContext context, SymbolUi compositionSymbolUi, List<SymbolUi.Child> selectedChildUis)
    {
        var disableBecauseAllEnabled = selectedChildUis.TrueForAll(c => c.EnabledForSnapshots);

        var macro = new MacroCommand("Toggle snapshot enabled");
        macro.AddAndExecCommand(new ChangeSnapshotEnabledCommand(compositionSymbolUi.Symbol.Id,
                                                                 selectedChildUis,
                                                                 newEnabled: !disableBecauseAllEnabled));

        var allSnapshots = VariationHandling.ActivePoolForSnapshots?.AllVariations;
        if (allSnapshots != null && allSnapshots.Count > 0)
        {
            if (disableBecauseAllEnabled)
            {
                VariationHandling.RemoveInstancesFromVariations(selectedChildUis.Select(ui => ui.Id), allSnapshots, collectInto: macro);
            }
            else
            {
                var selectedInstances = selectedChildUis
                                       .Select(ui => context.CompositionInstance.Children[ui.Id])
                                       .ToList();
                foreach (var snapshot in allSnapshots)
                {
                    VariationHandling.ActivePoolForSnapshots.UpdateVariationPropertiesForInstances(snapshot, selectedInstances, collectInto: macro);
                }
            }
        }

        UndoRedoStack.Add(macro);
    }

    // Resolves a selected op's file-path input to an absolute video path, for the "Generate proxy" action.
    private static bool TryGetVideoProxySource(Instance instance, out string absolutePath)
    {
        absolutePath = string.Empty;
        if (!SymbolAnalysis.TryGetFileInputFromInstance(instance, out var fileInput, out _))
            return false;

        var address = fileInput.TypedInputValue.Value;
        if (string.IsNullOrEmpty(address) || !IsVideoFile(address))
            return false;

        return AssetRegistry.TryResolveAddress(address, instance as IResourceConsumer, out absolutePath, out _)
               && File.Exists(absolutePath);
    }

    private static bool IsVideoFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".m4v" or ".mpg" or ".mpeg";
}