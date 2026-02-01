#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.Stats;
using T3.Core.Utils;

namespace T3.Core.Resource;

public interface IResource
{
    public IResourcePackage? OwningPackage { get; }
}
    
public sealed class Resource<T> : IDisposable, IResource
{
    IResourcePackage? IResource.OwningPackage => _owner?.Package;
        
    #region Constructors
    public Resource(InputSlot<string> slot, 
                    TryGenerate<T> tryGenerate,
                    bool allowDisposal = true, 
                    EqualityComparer<T?>? comparer = null)
        : this(slot.Value, slot.Parent, tryGenerate, allowDisposal, comparer)
    {
        _slot = slot;
    }

    public Resource(string? relativePath, IResourceConsumer? owner, TryGenerate<T> tryGenerate, 
                    bool allowDisposal = true, EqualityComparer<T?>? equalityComparer = null)
    {
        ArgumentNullException.ThrowIfNull(tryGenerate, nameof(tryGenerate));

        _tryGenerate = tryGenerate;
        _equalityComparer = equalityComparer ?? EqualityComparer<T?>.Default;
        _owner = owner;
        _allowDisposal = allowDisposal;

        _onFileChanged = OnFileUpdate;
        _valueFactory = ValueFactory;
        _onDispose = OnOwnerDisposed;

        if (owner != null)
            owner.Disposing += _onDispose;

        ReplaceFilePath(relativePath);
        _lazyValue = new Lazy<T?>(_valueFactory, ThreadSafetyMode);
    }
    #endregion
    public bool IsDisposed => _isDisposed;

    public T? Value
    {
        get
        {
            #if DEBUG
                if (_slot is not null && !_hasWarned)
                {
                    Log.Warning($"[{_owner}] It is recommended to use {nameof(GetValue)} instead of the Value property when using a slot." +
                                $" This is because the Value property will not update the slot value when the file is moved. " +
                                $"Slot: {_slot}", _owner!);
                    _hasWarned = true;
                }
            #endif

            ObjectDisposedException.ThrowIf(_isDisposed, this);

            return _lazyValue.Value;
        }
    }

