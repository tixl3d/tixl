namespace Lib.render._dx11.api;

[Guid("9b28e6b9-1d1f-42d8-8a9e-33497b1df820")]
internal sealed class Draw : Instance<Draw>, IRenderStatsProvider
{
    [Output(Guid = "49B28DC3-FCD1-4067-BC83-E1CC848AE55C")]
    public readonly Slot<Command> Output = new();

        
    public Draw()
    {
        lock (_lock)
        {
            if (!_registeredForStats)
            {
                RenderStatsCollector.RegisterProvider(this);
                _registeredForStats = true;
            }
        }
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //Log.Debug("Draw2", this);
        var vertexCount = VertexCount.GetValue(context);
        var startVertexLocation = VertexStartLocation.GetValue(context);

        var deviceContext = ResourceManager.Device.ImmediateContext;

        var setVs = deviceContext.VertexShader.Get();
        var setPs = deviceContext.PixelShader.Get();
            
        if (setVs == null || setPs == null)
        {
            if (!_complainedOnce)
            {
                Log.Warning("Trying to issue draw call, but pixel and/or vertex shader are null.", this);
            }
            _complainedOnce = true;
            return;
        }

        _vertexCount += vertexCount;
        _drawCallCount++;

        _complainedOnce = false;

        deviceContext.Draw(vertexCount, startVertexLocation);
    }

    protected override void Dispose(bool disposing)
    {
        RenderStatsCollector.UnregisterProvider(this);
        base.Dispose(disposing);
    }

    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("Triangles", _vertexCount/3);
        yield return ("Draw calls", _drawCallCount);
    }
        

    public void StartNewFrame()
    {
        _vertexCount = 0;
        _drawCallCount = 0;
    }

    private bool _complainedOnce;
    private static int _vertexCount;
    private static int _drawCallCount;
    private static bool _registeredForStats;
    private static readonly Dictionary<string, int> _statResults = new();
    private static readonly object _lock = new ();
        
    [Input(Guid = "8716B11A-EF71-437E-9930-BB747DA818A7")]
    public readonly InputSlot<int> VertexCount = new();
    [Input(Guid = "B381B3ED-F043-4001-9BBC-3E3915F38235")]
    public readonly InputSlot<int> VertexStartLocation = new();

}