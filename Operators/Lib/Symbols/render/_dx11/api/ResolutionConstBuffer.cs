namespace Lib.render._dx11.api;

[Guid("38e88910-6063-41d1-840b-8aeeb0eeccc0")]
internal sealed class ResolutionConstBuffer : Instance<ResolutionConstBuffer>
{
    [Output(Guid = "{FE020A5C-91E1-441F-BE0D-AB5900D150EB}")]
    public readonly Slot<Buffer> Buffer = new();

    public ResolutionConstBuffer()
    {
        Buffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var bufferContent = new ResolutionBufferLayout(Resolution.GetValue(context));
        ResourceManager.SetupConstBuffer(bufferContent, ref Buffer.Value);
        Buffer.Value.DebugName = nameof(ResolutionBufferLayout);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ResolutionBufferLayout
    {
        public ResolutionBufferLayout(Int2 resolution)
        {
            Width = resolution.Width;
            Height = resolution.Height;
        }
            
        [FieldOffset(0)]
        public float Width;

        [FieldOffset(4)]
        public float Height;
            
    }
        
    [Input(Guid = "3BBA98BD-2713-4E5B-B082-20B39392EF9B")]
    public readonly InputSlot<Int2> Resolution = new();
}