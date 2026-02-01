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

    public bool TryGetFileName(out ReadOnlySpan<char> filename)
    {
        filename = ReadOnlySpan<char>.Empty;
        if (IsDirectory)
            return false;

        var packageSep = Address.IndexOf(AssetRegistry.PackageSeparator);
        if (packageSep == -1)
            return false;
        
        var localPath = Address.AsSpan()[(packageSep+1)..];
        if (localPath.Length == 0)
            return false;
        
        var lastSlash = localPath.LastIndexOf(AssetRegistry.PathSeparator);
        if (lastSlash == -1)
        {
            filename = localPath;
            return true;
        }

        filename = localPath[(lastSlash+1)..];
        return true;
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

    public bool IsDefaultValueReference => SymbolChildId == Guid.Empty;
}