using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lib.geometry;

/// <summary>
/// Compiles a C# snippet into a ScalarField. The snippet is the body of a method
/// returning float, with the sample position `p`, the parameters `A`-`D`, and the
/// optional point positions `Points` in scope. System.MathF and the FieldCode
/// helpers are statically imported.
/// </summary>
[Guid("f68c9e5a-2d17-4b84-a3c6-8e0f5b7d21a9")]
[ExportDependencies("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll")]
internal sealed class CustomScalarField : Instance<CustomScalarField>
{
    [Output(Guid = "0a5e8d3c-71b9-4f26-9c48-d2e6a0b4f817")]
    public readonly Slot<ScalarField> Result = new();

    public CustomScalarField()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var code = Code.GetValue(context);
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var c = C.GetValue(context);
        var d = D.GetValue(context);

        if (string.IsNullOrWhiteSpace(code))
        {
            Result.Value = null;
            return;
        }

        if (code != _compiledCode)
        {
            Compile(code);
        }

        var evaluate = _evaluate;
        if (evaluate == null)
        {
            Result.Value = null;
            return;
        }

        var points = SnapshotPoints(context);
        Result.Value = new ScalarField((in FieldSample sample) => evaluate(sample.Position, a, b, c, d, points));
    }

    private Vector3[] SnapshotPoints(EvaluationContext context)
    {
        if (Points.GetValue(context) is not StructuredList<Point> pointList || pointList.NumElements == 0)
            return [];

        var elements = pointList.TypedElements;
        var count = 0;
        for (var i = 0; i < pointList.NumElements; i++)
        {
            if (!Point.IsSeparator(elements[i]))
                count++;
        }

        var snapshot = new Vector3[count];
        var writeIndex = 0;
        for (var i = 0; i < pointList.NumElements; i++)
        {
            if (!Point.IsSeparator(elements[i]))
                snapshot[writeIndex++] = elements[i].Position;
        }

        return snapshot;
    }

    private void Compile(string code)
    {
        _compiledCode = code;

        var source = $$"""
                       using System;
                       using System.Numerics;
                       using static System.MathF;
                       using static Lib.geometry.FieldCode;

                       public static class __CustomScalarField
                       {
                           public static float Evaluate(Vector3 p, float A, float B, float C, float D, Vector3[] Points)
                           {
                       #line 1
                       {{code}}
                           }
                       }
                       """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;

            var name = assembly.GetName().Name;
            if (name is "System.Private.CoreLib" or "System.Runtime" or "System.Numerics.Vectors"
                || assembly == typeof(CustomScalarField).Assembly)
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        var compilation = CSharpCompilation.Create("CustomScalarFieldSnippet",
                                                   [syntaxTree],
                                                   references,
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
                Log.Warning($"CustomScalarField line {line}: {diagnostic.GetMessage()}", this);
            }

            return; // keep the previous delegate so the field stays alive while typing
        }

        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("CustomScalarFieldSnippet", isCollectible: true);
        var snippetAssembly = loadContext.LoadFromStream(stream);
        var method = snippetAssembly.GetType("__CustomScalarField")?.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            Log.Warning("CustomScalarField: compiled snippet has no Evaluate method", this);
            loadContext.Unload();
            return;
        }

        _evaluate = method.CreateDelegate<SnippetFn>();
        // The unloaded context stays alive as long as older fields still hold its delegate
        _loadContext?.Unload();
        _loadContext = loadContext;
    }

    private delegate float SnippetFn(Vector3 p, float a, float b, float c, float d, Vector3[] points);

    private string? _compiledCode;
    private SnippetFn? _evaluate;
    private AssemblyLoadContext? _loadContext;

    [Input(Guid = "6e2a9c47-d580-4b13-8f6e-a1c3d7b9e024")]
    public readonly InputSlot<string> Code = new();

    [Input(Guid = "8b4d1f63-27a9-4c58-b0d2-e5f8a3c61970")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "2c7e5a91-b3d8-4f04-96c1-7a0e4d8b2f53")]
    public readonly InputSlot<float> B = new();

    [Input(Guid = "d19f6b28-4e73-4a05-8c9d-3b6f1e0a7c42")]
    public readonly InputSlot<float> C = new();

    [Input(Guid = "7a3c8d54-91e6-4b27-a0f8-5d2b9c4e6138")]
    public readonly InputSlot<float> D = new();

    [Input(Guid = "ef05b2a8-6c41-4d97-b3e5-90a7f8d1c264")]
    public readonly InputSlot<StructuredList> Points = new();
}

/// <summary>Helpers statically imported into CustomScalarField snippets.</summary>
public static class FieldCode
{
    public static float DistanceToClosestPoint(Vector3 position, Vector3[] points)
    {
        if (points.Length == 0)
            return float.MaxValue;

        var minDistanceSq = float.MaxValue;
        for (var i = 0; i < points.Length; i++)
        {
            var distanceSq = Vector3.DistanceSquared(position, points[i]);
            if (distanceSq < minDistanceSq)
                minDistanceSq = distanceSq;
        }

        return MathF.Sqrt(minDistanceSq);
    }
}
