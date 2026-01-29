#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text;
using T3.Core.Operator;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Windows.SymbolLib;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// A nested container that can contain further instances of <see cref="AssetFolder"/>
/// Used to structure the <see cref="SymbolLibrary"/>.
/// </summary>
internal sealed class AssetFolder
{
    internal string Name { get; private set; }
    internal List<AssetFolder> SubFolders { get; } = [];
    private AssetFolder? Parent { get; }
    internal int MatchingAssetCount;
    
    public readonly int HashCode;

    /// <summary>
    /// This could later be used for UI to distinguish projects from folders 
    /// </summary>
    internal FolderTypes FolderType;

    internal enum FolderTypes
    {
        ProjectNameSpace,
        Project,
        Directory
    }

    internal readonly string AbsolutePath;
    internal readonly string Address;
    

    internal AssetFolder(string name, Instance? selectedInstance, AssetFolder? parent = null, FolderTypes type = FolderTypes.Directory)
    {
        Name = name;
        Parent = parent;
        FolderType = type;

        if (name == RootNodeId)
        {
            AbsolutePath = string.Empty;
            Address = string.Empty;
            return;
        }

        Address = GetAliasPath();
        if (!AssetRegistry.TryResolveAddress(Address, selectedInstance, out AbsolutePath, out _, isFolder: true))
        {
            Log.Warning($"Can't resolve folder path '{Address}'? ");
        }
        
        HashCode = Address.GetHashCode();
    }
    
    internal static void PopulateCompleteTree(AssetLibState state, Predicate<Asset>? filterAction)
    {
        if (state.Composition == null)
            return;

        state.RootFolder.Name = RootNodeId;
        state.RootFolder.Clear();

        foreach (var file in state.AllAssets)
        {
            var keep = filterAction == null || filterAction(file);
            if (!keep)
                continue;

            state.RootFolder.SortInAssets(file, state.Composition);
        }
        
        state.RootFolder.UpdateMatchingAssetCounts(state.CompatibleExtensionIds, state.SearchString);
    }

    internal int UpdateMatchingAssetCounts(List<int> compatibleExtensionIds, string searchString)
    {
        var count = 0;

        // No filter: count everything
        if (compatibleExtensionIds.Count == 0 && string.IsNullOrEmpty(searchString))
        {
            count += FolderAssets.Count;
        }
        else
        {
            foreach (var asset in FolderAssets)
            {
                if (asset.IsDirectory)
                    continue;
                
                if(!string.IsNullOrEmpty(searchString) ||
                    compatibleExtensionIds.Contains(asset.ExtensionId) )
                {
                    count++;
                }
            }
        }

        // Recursively aggregate counts from subfolders
        foreach (var subFolder in SubFolders)
        {
            count += subFolder.UpdateMatchingAssetCounts(compatibleExtensionIds, searchString);
        }

        MatchingAssetCount = count;
        return count;
    }
    
    /// <summary>
    /// Build up folder structure by sorting in one asset at a time
    /// creating required sub folders on the way.
    /// </summary>
    private void SortInAssets(Asset asset, Instance composition)
    {
        var currentFolder = this;
        foreach (var pathPart in asset.PathParts) // Using core pre-calculated parts
        {
            if (currentFolder.TryGetSubFolder(pathPart, out var folder))
                currentFolder = folder;
            else
            {
                var newFolder = new AssetFolder(pathPart, composition, currentFolder);
                currentFolder.SubFolders.Add(newFolder);
                currentFolder = newFolder;
            }
        }
        currentFolder.FolderAssets.Add(asset);
    }

    private bool TryGetSubFolder(string folderName, [NotNullWhen(true)] out AssetFolder? subFolder)
    {
        subFolder = SubFolders.FirstOrDefault(n => n.Name == folderName);
        return subFolder != null;
    }

    private string GetAliasPath()
    {
        var sb = new StringBuilder(4);

        var stack = new Stack<string>();
        var t = this;
        while (t != null && t.Name != RootNodeId)
        {
            stack.Push(t.Name);
            t = t.Parent;
        }

        var first = true;
        while (stack.Count > 0)
        {
            sb.Append(stack.Pop());
            if (first)
            {
                sb.Append(AssetRegistry.PackageSeparator);
                first = false;
            }
            else
            {
                sb.Append(AssetRegistry.PathSeparator);
            }
        }

        return sb.ToString();
    }

    private void Clear()
    {
        SubFolders.Clear();
        FolderAssets.Clear();
    }

    public override string ToString()
    {
        return $"[{Name}/]";
    }

    internal readonly List<Asset> FolderAssets = [];
    internal const string RootNodeId = "__root__";
}