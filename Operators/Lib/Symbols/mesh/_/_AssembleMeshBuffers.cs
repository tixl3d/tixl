namespace Lib.mesh.@_;

[Guid("e0849edd-ea1b-4657-b22d-5aa646318aa8")]
internal sealed class _AssembleMeshBuffers : Instance<_AssembleMeshBuffers>
{
    [Output(Guid = "D71893DD-6CA2-4AB7-9E04-0BD7285ECCFB")]
    public readonly Slot<MeshBuffers> NewMeshBuffers = new();
        
        
    public _AssembleMeshBuffers()
    {
        NewMeshBuffers.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PrepareCommand.HasInputConnections)
        {
            PrepareCommand.GetValue(context);
        }
        else
        {
            PrepareCommand.DirtyFlag.Clear();
        }
            
        var vertices = Vertices.GetValue(context);
        var indices = Indices.GetValue(context);
        var chunks = ChunkDefs.GetValue(context);
            
        if (vertices == null || indices == null)
        {
            NewMeshBuffers.Value = null;
            return;
        }

        _result.VertexBuffer = vertices;
        _result.IndicesBuffer = indices;
        _result.ChunkDefsBuffer = chunks;
        NewMeshBuffers.Value = _result;
    }

    private MeshBuffers _result = new();

        
    [Input(Guid = "BA53B274-62CA-40A2-B8D2-87D08F0BC259")]
    public readonly InputSlot<BufferWithViews> Vertices = new();
        
    [Input(Guid = "892838C5-FA5A-418E-81D6-A3A523819324")]
    public readonly InputSlot<BufferWithViews> Indices = new();
    
    [Input(Guid = "87116A9A-BEB0-4E2A-A2EF-F3E0B357F81D")]
    public readonly InputSlot<BufferWithViews> ChunkDefs = new();
    
    [Input(Guid = "5E82E351-E8A8-4594-83E3-E86C888D0588")]
    public readonly InputSlot<Command> PrepareCommand = new();
}