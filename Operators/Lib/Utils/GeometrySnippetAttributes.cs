#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Lets the C# snippets of geometry ops read attributes as <c>@Name</c> (Houdini style). Each name becomes a typed
/// array field of the generated class, indexed by the element <c>i</c>; <c>@P</c> is the element position <c>p</c>.
/// An attribute on the op's domain is passed as is, a point attribute is converted (a vertex takes its point, a
/// primitive or part the mean of its points). The snippet recompiles only when the code or an attribute's type changes.
/// </summary>
internal sealed partial class GeometrySnippetAttributes
{
    /// <summary>The code with every <c>@Name</c> replaced; valid after <see cref="Parse"/>.</summary>
    public string Code { get; private set; } = string.Empty;

    public void Parse(string code)
    {
        _names.Clear();
        Code = AttributePattern().Replace(code, match =>
                                                {
                                                    var name = match.Groups[1].Value;
                                                    if (name == "P")
                                                        return "p";

                                                    var index = _names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                                                    if (index < 0)
                                                    {
                                                        index = _names.Count;
                                                        _names.Add(name);
                                                    }

                                                    return $"{FieldPrefix}{index}[i]";
                                                });

        while (_current.Count < _names.Count)
            _current.Add(default);
    }

    /// <summary>Finds the referenced attributes on <paramref name="geometry"/>.</summary>
    /// <returns>False with <paramref name="error"/> when one is missing or not numeric.</returns>
    public bool Resolve(Geometry geometry, AttributeDomain domain, out string? error)
    {
        error = null;
        for (var index = 0; index < _names.Count; index++)
        {
            var name = _names[index];
            var attribute = Find(geometry.Attributes, name, domain) ?? (domain != AttributeDomain.Point ? Find(geometry.Attributes, name, AttributeDomain.Point) : null);
            if (attribute == null)
            {
                error = GetMissingMessage(geometry, domain, name);
                return false;
            }

            if (GetTypeName(attribute) == null)
            {
                error = $"'@{name}' is a {attribute.ValueType.Name}; snippets read float, int and vector attributes.";
                return false;
            }

            _current[index] = attribute;
        }

        return true;
    }

    /// <summary>Identifies the referenced attributes' types, so a failed compile is only retried after they change.</summary>
    public int GetTypeSignature()
    {
        var hash = _names.Count;
        for (var index = 0; index < _names.Count; index++)
            hash = HashCode.Combine(hash, _current[index]?.ValueType);

        return hash;
    }

    /// <summary>True when the referenced attributes' types differ from the compiled snippet's fields.</summary>
    public bool NeedsCompile()
    {
        if (_compiledTypes.Count != _names.Count)
            return true;

        for (var index = 0; index < _names.Count; index++)
        {
            if (_compiledTypes[index] != _current[index]!.ValueType)
                return true;
        }

        return false;
    }

