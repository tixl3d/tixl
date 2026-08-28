namespace Lib.render._dx11.buffer;

[Guid("49549c3e-b09e-4633-86c6-1ac075f56b69")]
internal sealed class UseFallbackBuffer : Instance<UseFallbackBuffer>
{
    [Output(Guid = "EF014AE5-962F-4A7B-9DCB-9E26863DD074")]
    public readonly Slot<BufferWithViews> Output = new();

    public UseFallbackBuffer()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var buffer = PrimaryBuffer.GetValue(context);
        if (buffer == null)
            buffer = Fallback.GetValue(context);
            
        Output.Value = buffer;
    }
        
        
    [Input(Guid = "7246FA40-3106-4D60-AB2C-5E913F3A9648")]
    public readonly InputSlot<BufferWithViews> PrimaryBuffer = new();
        
    [Input(Guid = "9904BD42-CBE5-4F83-AAB0-4973E1CBCBF8")]
    public readonly InputSlot<BufferWithViews> Fallback = new();
}