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

    /// <summary>
    /// When set, dropping this asset onto the timeline clip area creates a TimeClip-backed op of this
    /// symbol (e.g. AudioClip, VideoClip, LoadDataClip). Kept separate from <see cref="PrimaryOperators"/>[0]
    /// (the graph-drop default) because a type's timeline-clip op can differ from its graph op — e.g. video
    /// drops <c>PlayVideo</c> on the graph but <c>VideoClip</c> on the timeline. Null ⇒ not timeline-droppable.
    /// </summary>
    public Guid? TimelineClipOperator;

    /// <summary>
    /// Subfolder under the project's <c>Assets/</c> where live-session recordings of this type are imported,
    /// and which the recorder scans to compute the next session index. Non-null marks the type as recordable.
    /// Set in <c>AssetHandling</c> — e.g. Audio → <c>"audio"</c>, Data → <c>"dataclips"</c>.
    /// </summary>
    public string? RecordingFolder;

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