#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lib.Utils;

/// <summary>
/// Compiles a C# source that wraps a user snippet (placed after a <c>#line 1</c> directive, so errors report
/// snippet lines) and binds one public static method as a delegate. A failed compile keeps the previous delegate, so
/// ops stay alive while the user types. Each snippet loads into its own collectible context, unloaded when replaced.
/// Ops that compile snippets need [ExportDependencies("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll")].
/// </summary>
internal sealed class CSharpSnippetCompiler<TDelegate> where TDelegate : Delegate
{
    public TDelegate? Delegate { get; private set; }

    /// <param name="errors">Filled with "line N: message" per error; empty on success.</param>
    public bool TryCompile(string source, string typeName, string methodName, string assemblyName, List<string> errors)
    {
        errors.Clear();
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(assemblyName,
                                                   [syntaxTree],
                                                   GetReferences(),
                                                   new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                                                                optimizationLevel: OptimizationLevel.Release));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            foreach (var diagnostic in emitResult.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;

                var line = diagnostic.Location.GetMappedLineSpan().StartLinePosition.Line + 1;
                errors.Add($"line {line}: {diagnostic.GetMessage()}");
            }

            return false;
        }

        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var method = loadContext.LoadFromStream(stream).GetType(typeName)?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            errors.Add($"compiled snippet has no {methodName} method");
            loadContext.Unload();
            return false;
        }

        Delegate = method.CreateDelegate<TDelegate>();
        // The unloaded context stays alive as long as older consumers still hold its delegate
        _loadContext?.Unload();
        _loadContext = loadContext;
        return true;
    }

    /** The runtime, numerics and Lib itself, so snippets can use Lib's static helpers. */
    private static List<MetadataReference> GetReferences()
    {
        var references = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;

            var name = assembly.GetName().Name;
            if (name is "System.Private.CoreLib" or "System.Runtime" or "System.Numerics.Vectors"
                || assembly == typeof(CSharpSnippetCompiler<>).Assembly)
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        return references;
    }

    private AssemblyLoadContext? _loadContext;
}
