#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using T3.Core.Utils;

namespace T3.Core.Resource.Assets;

/// <summary>
/// Defines a potential file resource in a SymbolProject.
/// </summary>
public sealed class Asset
{
    public Asset(string address)
    {
        Address = address;
        Id = address.GenerateGuidFromString();
    }
    
    public readonly string Address;
    public readonly Guid Id; 
    public required Guid PackageId;
    public FileSystemInfo? FileSystemInfo;

    public AssetType AssetType = AssetType.Unknown;
    public int ExtensionId;

    public bool IsDirectory;

    // Added to support folder structure in UI without re-parsing
    public IReadOnlyList<string> PathParts { get; internal init; } = [];

    public long FileSize
    {
        get
        {
            if (FileSystemInfo is not FileInfo fi) return 0;
            fi.Refresh();
            return fi.Exists ? fi.Length : 0;
        }
    }

    public override string ToString()
    {
        return Address + (IsDirectory ? " (Dir)" : AssetType);
    }
}

/// <summary>
/// A reference of a symbol child or input to an Package
/// </summary>
/// <remarks>
/// SymbolPackage will create a list of it's usages on init.
/// </remarks>
public sealed class AssetReference
{
    public required Asset Asset;
    public Guid SymbolId;
    public Guid SymbolChildId;
    public Guid InputId;
}