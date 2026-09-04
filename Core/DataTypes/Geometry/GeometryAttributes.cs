#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// The element domain an attribute's values are attached to. Mesh domains first;
/// curve domains (ControlPoint/Segment/Contour) are reserved for CurveGeometry.
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

    /// <summary>Face domain, 1 on extrusion walls and bevels ([CurvesToMesh]), 0 on caps.</summary>
    public const string IsSide = "IsSide";
}

/// <summary>Attribute names produced by text layout ([TextToCurves]); all on the Part (glyph) domain.</summary>
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
    protected GeometryAttribute(string name, AttributeDomain domain)
    {
        Name = name;
        Domain = domain;
    }

    public string Name { get; }
    public AttributeDomain Domain { get; }
    public abstract Type ValueType { get; }
    public abstract int Count { get; }
    public abstract void Resize(int count);
}

public sealed class GeometryAttribute<T> : GeometryAttribute where T : unmanaged
{
    public GeometryAttribute(string name, AttributeDomain domain, int count)
        : base(name, domain)
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
    public GeometryAttribute<T> GetOrCreate<T>(string name, AttributeDomain domain, int count) where T : unmanaged
    {
        if (TryGet<T>(name, domain, out var existing))
        {
            existing.Resize(count);
            return existing;
        }

        var attribute = new GeometryAttribute<T>(name, domain, count);
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

    public IEnumerator<GeometryAttribute> GetEnumerator() => _attributes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private readonly List<GeometryAttribute> _attributes = [];
}
