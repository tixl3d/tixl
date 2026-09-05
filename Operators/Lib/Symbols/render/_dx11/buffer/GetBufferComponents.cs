namespace Lib.render._dx11.buffer;

[Guid("80dff680-5abf-484a-b9e0-81d72f3b7aa4")]
internal sealed class GetBufferComponents : Instance<GetBufferComponents>
{
    [Output(Guid = "1368ab8e-d75e-429f-8ecd-0944f3ede9ab")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Output(Guid = "f03246a7-e39f-4a41-a0c3-22bc976a6000")]
    public readonly Slot<UnorderedAccessView> UnorderedAccessView = new();

    [Output(Guid = "a7d11905-eb9e-42a4-a077-11d2c1cb41b2")] 
    public readonly Slot<Buffer> Buffer = new();
    
    [Output(Guid = "D7918FD8-906E-424D-8C5C-9631941CFC9D")]
    public readonly Slot<int> Length = new();
        
    [Output(Guid = "6D7A9493-6210-462A-B9C2-525B925DE6C8")]
    public readonly Slot<int> Stride = new();

    [Output(Guid = "5DCC43AC-4FB9-49C3-A093-0A59A002FB6C")]
    public readonly Slot<bool> IsValid = new();



        
    public GetBufferComponents()
    {
        Buffer.UpdateAction += Update;
        ShaderResourceView.UpdateAction += Update;
        UnorderedAccessView.UpdateAction += Update;
        Length.UpdateAction += Update;
        Stride.UpdateAction += Update;
        IsValid.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var logWarnings = LogWarnings.GetValue(context);
        var bufferWithViews = BufferWithViews.GetValue(context);
        if (bufferWithViews != null  
            && bufferWithViews.Srv != null && !bufferWithViews.Srv.IsDisposed 
            && bufferWithViews.Uav != null && !bufferWithViews.Uav.IsDisposed )
        {
            IsValid.Value = true;
            Buffer.Value = bufferWithViews.Buffer;
            ShaderResourceView.Value = bufferWithViews.Srv;
            UnorderedAccessView.Value = bufferWithViews.Uav;
            if (ShaderResourceView?.Value == null)
            {
                if(logWarnings)
                    Log.Warning("Can't access undefined shader resource view", this);
                    
                SetAsInvalid();
            }
            else
            {
                try
                {
                    Length.Value = ShaderResourceView.Value.Description.Buffer.ElementCount;
                    Stride.Value = bufferWithViews.Buffer.Description.StructureByteStride;
                    // Buffer.DirtyFlag.Clear();
                    // ShaderResourceView.DirtyFlag.Clear();
                    // UnorderedAccessView.DirtyFlag.Clear();
                    // Length.DirtyFlag.Clear();
                    // Stride.DirtyFlag.Clear();
                }
                catch (Exception e)
                {
                    if(logWarnings)
                        Log.Warning("Can't access undefined SRV description: " + e.Message, this);
                        
                    SetAsInvalid();
                }
            }
        }
        else
        {
            SetAsInvalid();
        }
        // Buffer.DirtyFlag.Clear();
        // ShaderResourceView.DirtyFlag.Clear();
        // UnorderedAccessView.DirtyFlag.Clear();
        // Length.DirtyFlag.Clear();
        // Stride.DirtyFlag.Clear();
    }
 
    private void SetAsInvalid()
    {
        IsValid.Value = false;
        Buffer.Value = null;
        ShaderResourceView.Value = null;
        UnorderedAccessView.Value = null;
        Length.Value = 0;
    }
        
    [Input(Guid = "7a13b834-21e5-4cef-ad5b-23c3770ea763")]
    public readonly InputSlot<BufferWithViews> BufferWithViews = new();
        
    [Input(Guid = "AC0F9D07-B93D-45D3-8B3F-A4B2F7240AE0")]
    public readonly InputSlot<bool> LogWarnings = new();
}