using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// GPU timestamp queries, used by <c>GpuMeasure</c> to report how long a subtree took on the GPU.
/// </summary>
/// <remarks>
/// The backend API has no query support yet, so a query never reports data and the operator shows no timing
/// rather than a wrong one. Timestamps are cheap to add to both backends — D3D11 has these queries and Vulkan
/// has query pools — but they are not part of getting a picture on screen.
/// </remarks>
public sealed class Query(Device device, QueryDescription description) : Resource(device)
{
    public QueryDescription Description { get; } = description;
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

public struct QueryDescription
{
    public QueryType Type;
    public QueryFlags Flags;
}

[Flags]
public enum QueryFlags
{
    None = 0,
    Predicatehint = 1,
}

[Flags]
public enum AsynchronousFlags
{
    None = 0,
    DoNotFlush = 1,
}

/// <summary>What a disjoint timestamp query reports: the tick rate, and whether the timestamps are usable.</summary>
public struct QueryDataTimestampDisjoint
{
    public long Frequency;
    public bool Disjoint;
}

/// <summary>A box inside a resource, in texels.</summary>
public struct ResourceRegion(int left, int top, int front, int right, int bottom, int back)
{
    public int Left = left;
    public int Top = top;
    public int Front = front;
    public int Right = right;
    public int Bottom = bottom;
    public int Back = back;
}

/// <summary>
/// The vertex layout as D3D11 spells it. The facade turns these into the backend's vertex attributes, where
/// the semantic stays a string because that is what the shader's reflection matches against.
/// </summary>
public readonly struct InputElement(string semanticName, int semanticIndex, Format format, int alignedByteOffset, int slot,
                                    InputClassification classification = InputClassification.PerVertexData, int instanceDataStepRate = 0)
{
    public readonly string SemanticName = semanticName;
    public readonly int SemanticIndex = semanticIndex;
    public readonly Format Format = format;
    public readonly int AlignedByteOffset = alignedByteOffset;
    public readonly int Slot = slot;
    public readonly InputClassification Classification = classification;
    public readonly int InstanceDataStepRate = instanceDataStepRate;

    public InputElement(string semanticName, int semanticIndex, Format format, int slot)
        : this(semanticName, semanticIndex, format, 0, slot)
    {
    }

    internal VertexAttribute ToAttribute()
        => new()
               {
                   Semantic = SemanticName + SemanticIndex,
                   Buffer = Slot,
                   Offset = AlignedByteOffset,
                   Format = Format,
                   Rate = Classification == InputClassification.PerInstanceData ? VertexInputRate.PerInstance : VertexInputRate.PerVertex,
               };
}
