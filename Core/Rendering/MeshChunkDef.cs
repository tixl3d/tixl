using System.Runtime.InteropServices;

namespace T3.Core.Rendering;

/// <summary>
/// One contiguous range of a <see cref="MeshBuffers"/>: the unit that chunk-instancing
/// draws place at points. Face indices count triangles of the index buffer. Mirrors
/// the ChunkDef struct of the chunk shaders, so the layout is frozen at 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Stride)]
public struct MeshChunkDef
{
    [FieldOffset(0)]
    public int StartFaceIndex;

    [FieldOffset(4)]
    public int FaceCount;

    [FieldOffset(8)]
    public int StartVertexIndex;

    [FieldOffset(12)]
    public int VertexCount;

    public const int Stride = 16;
}
