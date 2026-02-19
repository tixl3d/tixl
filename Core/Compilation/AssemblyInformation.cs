#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Utils;
using T3.Serialization;

namespace T3.Core.Compilation;

/// <summary>
/// This class is used as the primary entry point for loading assemblies and extracting information about the types within them.
/// This is where we find all of the operators and their slots, as well as any other type implementations that are relevant to tooll.
/// This is also where C# dependencies need to be resolved, which is why each instance of this class has a reference to a <see cref="TixlAssemblyLoadContext"/>.
/// </summary>
public sealed partial class AssemblyInformation
{
    public string Name 
    { 
        get => _name ?? RootNamespace; // Fallback to RootNamespace if _name is null
        private set => _name = value; 
    }
    private string? _name;
    
    public string RootNamespace 
    {
        get
        {
            if (TryGetReleaseInfo(out var releaseInfo))
                return releaseInfo.RootNamespace;
            
            return string.Empty;
        }
    }
    
    public string Directory => _directory!;

    internal Guid Id =>
        TryGetReleaseInfo(out var releaseInfo) 
            ? releaseInfo.PackageId 
            : Guid.Empty; 

    public bool IsLoaded => _loadContext != null;

    private bool _loadedTypes;
    public event Action<AssemblyInformation>? Unloaded;
    public event Action<AssemblyInformation>? UnloadComplete;
    public event Action<AssemblyInformation>? Loaded;

    internal const BindingFlags ConstructorBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance;

    private WeakReference? _ctxReference;
    private WeakReference? _asmReference;

    public IReadOnlyDictionary<Guid, OperatorTypeInfo> OperatorTypeInfo => _operatorTypeInfo;
    private readonly ConcurrentDictionary<Guid, OperatorTypeInfo> _operatorTypeInfo = new();
    private readonly Dictionary<string, Type> _types = new();
    public IReadOnlySet<string> Namespaces => _namespaces;
    private readonly HashSet<string> _namespaces = [];

    internal bool ShouldShareResources;
    internal TixlAssemblyLoadContext? LoadContext => _loadContext;
    private TixlAssemblyLoadContext? _loadContext;
    private readonly Lock _assemblyLock = new();

    public static AssemblyInformation CreateUninitialized()
    {
        return new AssemblyInformation();
    }
    
    private string? _directory;
    private bool _isReadOnly;
    private bool _initialized;
    private ReleaseInfo? _releaseInfo;

    /// <summary>
    /// Constructor used for creating an uninitialized instance of <see cref="AssemblyInformation"/>.
    /// Useful for creating an instance without initializing it immediately - at time of writing, this is how editable symbol projects are created.
    /// </summary>
    private AssemblyInformation()
    {
        Name = null!;
    }
    
    /// <summary>
    /// Constructor used for creating a read-only instance of <see cref="AssemblyInformation"/> with the given directory.
    /// </summary>
    /// <param name="directory"></param>
    public AssemblyInformation(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Initialize(directory, true);
        Name=string.Empty;
    }

    public void Initialize(string directory, bool isReadOnly)
    {
        if(_initialized)
            throw new InvalidOperationException($"Cannot initialize assembly information for {Name} - already initialized");
        
        _isReadOnly = isReadOnly;
        _directory = directory;
        _initialized = true;
        
        if(ProjectSettings.Config.LogCompilationDetails)
            Log.Debug($"{Name}: Assembly information initialized");
    }


