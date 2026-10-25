#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.SymbolLib;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// The graph actions of the app menu's Edit menu. They mirror the graph's context menu but act at the center
/// of the graph view, since the pointer is over the menu.
/// </summary>
internal static class GraphEditMenu
{
    internal static void Draw(GraphUiContext context)
    {
        var composition = context.CompositionInstance;
        var compositionUi = composition.GetSymbolUi();
        var nodeSelection = context.Selector;
        var isSaving = T3Ui.IsCurrentlySaving;
        var isIdle = context.StateMachine.CurrentState == GraphStates.Default;
        var someOpsSelected = nodeSelection.GetSelectedChildUis().Any();
        var canModify = !compositionUi.Symbol.SymbolPackage.IsReadOnly;
        var viewCenter = context.View.WindowPos + context.View.WindowSize * 0.5f;

        CustomComponents.SeparatorLine();

        if (AppMenuBar.MenuItem("Copy", UserActions.CopyToClipboard.ListShortcuts(), isEnabled: someOpsSelected))
        {
            NodeActions.CopySelectedNodesToClipboard(nodeSelection, composition);
        }

        if (AppMenuBar.MenuItem("Duplicate", UserActions.Duplicate.ListShortcuts(), isEnabled: someOpsSelected && !isSaving))
        {
            if (NodeActions.CopySelectedNodesToClipboard(nodeSelection, composition))
            {
                NodeActions.PasteClipboard(nodeSelection, context.View, composition, viewCenter);
                context.Layout.FlagStructureAsChanged();
            }
        }

        if (AppMenuBar.MenuItem("Paste", UserActions.PasteFromClipboard.ListShortcuts(), isEnabled: !isSaving))
        {
            NodeActions.PasteClipboard(nodeSelection, context.View, composition, viewCenter);
            context.Layout.FlagStructureAsChanged();
        }

        if (AppMenuBar.MenuItem("Delete", UserActions.DeleteSelection.ListShortcuts(),
                                isEnabled: nodeSelection.Selection.Count > 0 && isIdle && !isSaving))
        {
            Modifications.DeleteSelection(context);
            context.Layout.FlagStructureAsChanged();
        }

        if (AppMenuBar.MenuItem("Delete and Disconnect", UserActions.DeleteSelectionAndConnections.ListShortcuts(),
                                isEnabled: nodeSelection.Selection.Count > 0 && isIdle && !isSaving))
        {
            Modifications.DeleteSelection(context, keepConnections: false);
            context.Layout.FlagStructureAsChanged();
        }

        CustomComponents.SeparatorLine();
        CustomComponents.DrawMenuGroupLabel("Add");

        if (AppMenuBar.BeginSubMenu("Operator", isEnabled: isIdle && !isSaving))
        {
            // Built when the menu opens, so it shows operators that were added or renamed since.
            if (ImGui.IsWindowAppearing())
                _operatorTree.PopulateCompleteTree();

            DrawNamespaceMenu(context, _operatorTree, viewCenter);
            ImGui.EndMenu();
        }

        if (AppMenuBar.MenuItem("Section", UserActions.AddSection.ListShortcuts(), isEnabled: !isSaving))
        {
            NodeActions.AddSection(nodeSelection, context.View, composition, viewCenter);
            context.Layout.FlagStructureAsChanged();
        }

        if (canModify)
        {
            var centerOnCanvas = context.View.InverseTransformPositionFloat(viewCenter);
            if (AppMenuBar.MenuItem("Input Parameter...", isEnabled: !isSaving))
                context.AddInputDialog.ShowNextFrame(centerOnCanvas);

            if (AppMenuBar.MenuItem("Output...", isEnabled: !isSaving))
                context.AddOutputDialog.ShowNextFrame(centerOnCanvas);
        }
    }

    /** Namespaces become nested menus. Those starting with "_" hold internal helpers and are left out. */
    private static void DrawNamespaceMenu(GraphUiContext context, NamespaceTreeNode node, Vector2 viewCenter)
    {
        foreach (var child in node.Children)
        {
            if (child.Name.StartsWith('_'))
                continue;

            if (!AppMenuBar.BeginSubMenu(child.Name))
                continue;

            DrawNamespaceMenu(context, child, viewCenter);
            ImGui.EndMenu();
        }

        foreach (var symbol in node.Symbols)
        {
            if (symbol.Name.StartsWith('_'))
                continue;

            if (AppMenuBar.MenuItem(symbol.Name))
                InsertOperator(context, symbol, viewCenter);
        }
    }

    private static void InsertOperator(GraphUiContext context, Symbol symbol, Vector2 viewCenter)
    {
        var composition = context.CompositionInstance;
        var posOnCanvas = context.View.InverseTransformPositionFloat(viewCenter);
        var childUi = GraphOperations.AddSymbolChild(symbol, composition.GetSymbolUi(), posOnCanvas);
        if (composition.Children.TryGetChildInstance(childUi.Id, out var instance))
            context.Selector.SetSelection(childUi, instance);

        context.Layout.FlagStructureAsChanged();
    }

    private static readonly NamespaceTreeNode _operatorTree = new(NamespaceTreeNode.RootNodeId);
}
