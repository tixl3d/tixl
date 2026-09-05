namespace Lib.numbers.int2.process;

[Guid("f86358e0-2573-4acd-9a90-e95108e8a4da")]
internal sealed class Int2Components : Instance<Int2Components>
{
    [Output(Guid = "CD0BD085-DD4A-46A5-BF00-39A199434B30")]
    public readonly Slot<int> Width = new();

    [Output(Guid = "DC835127-E03B-4AFA-B91A-468781B5B599")]
    public readonly Slot<int> Height = new();

    [Output(Guid = "894E22A0-B3D0-425E-9BB9-A0CBB821D4DE")]
    public readonly Slot<int> Length = new();

    [Output(Guid = "DD31C09B-CB39-44FF-9CC8-2AEDEC4E758B")]
    public readonly Slot<float> AspectRatio = new();

    public Int2Components()
    {
        Width.UpdateAction += Update;
        Height.UpdateAction += Update;
        Length.UpdateAction += Update;
        AspectRatio.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var r = Resolution.GetValue(context);
        Width.Value = r.Width;
        Height.Value = r.Height;
        Length.Value = r.Width * r.Height;
        AspectRatio.Value = (float)r.Width / (r).Height;
    }
        
    [Input(Guid = "425BA347-D82A-49EC-B8B4-D0F8F7E3A504")]
    public readonly InputSlot<Int2> Resolution = new();
}