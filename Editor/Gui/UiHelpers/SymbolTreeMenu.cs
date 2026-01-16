using ImGuiNET;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.SymbolLib;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Shows a tree of all defined symbols sorted by namespace.
/// This is used by <see cref="SymbolLibrary"/> and <see cref="AppMenuBar"/>
/// </summary>
internal static class SymbolTreeMenu
{
    internal static void Draw()
    {
        _treeNode.PopulateCompleteTree();
        DrawNodesRecursively(_treeNode);
    }

    private static void DrawNodesRecursively(NamespaceTreeNode subtree)
    {
        if (subtree.Name == NamespaceTreeNode.RootNodeId)
        {
            DrawContent(subtree);
        }
        else
        {
            ImGui.PushID(subtree.Name);
            if (ImGui.BeginMenu(subtree.Name))
            {
                DrawContent(subtree);

                ImGui.EndMenu();
            }

            ImGui.PopID();
        }
    }

    private static void DrawContent(NamespaceTreeNode subtree)
    {
        foreach (var subspace in subtree.Children)
        {
            DrawNodesRecursively(subspace);
        }

        foreach (var symbol in subtree.Symbols)
        {
            SymbolLibrary.DrawSymbolItem(symbol);
        }
    }

    private static readonly NamespaceTreeNode _treeNode = new(NamespaceTreeNode.RootNodeId);
}