#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.attribute;

/// <summary>
/// Writes a float selection attribute (0..1) on one domain of a Geometry, by index pattern or by a C# snippet, and
/// combines it with an existing selection of the same name. Topology, positions and other attributes are shared.
/// </summary>
[Guid("55b64ede-9730-457c-8d3c-981a9fe6120e")]
[ExportDependencies("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll")]
internal sealed class SelectGeometry : Instance<SelectGeometry>, IStatusProvider, IInputStatusProvider, ITransformable
{
    [Output(Guid = "aa5016f3-1c6a-4978-a56d-3567c65c9610")]
    public readonly TransformCallbackSlot<Geometry?> Result = new();

    public SelectGeometry()
    {
        Result.TransformableOp = this;
        Result.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null!;
    IInputSlot ITransformable.ScaleInput => null!;

    public Action<Instance, EvaluationContext>? TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var domain = (Domains)Domain.GetValue(context).Clamp(0, 3);
        var mode = (Modes)Mode.GetValue(context).Clamp(0, 1);
        var every = Math.Max(1, Every.GetValue(context));
        var offset = Offset.GetValue(context);
        var limit = Count.GetValue(context);
        var code = Code.GetValue(context) ?? string.Empty;
        var a = A.GetValue(context);
        var b = B.GetValue(context);
        var center = Center.GetValue(context);
        var clampResult = ClampResult.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        var applyGainAndBias = gainAndBias != new Vector2(0.5f);
        var combine = (Combinations)Combine.GetValue(context).Clamp(0, 3);
        var invert = Invert.GetValue(context);
        var name = Name.GetValue(context);
        if (string.IsNullOrWhiteSpace(name))
            name = GeometryAttributeNames.Selection;

        if (source == null)
        {
            Result.Value = null;
            return;
        }

        var attributeDomain = ToAttributeDomain(domain);
        var isSnippetReady = mode == Modes.Custom && PrepareSnippet(code, source, attributeDomain);
        var count = GeometryElements.GetCount(source, attributeDomain);

        _output.Positions = source.Positions;
        _output.PrimitiveVertexOffsets = source.PrimitiveVertexOffsets;
        _output.VertexPoints = source.VertexPoints;
        _output.PrimitiveKind = source.PrimitiveKind;
        _output.PrimitiveClosed = source.PrimitiveClosed;
        _output.Parts = source.Parts;

        _output.Attributes.Clear();
        GeometryAttribute<float>? existing = null;
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain == attributeDomain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                existing = attribute as GeometryAttribute<float>;
                continue;
            }

