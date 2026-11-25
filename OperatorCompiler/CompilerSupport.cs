using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace T3.OperatorCompiler;

/// <summary>
/// The .NET reference assemblies and source generators that `dotnet build` would take from the SDK, loaded once and
/// shared by all compiles of the process. Thread-safe.
/// </summary>
public sealed class CompilerSupport
{
    /// <param name="referencePackDirectory">
    /// The root of a Microsoft.NETCore.App.Ref package: <c>data/FrameworkList.xml</c>, <c>ref/net10.0/</c> and
    /// <c>analyzers/dotnet/cs/</c>.
    /// </param>
    public CompilerSupport(string referencePackDirectory)
    {
        ReferencePackDirectory = Path.GetFullPath(referencePackDirectory);
        _frameworkReferences = new Lazy<ImmutableArray<MetadataReference>>(LoadFrameworkReferences);
        _generators = new Lazy<ImmutableArray<ISourceGenerator>>(LoadGenerators);
    }

    public string ReferencePackDirectory { get; }

    /** True if the reference pack is complete enough to compile with. */
    public bool IsAvailable => File.Exists(FrameworkListPath);

    internal ImmutableArray<MetadataReference> FrameworkReferences => _frameworkReferences.Value;
    internal ImmutableArray<ISourceGenerator> Generators => _generators.Value;
    internal IReadOnlyCollection<string> GeneratorLoadErrors => _generatorLoadErrors;

    /// <summary>
    /// A reference to an assembly outside the reference pack (the editor's Core.dll and friends), re-read only
    /// when the file changed.
    /// </summary>
    internal MetadataReference GetReference(string path)
    {
        var file = new FileInfo(path);
        var stamp = (file.LastWriteTimeUtc, file.Length);
        if (_referencesByPath.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Reference;

        var reference = MetadataReference.CreateFromFile(path);
        _referencesByPath[path] = (stamp, reference);
        return reference;
    }

    private string FrameworkListPath => Path.Combine(ReferencePackDirectory, "data", "FrameworkList.xml");

    /** The SDK references exactly the managed files its reference pack lists, so do the same. */
    private ImmutableArray<MetadataReference> LoadFrameworkReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var file in XDocument.Load(FrameworkListPath).Root!.Elements("File"))
        {
            if (file.Attribute("Type")?.Value != "Managed")
                continue;

            var path = Path.Combine(ReferencePackDirectory, file.Attribute("Path")!.Value);
            builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<ISourceGenerator> LoadGenerators()
    {
        var directory = Path.Combine(ReferencePackDirectory, "analyzers", "dotnet", "cs");
        if (!Directory.Exists(directory))
            return [];

        var loader = new GeneratorAssemblyLoader(directory);
        var builder = ImmutableArray.CreateBuilder<ISourceGenerator>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            var reference = new AnalyzerFileReference(path, loader);
            reference.AnalyzerLoadFailed += (_, e) => _generatorLoadErrors.Add($"{Path.GetFileName(path)}: {e.Message}");
            builder.AddRange(reference.GetGenerators(LanguageNames.CSharp));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Loads generator assemblies and their helpers from the reference pack into a context of their own, while
    /// Roslyn itself resolves to the copy the host already loaded (the generators must see the same Roslyn types).
    /// </summary>
    private sealed class GeneratorAssemblyLoader(string directory) : AssemblyLoadContext("OperatorCompilerGenerators"), IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => LoadFromAssemblyPath(fullPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private readonly Lazy<ImmutableArray<MetadataReference>> _frameworkReferences;
    private readonly Lazy<ImmutableArray<ISourceGenerator>> _generators;
    private readonly ConcurrentBag<string> _generatorLoadErrors = [];
    private readonly ConcurrentDictionary<string, ((DateTime, long) Stamp, MetadataReference Reference)> _referencesByPath = new(StringComparer.Ordinal);
}
