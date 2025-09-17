#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using NuGet.Configuration;
using NuGet.Frameworks;
using T3.Core.Logging;
using Microsoft.Extensions.DependencyModel;
using T3.Core.IO;

namespace T3.Core.Compilation;

internal sealed partial class TixlAssemblyLoadContext
{
    private static readonly Lock _nugetLock = new();
    private static readonly AssemblyLoadContext _nugetContext = new("NuGet", true);
    private static readonly List<AssemblyTreeNode> _loadedNuGetAssemblies = [];

    // todo - per-project directory, since this walks up the directory provided to find nuget config files
    private static readonly string _nugetDirectory = SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(null, null, null));

    private readonly record struct NugetFrameworkDirectory(DirectoryInfo Directory, NuGetFramework Framework) : IFrameworkSpecific
    {
        NuGetFramework IFrameworkSpecific.TargetFramework => Framework;
    }

    private Assembly? SearchNugetForAssemblies(AssemblyName asmName, string name)
    {
        lock (_nugetLock)
        {
            foreach (var node in _loadedNuGetAssemblies)
            {
                if (node.TryFindExisting(name, out var asmNode))
                {
                    // add the dependency to our context
                    AddDependency(asmNode);
                    return asmNode.Assembly;
                }
            }

            foreach (var node in _loadedNuGetAssemblies)
            {
                if (node.TryFindUnreferenced(name, out var asmNode))
                {
                    // add the dependency to our context
                    AddDependency(asmNode);
                    return asmNode.Assembly;
                }
            }

            if (_dependencyContext != null)
            {
                // check if the assembly is in the dependency context
                var runtimeAssemblies = _dependencyContext.RuntimeLibraries;

                var assemblyVersion = asmName.Version;
                var assemblyVersionStr = "";
                var assemblyVersionRevisionStr = "";
                if (assemblyVersion != null)
                {
                    assemblyVersionStr = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
                    assemblyVersionRevisionStr =
                        $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.MajorRevision}/{assemblyVersion.MinorRevision}";
                }

                foreach (var library in runtimeAssemblies)
                {
                    if (library.Path == null)
                        continue;
                    var assemblyNames = library.GetDefaultAssemblyNames(_dependencyContext);
                    foreach (var libName in assemblyNames)
                    {
                        if (libName == asmName || (libName.Version == null && asmName.Name == libName.Name))
                        {
                            // this is the one we are looking for
                            // var assemblyPath = Path.Combine(_dependencyContext.RuntimeGraph.First().Runtime, library.Path, libName.Name + ".dll");
                            var assemblyRelativePath = library.Path;
                            var assemblyBasePath = Path.Combine(_nugetDirectory, assemblyRelativePath);
                            if (!Directory.Exists(assemblyBasePath))
                            {
                                Log.Debug("Failed to find nuget assembly directory: " + assemblyBasePath);
                                continue;
                            }

                            // throw runtime info to the end of the path to find our dll
                            var runtimes = _dependencyContext.RuntimeGraph;
                            foreach (var group in library.RuntimeAssemblyGroups)
                            {
                                if (group.Runtime is { Length: > 0 } && runtimes.All(x => x.Runtime != group.Runtime))
                                {
                                    // we cannot support the specified runtime
                                    continue;
                                }

                                foreach (var file in group.RuntimeFiles)
                                {
                                    var fileVersion = file.AssemblyVersion;
                                    if (fileVersion != null && assemblyVersion != null)
                                    {
                                        // we check the version of the assembly
                                        if (fileVersion != assemblyVersionStr && fileVersion != assemblyVersionRevisionStr)
                                        {
                                            continue;
                                        }
                                    }

                                    // todo - probably dont want or need a separate nuget context? but in the case of transitive dependencies this could be simplest..
                                    var fullPath = Path.Combine(assemblyBasePath, file.Path);
                                    if (!File.Exists(fullPath))
                                    {
                                        Log.Error($"NuGet assembly {asmName} not found at {fullPath}");
                                        continue;
                                    }

                                    var assembly = LoadAssembly(fullPath, _nugetContext);
                                    var newNode = new AssemblyTreeNode(assembly, _nugetContext, true, true, _dllImportResolver);
                                    AddDependency(newNode);
                                    _loadedNuGetAssemblies.Add(newNode);
                                    return newNode.Assembly;
                                }
                            }
                        }
                    }
                }
            }
        }

        if(ProjectSettings.Config.LogAssemblyLoadingDetails)
            Log.Error($"{Name}: Could not find nuget assembly '{asmName}'.");
        
        return null;
    }

    private readonly DependencyContext? _dependencyContext;
}