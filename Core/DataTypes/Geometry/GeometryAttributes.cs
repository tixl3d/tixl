#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// The element domain an attribute's values are attached to. <see cref="Geometry"/> uses Point, Vertex, Primitive,
/// Edge and Part; Corner and Face remain for <see cref="MeshGeometry"/> until it is retired. ControlPoint, Segment and
/// Contour belonged to the retired curve type and are unused; values are appended so stored ints stay valid.
/// </summary>
public enum AttributeDomain
{
    Point,
    Corner,
    Edge,
    Face,
    Part,
    ControlPoint,
    Segment,
    Contour,
    Vertex,
    Primitive,
}

/// <summary>
/// How an attribute's values behave under transforms, so deform ops can treat every attribute correctly without
/// knowing its name.
/// </summary>
public enum AttributeTypeInfos
{
    /// <summary>Not transformed (ids, weights, UVs, selections).</summary>
    Value,

    /// <summary>A location: translated, rotated and scaled like the points.</summary>
    Position,

    /// <summary>A direction or offset with length: rotated and scaled, not translated (bezier handles, velocity).</summary>
    Vector,

    /// <summary>A surface normal: rotated and scaled by the inverse transpose, then normalized.</summary>
    Normal,

    /// <summary>An orientation as a quaternion (X, Y, Z, W): rotated, not scaled.</summary>
    Quaternion,

    /// <summary>A color: never transformed, but shown and blended as one.</summary>
    Color,
}

/// <summary>Well-known attribute names, so producers and consumers agree without magic strings.</summary>
public static class GeometryAttributeNames
{
    public const string Normal = "Normal";
    public const string TexCoord = "TexCoord";
    public const string TexCoord2 = "TexCoord2";
    public const string Color = "Color";
    public const string Selection = "Selection";

    /// <summary>Face domain, 1 on faces created by a cut (e.g. fracture caps), 0 on original surface.</summary>
    public const string IsCut = "IsCut";

    /// <summary>Part domain, int: index of the point a part was instanced at, so per-point data can be joined back later.</summary>
    public const string SourcePoint = "SourcePoint";

    /// <summary>Primitive (or Face) domain, 1 on extrusion walls and bevels ([ExtrudeCurves]), 0 on caps.</summary>
    public const string IsSide = "IsSide";

    /// <summary>Point domain, vec3: a curve point's incoming bezier handle, as an offset from the point.</summary>
    public const string HandleIn = "HandleIn";

    /// <summary>Point domain, vec3: a curve point's outgoing bezier handle, as an offset from the point.</summary>
    public const string HandleOut = "HandleOut";

    /// <summary>Point domain, vec4 quaternion: a point's rotation, matching <c>Point.Orientation</c>.</summary>
    public const string Orientation = "Orientation";

    /// <summary>Point domain, vec3: a point's scale, matching <c>Point.Scale</c>.</summary>
    public const string Scale = "Scale";

    /// <summary>The type info an attribute gets when its creator doesn't specify one.</summary>
    public static AttributeTypeInfos GetDefaultTypeInfo(string name)
    {
        if (Matches(name, Normal))
            return AttributeTypeInfos.Normal;

        if (Matches(name, HandleIn) || Matches(name, HandleOut))
            return AttributeTypeInfos.Vector;

        if (Matches(name, Orientation))
            return AttributeTypeInfos.Quaternion;

        if (Matches(name, Color))
            return AttributeTypeInfos.Color;

        return AttributeTypeInfos.Value;
    }

    private static bool Matches(string name, string wellKnownName) => string.Equals(name, wellKnownName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Attribute names produced by text layout ([StringToCurves]); all on the Part (glyph) domain.</summary>
public static class CurveAttributeNames
{
    public const string CodePoint = "CodePoint";
    public const string GlyphId = "GlyphId";
    public const string CharIndex = "CharIndex";
    public const string WordIndex = "WordIndex";
    public const string LineIndex = "LineIndex";

    /// <summary>float: the glyph's layout width in scene units.</summary>
    public const string Advance = "Advance";
}

/// <summary>
/// One named, typed, dense value buffer attached to a geometry domain.
/// Values.Length must equal the geometry's element count for the domain.
/// </summary>
public abstract class GeometryAttribute
{
    /// <param name="typeInfo">Null takes the default for well-known names, see <see cref="GeometryAttributeNames.GetDefaultTypeInfo"/>.</param>
    protected GeometryAttribute(string name, AttributeDomain domain, AttributeTypeInfos? typeInfo)
    {
        Name = name;
        Domain = domain;
        TypeInfo = typeInfo ?? GeometryAttributeNames.GetDefaultTypeInfo(name);
    }

    public string Name { get; }
    public AttributeDomain Domain { get; }
    public AttributeTypeInfos TypeInfo { get; }
    public abstract Type ValueType { get; }
    public abstract int Count { get; }
    public abstract void Resize(int count);
}

public sealed class GeometryAttribute<T> : GeometryAttribute where T : unmanaged
{
    public GeometryAttribute(string name, AttributeDomain domain, int count, AttributeTypeInfos? typeInfo = null)
        : base(name, domain, typeInfo)
    {
        Values = count == 0 ? Array.Empty<T>() : new T[count];
    }

    /// <summary>Dense values, one per domain element. Hot loops should take this once, not per element.</summary>
    public T[] Values;

    public override Type ValueType => typeof(T);
    public override int Count => Values.Length;

    public override void Resize(int count)
    {
        if (Values.Length != count)
            Array.Resize(ref Values, count);
    }
}

/// <summary>
/// The attribute table of a geometry: named typed buffers over element domains.
/// Lookups are meant to happen once per operation, outside hot loops.
/// </summary>
public sealed class GeometryAttributes : IEnumerable<GeometryAttribute>
{
    /// <param name="typeInfo">Used only when the attribute is created; null takes the default for the name.</param>
    public GeometryAttribute<T> GetOrCreate<T>(string name, AttributeDomain domain, int count, AttributeTypeInfos? typeInfo = null)
        where T : unmanaged
    {
        if (TryGet<T>(name, domain, out var existing))
        {
            existing.Resize(count);
            return existing;
        }

        var attribute = new GeometryAttribute<T>(name, domain, count, typeInfo);
        _attributes.Add(attribute);
        return attribute;
    }

    public bool TryGet<T>(string name, AttributeDomain domain, out GeometryAttribute<T> attribute) where T : unmanaged
    {
        foreach (var candidate in _attributes)
        {
            if (candidate.Domain != domain || candidate is not GeometryAttribute<T> typed)
                continue;

            if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            attribute = typed;
            return true;
        }

        attribute = null!;
        return false;
    }

    /// <summary>
    /// Adds an existing attribute, typically to share an unmodified buffer with another
    /// geometry (buffers flowing through the graph are immutable by convention).
    /// </summary>
    public void Add(GeometryAttribute attribute)
    {
        Remove(attribute.Name, attribute.Domain);
        _attributes.Add(attribute);
    }

    public void Remove(string name, AttributeDomain domain)
    {
        _attributes.RemoveAll(a => a.Domain == domain && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear() => _attributes.Clear();
    public int Count => _attributes.Count;

    /// <summary>Allocation-free access for per-frame readers; <c>foreach</c> boxes the enumerator.</summary>
    public GeometryAttribute this[int index] => _attributes[index];

    public IEnumerator<GeometryAttribute> GetEnumerator() => _attributes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private readonly List<GeometryAttribute> _attributes = [];
}
