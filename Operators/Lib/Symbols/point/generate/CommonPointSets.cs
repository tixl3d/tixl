//using SharpDX;

using T3.Core.Utils;

namespace Lib.point.generate;

[Guid("353f63fc-e613-43ca-b037-02d7b9f4e935")]
internal sealed class CommonPointSets : Instance<CommonPointSets>
{
    [Output(Guid = "E5DC2CD0-C57F-4E72-9452-E162FE1C37D5")]
    public readonly Slot<BufferWithViews> GpuBuffer = new();

    [Output(Guid = "2e45df97-e5c9-454d-b6ea-569c16cc04d5")]
    public readonly Slot<StructuredList> CpuBuffer = new();

    public CommonPointSets()
    {
        if (!_initialized)
            Init();

        CpuBuffer.UpdateAction += Update;
        GpuBuffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var bufferIndex = (int)Set.GetEnumValue<Shapes>(context);
        CpuBuffer.Value = _cpuPointBuffers[bufferIndex];
        GpuBuffer.Value = _gpuBuffersWithViews[bufferIndex];
    }

    private static void Init()
    {
        var bufferCount = Enum.GetNames(typeof(Shapes)).Length;

        _cpuPointBuffers = new StructuredList<Point>[bufferCount];
        _gpuBuffers = new Buffer[bufferCount];
        _gpuBuffersWithViews = new BufferWithViews[bufferCount];

        // Setup CPU Buffers
        for (var bufferIndex = 0; bufferIndex < bufferCount; bufferIndex++)
        {
            var definitionPoints = Definitions[bufferIndex];
            var tmpBuffer = new StructuredList<Point>(definitionPoints.Length);

            for (var pointIndex = 0; pointIndex < definitionPoints.Length; pointIndex++)
            {
                var p = definitionPoints[pointIndex];
                p.Orientation = Quaternion.Identity;
                p.Color = Vector4.One;
                tmpBuffer.TypedElements[pointIndex] = p;
            }

            _cpuPointBuffers[bufferIndex] = tmpBuffer;
        }

        // Initialize GPU Buffers
        try
        {
            for (var bufferIndex = 0; bufferIndex < bufferCount; bufferIndex++)
            {
                Buffer gpuBuffer = null;
                ShaderResourceView srv = null;
                UnorderedAccessView uav = null;

                var pointBuffer = _cpuPointBuffers[bufferIndex];

                ResourceManager.SetupStructuredBuffer(pointBuffer.TypedElements,
                                                      Point.Stride * pointBuffer.NumElements,
                                                      Point.Stride,
                                                      ref gpuBuffer);

                ResourceManager.CreateStructuredBufferSrv(gpuBuffer,
                                                          ref srv);
                ResourceManager.CreateStructuredBufferUav(gpuBuffer,
                                                          UnorderedAccessViewBufferFlags.None,
                                                          ref uav);

                _gpuBuffersWithViews[bufferIndex] = new BufferWithViews
                                                        {
                                                            Buffer = gpuBuffer,
                                                            Srv = srv,
                                                            Uav = uav
                                                        };
            }
        }
        catch (Exception e)
        {
            Log.Error("Failed to setup buffer:" + e);
        }

        _initialized = true;
    }

    private static Buffer[] _gpuBuffers;
    private static BufferWithViews[] _gpuBuffersWithViews;

    [Input(Guid = "2BA96AEE-FF89-41BD-90C5-C6C36907B6E4", MappedType = typeof(Shapes))]
    public readonly InputSlot<int> Set = new();

    private static StructuredList<Point>[] _cpuPointBuffers;

    //private static StructuredList<Point> _newBuffer;
    private static bool _initialized;

    private const float S = 0.5f;

    private static readonly Point[] CrossPoints =
        {
            new() { Position = new Vector3(0, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
            new() { Position = new Vector3(-S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(0, 0, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, 0, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] CrossXYPoints =
        {
            new() { Position = new Vector3(0, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] CubePoints =
        {
            new() { Position = new Vector3(-S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(S, -S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, -S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(S, S, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S, S, S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] QuadPoints =
        {
            new() { Position = new Vector3(-S, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(+S, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(+S, +S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, +S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(-S, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] ArrowXPoints =
        {
            new() { Position = new Vector3(-S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(+S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(S / 1.5f, -S / 4, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(+S, 0, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S / 1.5f, S / 4, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] ArrowYPoints =
        {
            new() { Position = new Vector3(0, -S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, +S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S / 4, S / 1.5f, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, +S, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S / 4, S / 1.5f, 0), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private static readonly Point[] ArrowZPoints =
        {
            new() { Position = new Vector3(0, 0, -S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, 0, +S), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 

            new() { Position = new Vector3(-S / 4, 0, S / 1.5f), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(0, 0, +S), F1 = 1, Color = Vector4.One, F2 = 1 },
            new() { Position = new Vector3(S / 4, 0, S / 1.5f), F1 = 1, Color = Vector4.One, F2 = 1 },
            Point.Separator(), 
        };

    private enum Shapes
    {
        Cross,
        CrossXY,
        Cube,
        Quad,
        ArrowX,
        ArrowY,
        ArrowZ,
    }

    private static readonly List<Point[]> Definitions = new()
                                                            {
                                                                CrossPoints,
                                                                CrossXYPoints,
                                                                CubePoints,
                                                                QuadPoints,
                                                                ArrowXPoints,
                                                                ArrowYPoints,
                                                                ArrowZPoints,
                                                            };
}