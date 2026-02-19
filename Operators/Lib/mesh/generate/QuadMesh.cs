using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;


namespace Lib.mesh.generate;

[Guid("9d6dbf28-9983-4584-abba-6281ce51d583")]
internal sealed class QuadMesh : Instance<QuadMesh>
{
    [Output(Guid = "9c86f704-a28f-4d2a-b7c0-15648f982462")]
    public readonly Slot<MeshBuffers> Data = new();

    public QuadMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var scale = Scale.GetValue(context);
            var stretch = Stretch.GetValue(context);
            var pivot = Pivot.GetValue(context);
            var rotation = Rotation.GetValue(context);
                
            float yaw = rotation.Y.ToRadians();
            float pitch = rotation.X.ToRadians();
            float roll = rotation.Z.ToRadians();

            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
                
            //var offset = 
                
            var center = Center.GetValue(context);
            //var centerRotated = SharpDX.Vector3.Transform(new SharpDX.Vector3(center.X, center.Y, center.Z), rotationMatrix);
            var offset = new Vector3(stretch.X * scale * (pivot.X - 0.5f),
                                     stretch.Y * scale * (pivot.Y - 0.5f),
                                     0);

            var center2 = new Vector3(center.X, center.Y, center.Z);

            var segments = Segments.GetValue(context);
            var columns = segments.Width.Clamp(1, 10000) + 1;
            var rows = segments.Height.Clamp(1, 10000) + 1;
                
            var faceCount = (columns -1)  * (rows - 1) * 2;
            var verticesCount = columns * rows;

            // Create buffers
            if (_vertexBufferData.Length != verticesCount)
                _vertexBufferData = new PbrVertex[verticesCount];
                
            if (_indexBufferData.Length != faceCount)
                _indexBufferData = new Int3[faceCount];

            double columnStep = (scale * stretch.X) / (columns-1);  
            double rowStep = (scale * stretch.Y) / (rows-1);

            // var normal = SharpDX.Vector3.ForwardRH;
            // var tangent = SharpDX.Vector3.Right;
            // var binormal = SharpDX.Vector3.Up;

            var normal = Vector3.TransformNormal(VectorT3.ForwardLH, rotationMatrix);
            var tangent = Vector3.TransformNormal(VectorT3.Right, rotationMatrix);
            var binormal = Vector3.TransformNormal(VectorT3.Up, rotationMatrix);

            // Initialize
            for (int columnIndex = 0; columnIndex < columns; ++columnIndex)
            {
                var columnFragment = (float)( columnIndex * columnStep); 
                    
                var u0 = columnIndex / ((float)columns-1);
                //var v1 = (columnIndex + 1) / (float)columns;

                for (int rowIndex = 0; rowIndex < rows; ++rowIndex)
                {
                    var rowFragment = (float)( rowIndex * rowStep);
                    //var rowFragment = ((float)rowIndex / rows - pivot.Y) * stretch.Y;
                        
                    var vertexIndex = rowIndex + columnIndex * rows;
                    var faceIndex =  2 * (rowIndex + columnIndex * (rows-1));
                        
                        
                    var p = new Vector3(columnFragment,
                                        rowFragment, 
                                        0);
                        
                    var v0 = (rowIndex ) / ((float)rows-1);
                    var uv0 = new Vector2(u0, v0);
                    _vertexBufferData[vertexIndex + 0] = new PbrVertex
                                                             {
                                                                 Position = Vector3.TransformNormal(p + offset, rotationMatrix) + center2,
                                                                 Normal = normal,
                                                                 Tangent = tangent,
                                                                 Bitangent = binormal,
                                                                 Texcoord = uv0,
                                                                 Selection = 1,
                                                                 ColorRgb = Vector3.One
                                                             };

                    if (columnIndex >= columns - 1 || rowIndex >= rows - 1)
                        continue;
                        
                    _indexBufferData[faceIndex + 0] = new Int3(vertexIndex, vertexIndex + rows, vertexIndex + 1);
                    _indexBufferData[faceIndex + 1] = new Int3(vertexIndex + rows, vertexIndex + rows+1, vertexIndex + 1);
                }
            }
                
            // Write Data
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;
                
            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * faceCount, stride, ref _indexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
            _indexBufferWithViews.Buffer = _indexBuffer;

            _data.VertexBuffer = _vertexBufferWithViews;
            _data.IndicesBuffer = _indexBufferWithViews;
            Data.Value = _data;
            Data.DirtyFlag.Clear();
        }
        catch (Exception e)
        {
            Log.Error("Failed to create torus mesh:" + e.Message);
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();

    [Input(Guid = "18a5f3be-92a7-438c-b32b-e0da7c7a5736")]
    public readonly InputSlot<Int2> Segments = new();
        
    [Input(Guid = "0295DD65-95B4-4E02-8D61-4622F59D4FC4")]
    public readonly InputSlot<Vector2> Stretch = new();
        
    [Input(Guid = "44fc1e7b-b1d1-4199-b373-8b7c4cc060d2")]
    public readonly InputSlot<float> Scale = new();
        
    [Input(Guid = "7F01D9B9-C612-4A2D-A52F-B56C54FB62AF")]
    public readonly InputSlot<Vector2> Pivot = new();
        
    [Input(Guid = "B2A1C96A-4AEF-412A-B006-2EF285DD2479")]
    public readonly InputSlot<Vector3> Center = new();
        
    [Input(Guid = "A89E41BF-5395-41F0-9804-A782ED4C0F30")]
    public readonly InputSlot<Vector3> Rotation = new();
        
}