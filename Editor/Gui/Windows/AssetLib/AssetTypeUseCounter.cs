#nullable enable
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Helper to count and display how often assets are used.
/// </summary>
public static class AssetTypeUseCounter {

    public static void IncrementUseCount(AssetType assetType)
    {
        if (_counts.Length != AssetType.AvailableTypes.Count)
        {
            _counts = new int[AssetType.AvailableTypes.Count];
        }

        _counts[assetType.Index]++;
    }

    internal static int GetUseCount(AssetType assetType)
    {
        if (assetType.Index >= _counts.Length)
            return 0;
        
        return _counts[assetType.Index];
    } 
    
    internal static void ClearMatchingFileCounts()
    {
        Array.Clear(_counts, 0, _counts.Length);
    }

    private static int[] _counts = [];
}