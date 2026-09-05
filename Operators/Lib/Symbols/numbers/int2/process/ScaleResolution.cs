namespace Lib.numbers.int2.process;

[Guid("eb818dd0-0c9c-40ee-b76e-2148f958b987")]
internal sealed class ScaleResolution : Instance<ScaleResolution>
{
    [Output(Guid = "83b4187e-e4a6-45a4-816f-6d5cb75021d5")]
    public readonly Slot<Int2> Size = new();
        
    public ScaleResolution()
    {
        Size.UpdateAction += Update;
    }

    private const int MaxSize = 16384;
    private void Update(EvaluationContext context)
    {
        var r = Resolution.GetValue(context);
        // Log.Debug(" " + r);
        var f = Factor.GetValue(context);
        var newSize =new Int2(
                              (int)(r.Width * f.X),
                              (int)(r.Height * f.Y));

        if (ClampToValidTextureSize.GetValue(context))
        {
            if (newSize.Width <= 0)
                newSize.Width = 1;
            else if (newSize.Width > MaxSize)
                newSize.Width = MaxSize;
                
            if (newSize.Height <= 0)
                newSize.Height = 1;
            else if (newSize.Height > MaxSize)
                newSize.Height = MaxSize;
        }

        Size.Value = newSize;
    }

    [Input(Guid = "266B57DC-26B5-4267-854D-A8E1A25CF29C")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "94703070-18A3-41A7-B8E7-53D6E24D84CF")]
    public readonly InputSlot< Vector2> Factor = new();
        
    [Input(Guid = "D14B7C37-E012-4659-98E9-226AB97291BE")]
    public readonly InputSlot<bool> ClampToValidTextureSize = new();
}