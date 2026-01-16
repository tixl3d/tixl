#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace T3.Core.Operator;

public abstract partial class Instance :  IGuidPathContainer, IResourceConsumer
{
    public abstract Type Type { get; }

    public Guid SymbolChildId => SymbolChild.Id;
    public InstanceChildren Children { get; private set; }
    public event Action<IResourceConsumer>? Disposing;

    public abstract Symbol Symbol { get; }
    public Symbol.Child SymbolChild { get; private set; }
    public readonly List<ISlot> Outputs;
    public readonly List<IInputSlot> Inputs;
    public IReadOnlyList<Guid> InstancePath { get; private set; }

    public IReadOnlyList<IResourcePackage> AvailableResourcePackages
    {
        get
        {
            GatherResourcePackages(this, ref _availableResourcePackages);
            return _availableResourcePackages;
        }
    }

    private Guid[]? _parentPath;
    private Symbol.Child? _parentSymbolChild;

    private int? _pathHash;
    private List<SymbolPackage> _availableResourcePackages = [];

    private readonly List<ISlot> _outputs = [];
    private readonly List<IInputSlot> _inputs = [];

    protected Instance()
    {
        Outputs = _outputs;
        Inputs = _inputs;
        Children = null!;
        SymbolChild = null!;
        InstancePath = [];
    }

    internal void SetSymbolInfo(Symbol.Child child, Symbol.Child? parent, IReadOnlyList<Guid> instancePath, int pathHash)
    {
        if(SymbolChild != null)
            throw new InvalidOperationException("Instance already has a symbol child");
        
        if (instancePath.Count > 1)
        {
            _parentPath = new Guid[instancePath.Count - 1];
            for (var i = 0; i < _parentPath.Length; i++)
            {
                _parentPath[i] = instancePath[i];
            }
        }
        
        InstancePath = instancePath;
        _pathHash = pathHash;
        SymbolChild = child;
        _parentSymbolChild = parent;
        Children = new InstanceChildren(instancePath, child);
    }
    
    internal void Initialize(Instance? parentInstance)
    {
        SortInputSlotsByDefinitionOrder(this);
        
        Debug.Assert(_status == default);

        parentInstance ??= Parent;
        if (parentInstance is { Initialized: true, IsReconnecting: false })
        {
            // mark parent as needing reconnections if it's already initialized
            parentInstance.MarkNeedsConnections();
        }

        _status |= InstanceStatus.ResourceFoldersDirty; // we need to mark resource folders as dirty, so they are loaded by ops that need them
        foreach (var childInstance in Children.PreExistingValues)
        {
            childInstance.MarkResourceFoldersDirty();
        }
        
        ReconnectChildren();
        
        _status |= InstanceStatus.Initialized;
        _status |= InstanceStatus.Active;
    }

    private void MarkResourceFoldersDirty()
    {
        _status |= InstanceStatus.ResourceFoldersDirty;
        foreach(var child in Children.PreExistingValues)
        {
            child.MarkResourceFoldersDirty();
        }
    }

    public Instance? Parent
    {
        get
        {
            // a procedural lookup from the parent,
            // since we cannot count on it existing at all times (particularly during loading or unloading)
            if (_parentSymbolChild == null)
            {
                return null;
            }
            
            if (_parentPath == null)
                throw new InvalidOperationException($"Parent path is not initialized.");
            
            if(_parentSymbolChild.TryGetOrCreateInstance(_parentPath, out var parentInstance, out _))
            {
                return parentInstance;
            }
            Log.Error($"Could not find parent instance for {_parentSymbolChild} with path {_parentPath}");
            return null;
        }
    }

    internal bool TryGetParentInstance([NotNullWhen(true)] out Instance? instance, bool allowCreate)
    {
        if (_parentSymbolChild == null || _parentPath == null)
        {
            instance = null;
            return false;
        }
        
        if(_parentSymbolChild!.TryGetOrCreateInstance(_parentPath, out var parentInstance, out _, allowCreate))
        {
            instance = parentInstance;
            return true;
        }

        instance = null;
        return false;
    }

    SymbolPackage IResourceConsumer.Package => Symbol.SymbolPackage;


    /// <summary>
    /// get input without GC allocations 
    /// </summary>
    public IInputSlot? GetInput(Guid guid)
    {
        //return Inputs.SingleOrDefault(input => input.Id == guid);
        var inputCount = Inputs.Count;
        for (var index = 0; index < inputCount; index++)
        {
            var i = Inputs[index];
            if (i.Id == guid)
                return i;
        }

        return null;
    }

    ~Instance()
    {
        if ((_status & InstanceStatus.Disposed) == 0)
        {
            Log.Error($"Instance {GetType()} was not disposed properly");
            // todo : do we want to invoke disposal here? could this prevent memory leaks or other shutdown/reload issues?
            // would it cause thread safety issues? probably..
            // but currently this is using a disposal pattern designed around finalizers (disposing = true), etc
        }
    }

    internal void Dispose(SymbolPackage? packageToDispose)
    {
        if ((_status & InstanceStatus.Disposed) != 0)
        {
            throw new Exception($"{this} has already been disposed\n" + Environment.StackTrace);
        }
        
        _status &= ~InstanceStatus.Active; // remove active status
        Children.Dispose(packageToDispose);
        
        // clear connections - they need to be repopulated with valid connections next time
        // we don't clear outputs as they are solely connected to by internal instances
        var disconnectCount = DisconnectInputs();

        // we should let the parent know it's missing someone
        if (disconnectCount != 0 && TryGetParentInstance(out var parentInstance, false))
        {
            parentInstance.MarkNeedsConnections();
        }
        
        // if a package was specified that is not our own, we can live to see another day
        if (packageToDispose != null && Symbol.SymbolPackage != packageToDispose)
        { 
            return;
        }
        
        _status |= InstanceStatus.Disposed;
        
        try
        {
            Dispose(true);
        }
        catch (Exception e)
        {
            Log.Error($"Error disposing instance {this}: {e}");
        }

        // and remove ourselves from the symbol child
        SymbolChild.RemoveDisposedInstance(this, _pathHash!.Value);
        
        // any resources we have should be disposed of by the child class
        try
        {
            Disposing?.Invoke(this);
        }
        catch (Exception e)
        {
            Log.Error($"Error on dispose event for {this}: {e}");
        }
        
        // warning CA1816: Dispose methods should call SuppressFinalize on themselves
        // it doesn't recognize this as a dispose method due to its argument type
        #pragma warning disable CA1816
        GC.SuppressFinalize(this);
        #pragma warning restore CA1816
    }

    private static void GatherResourcePackages(Instance? instance, ref List<SymbolPackage> resourcePackages)
    {
        if((instance!._status & InstanceStatus.ResourceFoldersDirty) == 0) 
            return;
            
        instance._status &= ~InstanceStatus.ResourceFoldersDirty;

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (resourcePackages != null)
        {
            resourcePackages.Clear();
        }
        else
        {
            resourcePackages = [];
        }

        while (instance != null)
        {
            var package = instance.Symbol.SymbolPackage;
            if (!resourcePackages.Contains(package))
            {
                resourcePackages.Add(package);
            }

            instance = instance.Parent;
        }
    }

    private string? _asString;
}