            _output.Attributes.Add(attribute);
        }

        if (_selection == null || _selection.Domain != attributeDomain || !string.Equals(_selection.Name, name, StringComparison.Ordinal))
            _selection = new GeometryAttribute<float>(name, attributeDomain, count);

        _selection.Resize(count);
        var weights = _selection.Values;
        var evaluate = isSnippetReady ? _compiler.Delegate : null;
        for (var i = 0; i < count; i++)
        {
            float weight;
            if (mode == Modes.IndexPattern)
            {
                if (limit < 0)
                {
                    weight = ((i - offset) % every + every) % every == 0 ? 1 : 0;
                }
                else
                {
                    // A window of Count matches from Offset, wrapping past the last element
                    var distance = ((i - offset) % count + count) % count;
                    weight = distance % every == 0 && distance / every < limit ? 1 : 0;
                }
            }
            else
            {
                weight = evaluate != null ? evaluate(i, count, GeometryElements.GetPosition(source, attributeDomain, i), center, a, b) : 0;
            }

            if (clampResult)
                weight = Math.Clamp(weight, 0, 1);

            // Unclamped weights outside 0..1 pass through; the curve meets them continuously at 0 and 1
            if (applyGainAndBias && weight > 0 && weight < 1)
                weight = weight.ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);

            if (invert)
                weight = 1 - weight;

            var previous = existing != null && i < existing.Values.Length ? existing.Values[i] : 0;
            weights[i] = combine switch
                             {
                                 Combinations.Add       => MathF.Max(previous, weight),
                                 Combinations.Remove    => previous * (1 - weight),
                                 Combinations.Intersect => MathF.Min(previous, weight),
                                 _                      => weight,
                             };
        }

        _output.Attributes.Add(_selection);
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private static AttributeDomain ToAttributeDomain(Domains domain)
    {
        return domain switch
                   {
                       Domains.Points     => AttributeDomain.Point,
                       Domains.Vertices   => AttributeDomain.Vertex,
                       Domains.Primitives => AttributeDomain.Primitive,
                       _                  => AttributeDomain.Part,
                   };
    }

    /// <summary>
    /// The snippet may return a bool (selected or not) or a float weight; it is compiled as a bool method first and as a
    /// float method if that fails, so either reads naturally.
    /// </summary>
    /// <summary>Parses @attributes, recompiles when the code or their types changed and binds their values.</summary>
    private bool PrepareSnippet(string code, Geometry source, AttributeDomain domain)
    {
        if (code != _parsedCode)
        {
            _snippetAttributes.Parse(code);
            _parsedCode = code;
        }

        _attributeError = string.Empty;
        if (!_snippetAttributes.Resolve(source, domain, out var error))
        {
            // The last compile error belongs to earlier code; the missing attribute is the problem now
            _attributeError = error!;
            _statusMessage = string.Empty;
            _compiledCode = null;
            return false;
        }

        var signature = _snippetAttributes.GetTypeSignature();
        if (code != _compiledCode || signature != _compiledSignature)
        {
            _compiledSignature = signature;
            Compile(code);
        }

        return _snippetAttributes.Bind(source, domain);
    }

    private void Compile(string code)
    {
        _compiledCode = code;
        _statusMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
            return;

        var fields = _snippetAttributes.GetFieldDeclarations();
        var body = _snippetAttributes.Code;
        if (_compiler.TryCompile(BuildSource(body, fields, returnsBool: true), "__SelectGeometry", "Evaluate", "SelectGeometrySnippet", _compileErrors))
        {
            _snippetAttributes.SetCompiledType(_compiler.Delegate?.Method.DeclaringType);
            return;
        }

        _boolErrors.Clear();
        _boolErrors.AddRange(_compileErrors);
        if (_compiler.TryCompile(BuildSource(body, fields, returnsBool: false), "__SelectGeometry", "Evaluate", "SelectGeometrySnippet", _compileErrors))
        {
            _snippetAttributes.SetCompiledType(_compiler.Delegate?.Method.DeclaringType);
            return;
        }

        _snippetAttributes.SetCompiledType(null);

        // Report whichever attempt failed less, which is the one matching what the snippet returns
        var errors = _boolErrors.Count < _compileErrors.Count ? _boolErrors : _compileErrors;
        _compileErrorLine = _compiler.FirstErrorLine;
        _statusMessage = errors.Count > 0 ? $"Snippet {errors[0]}" : "Snippet failed to compile";
        foreach (var error in errors)
            Log.Warning($"SelectGeometry {error}", this);
    }

    private static string BuildSource(string code, string fields, bool returnsBool)
    {
        var returnType = returnsBool ? "bool" : "float";
        var wrapper = returnsBool ? "Snippet(i, count, p, Center, A, B) ? 1f : 0f" : "Snippet(i, count, p, Center, A, B)";
        return $$"""
                 using System;
                 using System.Numerics;
                 using static System.MathF;

                 public static class __SelectGeometry
                 {
                 {{fields}}
                     public static float Evaluate(int i, int count, Vector3 p, Vector3 Center, float A, float B) => {{wrapper}};

                     private static {{returnType}} Snippet(int i, int count, Vector3 p, Vector3 Center, float A, float B)
                     {
                 #line 1
                 {{code}}
                     }
                 }
                 """;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(GetStatusMessage()) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    bool IInputStatusProvider.TryGetInputStatus(Guid inputId, out InputStatus status)
    {
        status = default;
        if (inputId != Code.Input.InputDefinition.Id || Mode.Value != (int)Modes.Custom)
            return false;

        var message = GetStatusMessage();
        if (message.Length > 0)
        {
            status = new InputStatus(message, _attributeError.Length > 0 ? 0 : _compileErrorLine);
            return true;
        }

        // No problem: say which attributes come from another domain
        status = new InputStatus(_snippetAttributes.ConversionNote, IsError: false);
        return status.Message.Length > 0;
    }

    public string GetStatusMessage() => Mode.Value == (int)Modes.Custom && _attributeError.Length > 0 ? _attributeError : _statusMessage;

    private delegate float SnippetFn(int i, int count, Vector3 p, Vector3 center, float a, float b);

    private enum Domains
    {
        Points,
        Vertices,
        Primitives,
        Parts,
    }

    private enum Modes
    {
        IndexPattern,
        Custom,
    }

    private enum Combinations
    {
        Replace,
        Add,
        Remove,
        Intersect,
    }

    private readonly Geometry _output = new();
    private readonly CSharpSnippetCompiler<SnippetFn> _compiler = new();
    private readonly List<string> _compileErrors = [];
    private readonly List<string> _boolErrors = [];
    private GeometryAttribute<float>? _selection;
    private string? _compiledCode;
    private string? _parsedCode;
    private int _compiledSignature;
    private string _attributeError = string.Empty;
    private int _compileErrorLine;
    private readonly GeometrySnippetAttributes _snippetAttributes = new();
    private string _statusMessage = string.Empty;

    [Input(Guid = "10342312-f3b9-4a53-9413-ab358860eed4")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "427919c7-6b02-4009-a9cc-938986ad1053", MappedType = typeof(Domains))]
    public readonly InputSlot<int> Domain = new();

    [Input(Guid = "d82c7cd6-f36b-4ca9-97a7-db120670e5c1", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "7b777d3d-1bec-4297-94c3-f7450a162aeb")]
    public readonly InputSlot<int> Every = new();

    [Input(Guid = "4dba7742-653a-4ad5-9e21-0ea557865bcd")]
    public readonly InputSlot<int> Offset = new();

    [Input(Guid = "7bac9932-a892-4d15-909d-ded6f6c90564")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "2f24c52a-0a48-4c4e-a359-0b6012016270")]
    public readonly InputSlot<string> Code = new();

    [Input(Guid = "959c2660-ea9c-4c3a-8f00-d4b571d777a6")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "738d7eae-2dfa-4c56-a85d-a25c00143f6e")]
    public readonly InputSlot<float> B = new();

    [Input(Guid = "3adc4760-5ba6-46ec-b8c1-2ad4ec3d25cc")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "b481e8e6-3730-4253-88ee-c04ea20184bd")]
    public readonly InputSlot<bool> ClampResult = new();

    [Input(Guid = "53192fa7-eeec-462f-9ed8-c56d90d4fd82")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "952ea8f6-51f4-46b8-94af-83dda89159b0")]
    public readonly InputSlot<bool> Invert = new();

    [Input(Guid = "ec982690-0ef4-4b25-83d8-ddddb2c2d24e", MappedType = typeof(Combinations))]
    public readonly InputSlot<int> Combine = new();

    [Input(Guid = "6355884b-d2a9-4892-9698-944c8ab3a0de")]
    public readonly InputSlot<string> Name = new();
}