    /// <summary>Field declarations for the generated class, after a successful <see cref="Resolve"/>.</summary>
    public string GetFieldDeclarations()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < _names.Count; index++)
        {
            var typeName = GetTypeName(_current[index]!);
            builder.Append($"    public static {typeName}[] {FieldPrefix}{index} = Array.Empty<{typeName}>();\n");
        }

        return builder.ToString();
    }

    /// <summary>Records the fields of the class that compiled; null when compiling failed.</summary>
    public void SetCompiledType(Type? compiledType)
    {
        // A failed compile keeps the previous delegate; it must not run against fields it doesn't have
        _isDisabled = compiledType == null && (_fields.Count > 0 || _names.Count > 0);
        _compiledTypes.Clear();
        _fields.Clear();
        if (compiledType == null)
            return;

        for (var index = 0; index < _names.Count; index++)
        {
            _compiledTypes.Add(_current[index]!.ValueType);
            _fields.Add(compiledType.GetField(FieldPrefix + index, BindingFlags.Public | BindingFlags.Static)!);
        }
    }

    /// <summary>Hands the attribute values of the last <see cref="Resolve"/> to the compiled snippet.</summary>
    /// <returns>False when the compiled snippet doesn't match the attributes, e.g. after a failed recompile.</returns>
    public bool Bind(Geometry geometry, AttributeDomain domain)
    {
        if (_isDisabled || NeedsCompile() || _fields.Count != _names.Count)
            return false;

        while (_buffers.Count < _names.Count)
            _buffers.Add(null);

        for (var index = 0; index < _names.Count; index++)
        {
            var attribute = _current[index]!;
            var values = attribute.Domain == domain ? GetValues(attribute) : ConvertFromPoints(geometry, domain, attribute, index);
            _fields[index].SetValue(null, values);
        }

        return true;
    }

    private Array ConvertFromPoints(Geometry geometry, AttributeDomain domain, GeometryAttribute attribute, int index)
    {
        var count = GeometryElements.GetCount(geometry, domain);
        var buffer = _buffers[index];
        if (buffer == null || buffer.Length != count || buffer.GetType().GetElementType() != attribute.ValueType)
            _buffers[index] = buffer = Array.CreateInstance(attribute.ValueType, count);

        for (var element = 0; element < count; element++)
        {
            var sum = Vector4.Zero;
            var pointCount = 0;
            switch (domain)
            {
                case AttributeDomain.Vertex:
                    sum = Read(attribute, geometry.VertexPoints[element]);
                    pointCount = 1;
                    break;
                case AttributeDomain.Primitive:
                    AddPrimitivePoints(geometry, attribute, element, ref sum, ref pointCount);
                    break;
                case AttributeDomain.Part:
                    var part = geometry.Parts[element];
                    for (var primitive = part.PrimitiveStart; primitive < part.PrimitiveStart + part.PrimitiveCount; primitive++)
                        AddPrimitivePoints(geometry, attribute, primitive, ref sum, ref pointCount);

                    break;
            }

            Write(buffer, element, pointCount > 0 ? sum / pointCount : Vector4.Zero);
        }

        return buffer;
    }

    private static void AddPrimitivePoints(Geometry geometry, GeometryAttribute attribute, int primitive, ref Vector4 sum, ref int pointCount)
    {
        for (var vertex = geometry.PrimitiveVertexOffsets[primitive]; vertex < geometry.PrimitiveVertexOffsets[primitive + 1]; vertex++)
        {
            sum += Read(attribute, geometry.VertexPoints[vertex]);
            pointCount++;
        }
    }

    private static Vector4 Read(GeometryAttribute attribute, int index)
    {
        return attribute switch
                   {
                       GeometryAttribute<float> floats    => new Vector4(floats.Values[index], 0, 0, 0),
                       GeometryAttribute<int> ints        => new Vector4(ints.Values[index], 0, 0, 0),
                       GeometryAttribute<Vector2> vectors => new Vector4(vectors.Values[index], 0, 0),
                       GeometryAttribute<Vector3> vectors => new Vector4(vectors.Values[index], 0),
                       GeometryAttribute<Vector4> vectors => vectors.Values[index],
                       _                                  => Vector4.Zero,
                   };
    }

    private static void Write(Array buffer, int index, Vector4 value)
    {
        switch (buffer)
        {
            case float[] floats:
                floats[index] = value.X;
                break;
            case int[] ints:
                ints[index] = (int)MathF.Round(value.X);
                break;
            case Vector2[] vectors:
                vectors[index] = new Vector2(value.X, value.Y);
                break;
            case Vector3[] vectors:
                vectors[index] = new Vector3(value.X, value.Y, value.Z);
                break;
            case Vector4[] vectors:
                vectors[index] = value;
                break;
        }
    }

    private static Array GetValues(GeometryAttribute attribute)
    {
        return attribute switch
                   {
                       GeometryAttribute<float> floats    => floats.Values,
                       GeometryAttribute<int> ints        => ints.Values,
                       GeometryAttribute<Vector2> vectors => vectors.Values,
                       GeometryAttribute<Vector3> vectors => vectors.Values,
                       GeometryAttribute<Vector4> vectors => vectors.Values,
                       _                                  => Array.Empty<float>(),
                   };
    }

    private static string? GetTypeName(GeometryAttribute attribute)
    {
        return attribute switch
                   {
                       GeometryAttribute<float>   => "float",
                       GeometryAttribute<int>     => "int",
                       GeometryAttribute<Vector2> => "Vector2",
                       GeometryAttribute<Vector3> => "Vector3",
                       GeometryAttribute<Vector4> => "Vector4",
                       _                          => null,
                   };
    }

    /** Names what is available instead; cached while the geometry and the name stay the same. */
    private string GetMissingMessage(Geometry geometry, AttributeDomain domain, string name)
    {
        if (ReferenceEquals(geometry, _missingGeometry) && geometry.Version == _missingVersion && domain == _missingDomain && name == _missingName)
            return _missingMessage;

        var builder = new StringBuilder($"No attribute @{name} on {GetDomainLabel(domain)}");
        builder.Append(domain == AttributeDomain.Point ? "." : " or points.");
        var count = 0;
        foreach (var available in GeometryAttributeChoices.GetAttributeNames(geometry, null, floatsOnly: false))
        {
            var attribute = Find(geometry.Attributes, available, domain) ?? Find(geometry.Attributes, available, AttributeDomain.Point);
            if (attribute == null || GetTypeName(attribute) == null)
                continue;

            builder.Append(count++ == 0 ? " Available: @" : ", @").Append(attribute.Name);
        }

        if (count == 0)
            builder.Append(" This geometry has none here; write one upstream, e.g. with [CustomGeometryAttribute] or [SelectGeometry].");

        _missingGeometry = geometry;
        _missingVersion = geometry.Version;
        _missingDomain = domain;
        _missingName = name;
        _missingMessage = builder.ToString();
        return _missingMessage;
    }

    private static string GetDomainLabel(AttributeDomain domain)
    {
        return domain switch
                   {
                       AttributeDomain.Point     => "points",
                       AttributeDomain.Vertex    => "vertices",
                       AttributeDomain.Primitive => "primitives",
                       AttributeDomain.Part      => "parts",
                       _                         => domain.ToString(),
                   };
    }

    private static GeometryAttribute? Find(GeometryAttributes attributes, string name, AttributeDomain domain)
    {
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain == domain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
                return attribute;
        }

        return null;
    }

    [GeneratedRegex(@"(?<![\w""])@([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex AttributePattern();

    private const string FieldPrefix = "__attribute";

    private readonly List<string> _names = [];
    private readonly List<GeometryAttribute?> _current = [];
    private readonly List<Type> _compiledTypes = [];
    private readonly List<FieldInfo> _fields = [];
    private readonly List<Array?> _buffers = [];
    private bool _isDisabled;
    private Geometry? _missingGeometry;
    private int _missingVersion;
    private AttributeDomain _missingDomain;
    private string? _missingName;
    private string _missingMessage = string.Empty;
}
