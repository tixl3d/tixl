using T3.Core.Utils;


namespace Lib.mesh.@_;

[Guid("e43370a7-dafd-48b0-bac6-f30ea1bcf4cb")]
internal sealed class AnalyzeMeshBuffers : Instance<AnalyzeMeshBuffers>
{
    [Output(Guid = "bbf576df-f319-4419-9067-d8e364e375af")]
    public readonly Slot<int> MeshBufferCount = new();

    [Output(Guid = "09DA3EE3-942B-4FD2-ADB3-C70C10A593A0")]
    public readonly Slot<BufferWithViews> SelectedVertexBuffer = new();

    [Output(Guid = "69F80F9A-6F5C-48D8-B29D-907A740A259D")]
    public readonly Slot<int> SelectedVertexBufferStartPos = new();
        
    [Output(Guid = "8DF00FC8-A4CF-4874-8C3E-7604A5E05FEA")]
    public readonly Slot<int> TotalVertexCount = new();

    [Output(Guid = "5cb8b61f-c385-4cf6-8061-3d37011d3011")]
    public readonly Slot<BufferWithViews> SelectedIndexBuffer = new();

    [Output(Guid = "53ebc3b1-aea1-4c14-a755-0b4a95a12299")]
    public readonly Slot<int> SelectedIndexBufferStartPos = new();
        
    [Output(Guid = "539bd13f-0400-4ea4-9ad4-77c3724b9cb0")]
    public readonly Slot<int> TotalIndexCount = new();
        
    public AnalyzeMeshBuffers()
    {
        MeshBufferCount.UpdateAction += Update;
        SelectedIndexBufferStartPos.UpdateAction += Update;
        SelectedVertexBufferStartPos.UpdateAction += Update;
        TotalIndexCount.UpdateAction += Update;
        TotalVertexCount.UpdateAction += Update;
        SelectedIndexBuffer.UpdateAction += Update;
        SelectedVertexBuffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var connections = Meshes.GetCollectedTypedInputs();
        var selectedIndex = Index.GetValue(context).Clamp(0, connections.Count-1);

        if (connections.Count == 0)
        {
            TotalIndexCount.Value = 0;
            SelectedIndexBufferStartPos.Value = 0;
            MeshBufferCount.Value = 0;
            return;
        }

        var totalIndexCount = 0;
        var totalVertexCount = 0;

        for (var connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
        {
            var input = connections[connectionIndex];
                
            var meshBuffer = input.GetValue(context);
            if (meshBuffer !=null 
                && meshBuffer.IndicesBuffer != null 
                && meshBuffer.VertexBuffer != null 
                && meshBuffer.IndicesBuffer.Srv != null
                && meshBuffer.VertexBuffer.Srv != null
                && !meshBuffer.IndicesBuffer.Srv.IsDisposed
                && !meshBuffer.VertexBuffer.Srv.IsDisposed
               )
            {
                var indexBuffer = meshBuffer.IndicesBuffer;
                var vertexBuffer = meshBuffer.VertexBuffer;
                var indexCount = indexBuffer.Srv.Description.Buffer.ElementCount;
                var vertexCount = vertexBuffer.Srv.Description.Buffer.ElementCount;

                if (connectionIndex == selectedIndex)
                {
                    SelectedIndexBuffer.Value = indexBuffer;
                    SelectedIndexBuffer.DirtyFlag.Invalidate();
                        
                    SelectedIndexBufferStartPos.Value = totalIndexCount;
                    SelectedIndexBufferStartPos.DirtyFlag.Invalidate();
                        
                    SelectedVertexBuffer.Value = vertexBuffer;
                    SelectedVertexBuffer.DirtyFlag.Invalidate();
                        
                    SelectedVertexBufferStartPos.Value = totalVertexCount;
                    SelectedVertexBufferStartPos.DirtyFlag.Invalidate();
                }
                totalIndexCount += indexCount;
                totalVertexCount += vertexCount;
            }
            else
            {
                Log.Warning($"Undefined MeshBuffer at index {connectionIndex}", this);
            }
        }
            
        MeshBufferCount.Value = connections.Count; 
        TotalIndexCount.Value = totalIndexCount;
        TotalVertexCount.Value = totalVertexCount;
            
        MeshBufferCount.DirtyFlag.Clear();
        SelectedIndexBufferStartPos.DirtyFlag.Clear();
        SelectedVertexBufferStartPos.DirtyFlag.Clear();
        TotalIndexCount.DirtyFlag.Clear();
        TotalVertexCount.DirtyFlag.Clear();
        SelectedIndexBuffer.DirtyFlag.Clear();
        SelectedVertexBuffer.DirtyFlag.Clear();
    }
        
    [Input(Guid = "F16E43EB-36AB-4104-ACA3-AFE8617EE9DA")]
    public readonly MultiInputSlot<MeshBuffers> Meshes = new();

    [Input(Guid = "dca386aa-b0cd-44a3-9472-e05e16a8f87d")]
    public readonly InputSlot<int> Index = new();

}