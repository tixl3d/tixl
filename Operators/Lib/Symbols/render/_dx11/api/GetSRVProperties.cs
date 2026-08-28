namespace Lib.render._dx11.api;

[Guid("bc489196-9a30-4580-af6f-dc059f226da1")]
internal sealed class GetSRVProperties : Instance<GetSRVProperties>
{
    [Output(Guid = "431B39FD-4B62-478B-BBFA-4346102C3F61")]
    public readonly Slot<int> ElementCount = new();

    [Output(Guid = "59C4FE70-9129-4BCE-BA39-6D252A59FB97")]
    public readonly Slot<Buffer> Buffer = new();

    public GetSRVProperties()
    {
        ElementCount.UpdateAction += Update;
        Buffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var srv = SRV.GetValue(context);
        if (srv == null)
        {
            return;
        }

        try
        {
            ElementCount.Value = srv.Description.Buffer.ElementCount;
        }
        catch (Exception e)
        {
            Log.Error("Failed to get SRVProperties: " + e.Message, this);
        }
    }

    [Input(Guid = "E79473F4-3FD2-467E-ACDA-B27EF7DAE6A9")]
    public readonly InputSlot<ShaderResourceView> SRV = new();
}