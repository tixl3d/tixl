using System.Numerics;
using Lib.physics;
using Lib.Utils;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Rendering;
using Xunit;

namespace Lib.Tests.Geometry;

public class MeshBuffersToGeometryTests
{
    [Fact]
    public void WeldingJoinsVerticesThatShareAPosition()
    {
        // Two triangles of a quad, each with its own three vertices, as flat-shaded meshes store them.
        PbrVertex[] vertices =
        [
            Vertex(0, 0, 0), Vertex(1, 0, 0), Vertex(1, 1, 0),
            Vertex(0, 0, 0), Vertex(1, 1, 0), Vertex(0, 1, 0),
        ];
        Int3[] triangles = [new(0, 1, 2), new(3, 4, 5)];
        var output = new MeshGeometry();
        var lookup = new Dictionary<Vector3, int>();

        Assert.True(MeshBuffersToGeometry.Build(vertices, triangles, [], weld: true, output, lookup));
        Assert.Equal(4, output.PointCount);
        Assert.Equal(2, output.FaceCount);
        Assert.Equal(output.CornerPointIndices[0], output.CornerPointIndices[3]);
        Assert.Empty(output.Parts);

        Assert.True(MeshBuffersToGeometry.Build(vertices, triangles, [], weld: false, output, lookup));
        Assert.Equal(6, output.PointCount);
    }

    [Fact]
    public void ChunksBecomeParts()
    {
        PbrVertex[] vertices =
        [
            Vertex(0, 0, 0), Vertex(1, 0, 0), Vertex(0, 1, 0),
            Vertex(4, 0, 0), Vertex(5, 0, 0), Vertex(4, 1, 0),
        ];
        Int3[] triangles = [new(0, 1, 2), new(3, 4, 5)];
        MeshChunkDef[] chunks =
        [
            new() { StartFaceIndex = 0, FaceCount = 1, StartVertexIndex = 0, VertexCount = 3 },
            new() { StartFaceIndex = 1, FaceCount = 1, StartVertexIndex = 3, VertexCount = 3 },
        ];
        var output = new MeshGeometry();

        Assert.True(MeshBuffersToGeometry.Build(vertices, triangles, chunks, weld: true, output, new Dictionary<Vector3, int>()));
        Assert.Equal(2, output.Parts.Length);
        Assert.Equal(1, output.Parts[1].FaceStart);
        Assert.Equal(new Vector3(4.5f, 0.5f, 0), output.Parts[1].Pivot);

        // The parts feed physics as two bodies.
        Assert.Equal(2, PhysicsGeometry.BuildTriangles(output).Length / 3);
    }

    [Fact]
    public void IndicesOutsideTheVerticesAreRejected()
    {
        PbrVertex[] vertices = [Vertex(0, 0, 0), Vertex(1, 0, 0), Vertex(0, 1, 0)];
        Assert.False(MeshBuffersToGeometry.Build(vertices, [new Int3(0, 1, 7)], [], weld: true, new MeshGeometry(), new Dictionary<Vector3, int>()));
    }

    private static PbrVertex Vertex(float x, float y, float z) => new() { Position = new Vector3(x, y, z), Normal = Vector3.UnitZ };
}
