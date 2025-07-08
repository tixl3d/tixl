#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Model;

namespace T3.Core.Resource;

public sealed class TempResourceConsumer : IResourceConsumer
{
    private readonly SymbolPackage? _package;

    public TempResourceConsumer(IReadOnlyList<IResourcePackage> availableResourcePackages, SymbolPackage? package = null)
    {
        _package = package;
        AvailableResourcePackages = availableResourcePackages;
    }
    
    public IReadOnlyList<IResourcePackage> AvailableResourcePackages { get; }
    public SymbolPackage? Package => _package;
    public event Action<IResourceConsumer>? Disposing;
            
    public void TriggerDispose() => Disposing?.Invoke(this);
}