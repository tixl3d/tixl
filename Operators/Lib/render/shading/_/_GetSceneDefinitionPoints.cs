namespace Lib.render.shading._;

[Guid("5b127401-600c-4247-9d59-2f6ff359ba85")]
internal sealed class _GetSceneDefinitionPoints : Instance<_GetSceneDefinitionPoints>
{
    // [Output(Guid = "8a1e3bc8-a7bd-40b5-a4cf-241c13bddbfb")]
    // public readonly Slot<Command> Output = new();

    [Output(Guid = "11DA2373-6F05-4818-8616-4F2C33F63301")]
    public readonly Slot<BufferWithViews> Points = new();

    [Output(Guid = "D9C04756-8922-496D-8380-120F280EF65B")]
    public readonly Slot<StructuredList> ResultList = new();

    [Output(Guid = "CB0E5B54-1C68-43A6-9101-2C9BC9B67C51")]
    public readonly Slot<BufferWithViews> ChunkDefsBuffer = new();

    public _GetSceneDefinitionPoints()
    {
        Points.UpdateAction = Update;
        ChunkDefsBuffer.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var sceneDefinition = SceneSetup.GetValue(context);
        if (sceneDefinition == null)
            return;

        sceneDefinition.GenerateSceneDrawDispatches();

        var dispatchesCount = sceneDefinition.Dispatches.Count;
        if (dispatchesCount == 0)
            return;

        if (dispatchesCount != _pointList.NumElements)
        {
            _pointList = new StructuredList<Point>(dispatchesCount);
        }

        if (_chunkIndices.Length != dispatchesCount)
        {
            _chunkIndices = new int[dispatchesCount];
        }

        for (var index = 0; index < dispatchesCount; index++)
        {
            var sceneDispatch = sceneDefinition.Dispatches[index];
            var matrix = sceneDispatch.CombinedTransform;
            Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation);
            _pointList.TypedElements[index].Position = translation;
            _pointList.TypedElements[index].F1 = 1.0f;
            _pointList.TypedElements[index].Orientation = rotation;
            _pointList.TypedElements[index].Color = Vector4.One;
            _pointList.TypedElements[index].Scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
            _pointList.TypedElements[index].F2 = 1.0f;

            _chunkIndices[index] = sceneDispatch.ChunkIndex;
        }

        ResourceManager.SetupStructuredBuffer(_chunkIndices, dispatchesCount * 4, 4, ref _chunksDefBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_chunksDefBuffer.Buffer, ref _chunksDefBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_chunksDefBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                  ref _chunksDefBuffer.Uav);

        ResourceManager.SetupStructuredBuffer(_pointList.TypedElements, _pointList.ElementSizeInBytes * _pointList.NumElements, _pointList.ElementSizeInBytes,
                                              ref _pointsBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_pointsBuffer.Buffer, ref _pointsBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_pointsBuffer.Buffer, UnorderedAccessViewBufferFlags.None,
                                                  ref _pointsBuffer.Uav);

        ChunkDefsBuffer.Value = _chunksDefBuffer;
        Points.Value = _pointsBuffer;

        ChunkDefsBuffer.DirtyFlag.Clear();
        Points.DirtyFlag.Clear();
    }

    private StructuredList<Point> _pointList = new(1);
    private int[] _chunkIndices = [];
    private BufferWithViews _chunksDefBuffer = new();
    private BufferWithViews _pointsBuffer = new();

    [Input(Guid = "41054d35-5564-42db-9109-263f8c447057")]
    public readonly InputSlot<SceneSetup> SceneSetup = new();
}