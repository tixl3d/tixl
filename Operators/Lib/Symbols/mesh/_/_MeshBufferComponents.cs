namespace Lib.mesh.@_;

[Guid("5b9f1d97-4e10-4f31-ba83-4cbf7be9719b")]
internal sealed class _MeshBufferComponents : Instance<_MeshBufferComponents>, IStatusProvider
{
    [Output(Guid = "0C5E2EC1-AB60-43CE-B823-3DF096FF9A28")]
    public readonly Slot<BufferWithViews> Vertices = new();

    [Output(Guid = "78C53086-BB28-4C58-8B51-42CFDF6620C4")]
    public readonly Slot<BufferWithViews> Indices = new();
        
        
    [Output(Guid = "8FEF2E09-4F1E-4BA8-8D62-858C3FB0AC23")]
    public readonly Slot<BufferWithViews> ChunkDefs = new();


    public _MeshBufferComponents()
    {
        Vertices.UpdateAction = Update;
        Indices.UpdateAction = Update;
        ChunkDefs.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var mesh = MeshBuffers.GetValue(context);
        Vertices.Value = null;
        Indices.Value = null;
        ChunkDefs.Value = null;

        if (mesh == null)
        {
            _lastError = "Undefined Mesh?";
            return;
        }

        if (mesh.VertexBuffer?.Buffer == null || mesh.VertexBuffer.Srv == null)
        {
            _lastError = "Vertex buffer undefined";
            return;
        }

        if (mesh.IndicesBuffer?.Buffer == null || mesh.IndicesBuffer.Srv == null)
        {
            _lastError = "Indices buffer undefined";
            return;
        }
            
            
        _lastError = null;
            
        Vertices.Value = mesh.VertexBuffer;
        Indices.Value = mesh.IndicesBuffer;
        ChunkDefs.Value = mesh.ChunkDefsBuffer;
            
        Vertices.DirtyFlag.Clear();
        Indices.DirtyFlag.Clear();
        ChunkDefs.DirtyFlag.Clear();
    }

    [Input(Guid = "1B0B7587-DE86-4FC4-BE78-A21392E8AA9B")]
    public readonly InputSlot<MeshBuffers> MeshBuffers = new();

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastError) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastError;
    }

    private string _lastError;
}