    public T? GetValue(EvaluationContext context)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_slot is not { IsDirty: true })
            return _lazyValue.Value;

        var oldPath = _slot.Value;
        
        var newPath = _slot.GetValue(context);
        var hasChanged = oldPath != newPath;
        
        if(!hasChanged)
            return _lazyValue.Value;
        
        if (!string.IsNullOrWhiteSpace(newPath))
        {
            if (!_slot.HasInputConnections)
            {
                newPath.ToForwardSlashesUnsafe();
            }
            else
            {
                newPath = newPath.ToForwardSlashes();
            }
        }

        ReplaceFilePath(newPath);
        MarkFileAsChanged();

        return _lazyValue.Value;
    }

    public bool TryGetValue(EvaluationContext context, [NotNullWhen(true)] out T? value)
    {
        value = GetValue(context);
        return !_equalityComparer.Equals(value, default);
    }

    public void AddDependentSlots(ISlot outputSlot)
    {
        if (outputSlot is IInputSlot)
        {
            throw new InvalidOperationException("Cannot add an input slot as a dependent slot - only outputs are permitted.");
        }

        _dependentSlots.Add(outputSlot);
    }

    public void AddDependentSlots(params ISlot[] slots)
    {
        foreach (var slot in slots)
        {
            AddDependentSlots(slot);
        }
    }

    #region File Handling
    public void MarkFileAsChanged()
    {
        ResetLazyValue();
            
        // force-invalidate all dependent slots
        // it must be forced since this is called outside of the main invalidation loop
        // this isn't the prettiest solution but it works for now. 
        foreach (var slot in _dependentSlots)
            slot.DirtyFlag.ForceInvalidate();

        Changed?.Invoke();
        return;

        void ResetLazyValue()
        {
            if (!_lazyValue.IsValueCreated)
                return;

            DisposeValue();
            _lazyValue = new Lazy<T?>(_valueFactory, ThreadSafetyMode);
        }
    }

    /// <summary>
    /// Replaces the current file path with a new one and ensures we have a FileResource if possible
    /// </summary>
    /// <param name="newUserPath">Path to replace the current value of <see cref="_userPath"/></param>
    /// <returns>True if we have a file resource</returns>
    private bool ReplaceFilePath(string? newUserPath)
    {
        // if the file resource is already set and the path is the same, return true
        if (_fileResource != null && _userPath == newUserPath)
            return true;

        ReleaseFileResource();

        _userPath = newUserPath;

        if (!FileResource.TryGetFileResource(newUserPath, _owner, out var fileResource))
            return false;

        fileResource.Claim(this);
        _fileResource = fileResource;
        fileResource.FileChanged += _onFileChanged;
        return true;
    }

    private void ReleaseFileResource()
    {
        if (_fileResource == null) return;

        _fileResource.FileChanged -= _onFileChanged;
        _fileResource.Release(this);
        _fileResource = null;
    }

    private void OnFileUpdate(object? sender, WatcherChangeTypes changeTypes)
    {
        //Log.Debug($"Resource file '{_fileResource!.AbsolutePath}' changed: {changeTypes}");
        // change type-in value to the new path
        if (changeTypes.WasMoved())
        {
            // We know the fileResource is not null because this is only called by the file resource
            var newPath = _fileResource!.AbsolutePath;

            if (AssetRegistry.TryConvertFilepathToAddress(newPath, false, out var relativePath))
            {
                newPath = relativePath;
            }
                
            _userPath = newPath;

            if (_slot is { HasInputConnections: false })
                _slot.SetTypedInputValue(newPath.ToForwardSlashes());
        }

        MarkFileAsChanged();
    }
    #endregion

    #region Value Evaluation
    private T? ValueFactory()
    {
        string? failureReason;
        bool success;
        T? newValue;
        var hasFileResource = _fileResource != null || ReplaceFilePath(_userPath);

        if (hasFileResource && _fileResource!.FileInfo is { Exists : true })
        {
            try
            {
                // get current value without creating it if it hasn't been created yet
                var currentValue = _lazyValue.IsValueCreated ? _lazyValue.Value : default;
                success = _tryGenerate.Invoke(_fileResource, currentValue, out newValue, out failureReason);
            }
            catch (Exception e)
            {
                failureReason = e.Message;
                success = false;
                newValue = default;
            }
        }
        else
        {
            success = false;

            // #if !DEBUG
            failureReason = "File not found";
            // #else
            //     failureReason = "File not found:\n" + Environment.StackTrace;
            // #endif
            newValue = default;
        }

        if (success) 
            return newValue;
        
        // Log error state with readable warning
        var absolute = _fileResource?.AbsolutePath !=null ?
                           $"'{_fileResource?.AbsolutePath}'"
                           :string.Empty;

        if (string.IsNullOrEmpty(absolute))
            return newValue;
            
        var reason = failureReason ?? "Failed";
        var errorLog = $"{reason} '{_userPath}' {absolute} for {typeof(T).Name}";

        if (_owner != null)
        {
            if (_owner is Instance instance)
            {
                instance.LogErrorState(errorLog);    
            }
            else
            {
                Log.Error(errorLog, _owner);
            }
        }
        else
        {
            Log.Error(errorLog);
        }

        return newValue;
    }
    #endregion

    #region Value Disposal

    private void OnOwnerDisposed(IResourceConsumer owner)
    {
        if(owner != _owner)
            throw new Exception($"Owner mismatch: {owner} != {_owner}");
        Dispose();
    }
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        ReleaseFileResource();

        if (DisposeValue())
            Changed?.Invoke();

        if (_owner != null)
            _owner.Disposing -= _onDispose;

        Changed = null;
    }

    /// <summary>
    /// Returns true if item was disposed and changed
    /// </summary>
    /// <returns></returns>
    private bool DisposeValue()
    {
        if (!_allowDisposal || !IsDisposableType)
            return false;

        if (_lazyValue is not { IsValueCreated: true })
            return false;

        var value = _lazyValue.Value;
        if (_equalityComparer.Equals(value, default))
            return false;

        ((IDisposable)value!).Dispose();
        return true;
    }
    #endregion

    // type handling
    private static readonly bool IsDisposableType = typeof(IDisposable).IsAssignableFrom(typeof(T));
    private readonly EqualityComparer<T?> _equalityComparer;

    // file handling
    private FileResource? _fileResource;
    private readonly EventHandler<WatcherChangeTypes> _onFileChanged;
    private readonly IResourceConsumer? _owner;
    private string? _userPath;
    private readonly InputSlot<string>? _slot;

    // Value handling
    private Lazy<T?> _lazyValue;
    private readonly Func<T?> _valueFactory;
    private bool _isDisposed;
    private readonly TryGenerate<T> _tryGenerate;
    private readonly Action<IResourceConsumer> _onDispose;
    private readonly bool _allowDisposal;
    private const LazyThreadSafetyMode ThreadSafetyMode = LazyThreadSafetyMode.None;
    public event Action? Changed;

    private readonly List<ISlot> _dependentSlots = new();

    #if DEBUG
        private bool _hasWarned;
    #endif
}

public delegate bool TryGenerate<T>(FileResource file,
                                    T? currentValue,
                                    [NotNullWhen(true)] out T? newValue,
                                    [NotNullWhen(false)] out string? failureReason);