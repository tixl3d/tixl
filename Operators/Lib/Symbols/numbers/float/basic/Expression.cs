#nullable enable
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lib.numbers.@float.basic;

/// <summary>
/// Evaluates a C# expression with variables a, b, c, d. The expression is compiled
/// with Roslyn and cached. Common math functions (sin, cos, tan, etc.) are available
/// via the statically imported ExpressionFunctions helper.
/// Example: (1 - a) + (b * 2 * c) - d / sin(c)
/// </summary>
[Guid("a3f7c2e1-9b4d-4e8f-b6a1-2c5d8e0f3a7b")]
[ExportDependencies("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll")]
internal sealed class Expression : Instance<Expression>
{
    [Output(Guid = "b8e4d1f6-3a7c-4f2e-9d5b-1c6a8e0f4b2d")]
    public readonly Slot<float> Result = new();

    public Expression()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var expression = ExpressionInput.GetValue(context);
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var c = C.GetValue(context);
        var d = D.GetValue(context);

        if (string.IsNullOrWhiteSpace(expression))
        {
            Result.Value = float.NaN;
            return;
        }

        if (expression != _compiledExpression)
        {
            Compile(expression);
        }

        var evaluate = _evaluate;
        if (evaluate == null)
        {
            Result.Value = float.NaN;
            return;
        }

        Result.Value = evaluate(a, b, c, d);
    }

    private void Compile(string expression)
    {
        _compiledExpression = expression;

        var source = $$"""
                        using System;
                        using static System.MathF;
                        using static Lib.numbers.@float.basic.ExpressionFunctions;

                        public static class __Expression
                        {
                            public static float Evaluate(float a, float b, float c, float d)
                            {
                                return {{expression}};
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
                || assembly == typeof(Expression).Assembly)
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        var compilation = CSharpCompilation.Create("ExpressionSnippet",
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
                T3.Core.Logging.Log.Warning($"Expression line {line}: {diagnostic.GetMessage()}", this);
            }

            return;
        }

        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("ExpressionSnippet", isCollectible: true);
        var snippetAssembly = loadContext.LoadFromStream(stream);
        var method = snippetAssembly.GetType("__Expression")?.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            T3.Core.Logging.Log.Warning("Expression: compiled snippet has no Evaluate method", this);
            loadContext.Unload();
            return;
        }

        _evaluate = method.CreateDelegate<ExpressionFn>();
        _loadContext?.Unload();
        _loadContext = loadContext;
    }

    private delegate float ExpressionFn(float a, float b, float c, float d);

    private string? _compiledExpression;
    private ExpressionFn? _evaluate;
    private AssemblyLoadContext? _loadContext;

    [Input(Guid = "c9d5e2f8-4b1a-4c7e-8f3d-2a9b6e0c5d1f")]
    public readonly InputSlot<string> ExpressionInput = new("a + b");

    [Input(Guid = "d0e6f3a9-5c2b-4d8f-9a4e-3b0c7f1d6e2a")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "e1f7a4b0-6d3c-4e9a-0b5f-4c1d8a2e7f3b")]
    public readonly InputSlot<float> B = new();

    [Input(Guid = "f2a8b5c1-7e4d-4f0b-1c6a-5d2e9b3f8a4c")]
    public readonly InputSlot<float> C = new();

    [Input(Guid = "a3b9c6d2-8f5e-4a1c-2d7b-6e3f0c4a9b5d")]
    public readonly InputSlot<float> D = new();
}

/// <summary>Helper functions statically imported into Expression snippets.</summary>
public static class ExpressionFunctions
{
    public static float Clamp(float x, float min, float max) => x < min ? min : (x > max ? max : x);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float DegToRad(float deg) => deg * MathF.PI / 180f;
    public static float RadToDeg(float rad) => rad * 180f / MathF.PI;
}