    /// <summary>
    /// The entry point for loading the assembly and extracting information about the types within it - particularly the operators.
    /// However, loading an assembly's types in this way will also trigger the <see cref="TixlAssemblyLoadContext"/> so that its dependencies are resolved.
    /// </summary>
    internal bool TryLoadTypes()
    {
        lock (_assemblyLock)
        {
            if (_loadContext == null)
            {
                GenerateLoadContext();
                if (_loadContext == null)
                    return false;
            }
            
            var rootNode = _loadContext.Root;
            if (rootNode != null && _loadedTypes)
            {
                Log.Debug($"{Name}: Already loaded types");
                return true;
            }

            if (rootNode == null)
            {
                Log.Error($"Failed to get assembly for {Name}");
                ShouldShareResources = false;
                return false;
            }

            try
            {
                var types = rootNode.Assembly.GetTypes();
                LoadTypes(types, rootNode.Assembly, out ShouldShareResources, _operatorTypeInfo, _namespaces, _types);
                _loadedTypes = true;
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to load types from assembly {rootNode.Assembly.FullName}\n{e.Message}\n{e.StackTrace}");
                _types.Clear();
                ShouldShareResources = false;
                _loadedTypes = true;
                return false;
            }
        }
    }

    public IEnumerable<Type> TypesInheritingFrom(Type type)
    {
        lock (_assemblyLock)
        {
            if (!_loadedTypes && !TryLoadTypes())
            {
                return [];
            }

            return _types!.Values.Where(t => t.IsAssignableTo(type));
        }
    }

    public void ChangeAssemblyDirectory(string directory)
    {
        if(_loadContext != null)
        {
            throw new InvalidOperationException($"Cannot change directory of assembly {Name} while it is loaded");
        }
        
        _directory = directory;
    }

