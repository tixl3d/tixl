using System;

namespace T3.Core.DataTypes;

/// <summary>
/// Combines buffers required for mesh rendering
/// </summary>
public class MeshBuffers: IDisposable
{
    public BufferWithViews VertexBuffer = new ();
    public BufferWithViews IndicesBuffer = new ();
    public BufferWithViews ChunkDefsBuffer = new ();

    public override string ToString()
    {
        if(VertexBuffer?.Srv == null || IndicesBuffer?.Srv == null)
            return "Undefined";

        try
        {
            var vertexCount = VertexBuffer?.Srv != null
                                  ? VertexBuffer.Srv.Description.Buffer.ElementCount
                                  : 0;
            var indicesCount = IndicesBuffer?.Srv != null
                                   ? IndicesBuffer.Srv.Description.Buffer.ElementCount
                                   : 0;

            return $"{vertexCount} vertices {indicesCount} faces";
        }
        catch
        {
            return "???";
        }
    }

    public int FaceCount =>
        IndicesBuffer?.Srv != null
            ? IndicesBuffer.Srv.Description.Buffer.ElementCount
            : 0;

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndicesBuffer?.Dispose();
        ChunkDefsBuffer?.Dispose();
    }
}