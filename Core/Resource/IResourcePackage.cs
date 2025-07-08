#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Model;

namespace T3.Core.Resource;

public interface IResourcePackage
{
    public string DisplayName { get; }
    public string? Alias { get; }
    public string ResourcesFolder { get; }
    public ResourceFileWatcher? FileWatcher { get; }
    public bool IsReadOnly { get; }
    public IReadOnlyCollection<DependencyCounter> Dependencies { get; }
}

public interface IResourceConsumer
{
    public IReadOnlyList<IResourcePackage> AvailableResourcePackages { get; }
    public SymbolPackage? Package { get; }
    public event Action<IResourceConsumer>? Disposing;
}