    private void Unloading(AssemblyLoadContext obj) // todo: this references prevents gc unload from within this function
    {
        Log.Debug($"{Name}: Assembly actually unloading");

        obj.Unloading -= Unloading;

        /*
         * We need to dereference the assembly to allow its types to be unloaded, allowing us to recompile into the same directory.
         * This is done by forcing a garbage collection and waiting for finalizers to complete.
         * If the references are still alive after a certain number of attempts, we log a warning.
         */
        /*
        obj = null!;
        const int maxTryCount = 10;
        int i = 0;
        for (; _asmReference!.IsAlive && (i < maxTryCount); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (i >= maxTryCount)
        {
            Log.Warning($"Failed to unload assembly {Name} - reference is still alive after {maxTryCount} attempts");
        }
        else
        {
            Log.Debug($"{Name}: Assembly completed unloaded after {i} attempts");
        }
        
        int t = 0;
        for(; t < maxTryCount && _ctxReference!.IsAlive; t++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        
        if (t >= maxTryCount)
        {
            Log.Warning($"Failed to unload context {Name} - reference is still alive after {maxTryCount} attempts");
        }
        else
        {
            Log.Debug($"{Name}: Context completed unload after {t} attempts");
        }
        
        var name = Name;
        var existingReferencesToMe = AssemblyLoadContext.All
                                                        .Where(x => x.Assemblies.Any(asm => asm.FullName != null && asm.FullName.Contains(name)))
                                                        .ToArray();
        if (existingReferencesToMe.Length != 0)
        {
            var sb = new StringBuilder();
            sb.Append("Assembly Info ")
                .Append(name)
                .Append(" failed to free its context. ")
                .Append(existingReferencesToMe.Length)
                .Append(" other contexts have references to it:\n");
            foreach(var refHolder in existingReferencesToMe)
            {
                sb.Append('\t')
                    .Append(refHolder.Name)
                    .Append('\n');
            }
            Log.Error(sb.ToString());
        }
        */
        
        _ctxReference = null;
        _asmReference = null;
        
        try
        {
            UnloadComplete?.Invoke(this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to invoke {nameof(UnloadComplete)} for {Name}: {ex}");
        }
    }

    private void OnUnloadBegan(object? sender, EventArgs e)
    {
        lock (_assemblyLock)
        {
            _loadContext!.UnloadBegan -= OnUnloadBegan;
            _loadContext = null;
            _loadedTypes = false;
            _releaseInfo = null;
            _operatorTypeInfo.Clear();
            _types.Clear(); // explicitly dereference all our types
            _namespaces.Clear();
            Log.Debug($"{Name}: Assembly information unloaded");

            try
            {
                Unloaded?.Invoke(this);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to invoke Unloaded event for {Name}: {ex}");
            }
        }
    }

    /// <summary>
    /// Does all within its power to unload the assembly from its load context.
    /// In order for an assembly to properly be unloaded, ALL references to it, including existing instances, references to its types, etc,
    /// must be released and dereferenced.
    /// </summary>
    public void Unload()
    {
        if (!IsLoaded)
            return;
        _loadContext?.BeginUnload();
        _loadContext = null;
    }
    
    /// <summary>
    /// Tries to get the release info for the package by looking for <see cref="RuntimeAssemblies.PackageInfoFileName"/> in the directory of the assembly.
    /// </summary>
    /// <param name="releaseInfo"></param>
    /// <returns></returns>
    public bool TryGetReleaseInfo([NotNullWhen(true)] out ReleaseInfo? releaseInfo)
    {
        if (_releaseInfo == null && _directory != null)
        {
            TryLoadReleaseInfo(_directory, out _releaseInfo);
        }
        
        releaseInfo = _releaseInfo;
        return releaseInfo != null;
    }

    public static bool TryLoadReleaseInfo(string directory, [NotNullWhen(true)] out ReleaseInfo? releaseInfo)
    {
        var filePath = Path.Combine(directory, ReleaseInfo.FileName);
        if (!JsonUtils.TryLoadingJson<ReleaseInfoSerialized>(filePath, out var releaseInfoSerialized))
        {
            Log.Warning($"Could not load package info from path {filePath}");
            releaseInfo = null;
            return false;
        }

        releaseInfo = releaseInfoSerialized.ToReleaseInfo();
        return true;
    }

    /// <summary>
    /// Returns true if the given package reference matches the given release info.
    /// </summary>
    public static bool Matches(OperatorPackageReference reference, ReleaseInfo releaseInfo)
    {
        if (reference.ResourcesOnly)
            return false;

        var identity = reference.Identity;
        var assemblyFileName = releaseInfo.AssemblyFileName;

        // todo : version checks

        return identity.SequenceEqual(assemblyFileName);
    }

    /// <summary>
    /// Creates an instance of the given type using this assembly via (slow) reflection.
    /// </summary>
    public object? CreateInstance(Type constructorInfoInstanceType)
    {
        var assembly = _loadContext!.Root?.Assembly;

        if (assembly == null)
        {
            Log.Error($"Failed to get assembly for {Name}");
            return null;
        }

        return assembly.CreateInstance(constructorInfoInstanceType.FullName!);
    }

    public void GenerateLoadContext()
    {
        if(!_initialized)
            throw new InvalidOperationException($"Cannot generate load context for {Name} - not initialized");
        
        lock (_assemblyLock)
        {
            if (_loadContext != null)
                return;

            if (_directory == null)
            {
                throw new InvalidOperationException($"Cannot create load context for {Name} - directory is null");
            }

            ReleaseInfo? releaseInfo;
            try
            {
                if (!TryLoadReleaseInfo(_directory, out releaseInfo))
                {
                    throw new Exception($"Failed to load release info from {_directory} - does it need to be compiled?");
                }

                _loadContext = new TixlAssemblyLoadContext(releaseInfo.AssemblyFileName, Directory, _isReadOnly);

                var asm = _loadContext.Root!.Assembly;
                _asmReference = new WeakReference(asm, true);
                _ctxReference = new WeakReference(_loadContext, true);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to create assembly load context for {Name}\n{e.Message}\n{e.StackTrace}");
                _loadContext = null;
                return;
            }

            _releaseInfo = releaseInfo;
            _loadContext.UnloadBegan += OnUnloadBegan;
            _loadContext.Unloading += Unloading;
            Name = _loadContext!.Name!; 

            try
            {
                Loaded?.Invoke(this);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to invoke Loaded event for {Name}: {ex}");
            }
        }
    }
}