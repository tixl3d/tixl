#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Model;

namespace T3.Core.Resource;

public interface IResourcePackage
{
    string DisplayName { get; }
    string Name { get; }
    Guid Id { get; }
    string AssetsFolder { get; }
    string Folder { get; }
    string? RootNamespace { get; }
    ResourceFileWatcher? FileWatcher { get; }
    bool IsReadOnly { get; }
    IReadOnlyCollection<DependencyCounter> Dependencies { get; }
}

public interface IResourceConsumer
{
    IReadOnlyList<IResourcePackage> AvailableResourcePackages { get; }
    SymbolPackage? Package { get; }
    event Action<IResourceConsumer>? Disposing;
}