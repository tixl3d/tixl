#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Resource.Assets;

public sealed class AssetType
{
    public readonly string Name;
    public readonly List<int> ExtensionIds;
    public required List<Guid> PrimaryOperators;
    public required Color Color;
    public required uint IconId;
    public int Index;
    public string[] Subfolders = [];
    
    public AssetType(string name, List<int> extensionIds)
    {
        Name = name;
        ExtensionIds = extensionIds;
        foreach (var id in extensionIds)
        {
            _assetTypeForExtensionId[id] = this;
        }
    }
    
    public override string ToString()
    {
        return Name;
    }

    public static bool TryGetForFilePath(string filepath, out AssetType assetType, out int extensionId)
    {

        if (!FileExtensionRegistry.TryGetExtensionIdForFilePath(filepath, out extensionId))
        {
            assetType = Unknown;
            return false;
        }

        if (TryGetFromExtensionId(extensionId, out assetType!))
            return true;

        assetType = Unknown;
        return false;
    }

    
    
    public static bool TryGetFromExtensionId(int extensionId, [NotNullWhen(true)] out AssetType? type)
    {
        return _assetTypeForExtensionId.TryGetValue(extensionId, out type);
    }

    
    /// <summary>
    /// This is mostly UI specific and should be initialized by Editor on application startup.
    /// </summary>
    public static List<AssetType> AvailableTypes { get; private set; } = [];
    private static readonly Dictionary<int, AssetType> _assetTypeForExtensionId = [];
    
    public static readonly AssetType Unknown = new("unknown", [])
                                                   {
                                                       PrimaryOperators = [],
                                                       Color = default,
                                                       IconId = 0
                                                   };

    public static void RegisterType(AssetType newAssetType)
    {
        var index = AvailableTypes.Count;
        AvailableTypes.Add(newAssetType);
        newAssetType.Index = index;
    }
}