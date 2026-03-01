#nullable enable

using System.Diagnostics.CodeAnalysis;
using T3.Core.Operator;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Windows.SymbolLib;

/// <summary>
/// A nested container that can contain further instances of <see cref="NamespaceTreeNode"/>
/// Used to structure the <see cref="SymbolLibrary"/>.
/// </summary>
internal sealed class NamespaceTreeNode
{
    internal string Name { get; private set; }
    internal List<NamespaceTreeNode> Children { get; } = new();
    internal readonly int Id;
    internal NamespaceTreeNode? Parent { get; }
    internal string Namespace => GetAsString();
    internal SymbolFolderTypes FolderType = SymbolFolderTypes.Undefined;

    internal NamespaceTreeNode(string name, NamespaceTreeNode? parent = null)
    {
        Name = name;
        Parent = parent;
        Id = name.GetHashCode();
    }

    internal string GetAsString()
    {
        var list = new List<string>();
        var t = this;
        while (t.Parent != null)
        {
            list.Insert(0, t.Name);
            t = t.Parent;
        }

        return string.Join(".", list);
    }

    private void Clear()
    {
        Children.Clear();
        Symbols.Clear();
    }

    private static readonly List<string> _rootProjectNames = [
            "Lib.",
            "Types.",
            "Examples.",
            "t3.",
        ]; 
    
    internal void PopulateCompleteTree()
    {
        PopulateCompleteTree(filterAction: null);
    }
    
    // Define an action delegate that takes a Symbol and returns a bool
    internal void PopulateCompleteTree(Predicate<SymbolUi>? filterAction)
    {
        Name = RootNodeId;
        Clear();
        
        var ordered = EditorSymbolPackage.AllSymbolUis
                                         .OrderBy(ui =>
                                                  {
                                                      var ns = ui.Symbol.Namespace ?? string.Empty;

                                                      // Find matching root index
                                                      var index = _rootProjectNames.FindIndex(p => ns.StartsWith(p, StringComparison.Ordinal));
                                                      if (index < 0)
                                                          index = int.MaxValue;

                                                      return (index, ns + ui.Symbol.Name);
                                                  });        

        foreach (var ui in ordered)
        {
            var keep = filterAction == null || filterAction(ui);
            if (!keep)
                continue;
            
            SortInOperator(ui.Symbol);
        }
    }

    internal enum SymbolFolderTypes
    {
        Undefined,
        Root,
        User,
        UserSubNamespace,
        Project,
        ProjectSubNamespace,
    } 

    private void SortInOperator(Symbol symbol)
    {
        if (symbol.Namespace == null)
        {
            return;
        }

        var symbolNamespaceParts = symbol.SymbolPackage.RootNamespace.Split('.');

        var parts = symbol.Namespace.Split('.');

        var currentNode = this;
        var expandingSubTree = false;

        foreach (var part in parts)
        {
            if (part == "")
                continue;

            if (!expandingSubTree)
            {
                if(currentNode.TryFindNodeDataByName(part, out var node))
                {
                    currentNode = node;
                }
                else
                {
                    expandingSubTree = true;
                }
            }

            if (!expandingSubTree)
                continue;

            var newNode = new NamespaceTreeNode(part, currentNode);

            var typeIndex = GetIndexInList(part, symbolNamespaceParts);
            if (typeIndex == -1)
            {
                newNode.FolderType = SymbolFolderTypes.ProjectSubNamespace;
            }
            else if (typeIndex == symbolNamespaceParts.Length - 1)
            {
                newNode.FolderType = SymbolFolderTypes.Project;
            }
            else if (typeIndex == 0)
            {
                newNode.FolderType = SymbolFolderTypes.User;
            }
            else
            {
                newNode.FolderType = SymbolFolderTypes.UserSubNamespace;
            }
            
            currentNode.Children.Add(newNode);
            currentNode = newNode;
        }

        currentNode.Symbols.Add(symbol);
    }

    private static int GetIndexInList(string part, string[] list)
    {
        var partIndex = -1;
        
        foreach (var p in list)
        {
            partIndex++;
            if (p.Equals(part, StringComparison.InvariantCultureIgnoreCase))
            {
                return partIndex;
            }
        }

        return -1;
    }

    private bool TryFindNodeDataByName(string name, [NotNullWhen(true)]out  NamespaceTreeNode? node)
    {
        node=Children.FirstOrDefault(n => n.Name == name);
        return node != null;
    }

    internal readonly List<Symbol> Symbols = [];
    internal const string RootNodeId = "root";
}