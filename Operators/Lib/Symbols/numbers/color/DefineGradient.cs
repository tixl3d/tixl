namespace Lib.numbers.color;

[Guid("185452c6-f299-42a3-b618-c6aa00d66c62")]
internal sealed class DefineGradient : Instance<DefineGradient>
{
    [Output(Guid = "b4b1c17e-55b6-439a-8baa-ac5c6c3003c8")]
    public readonly Slot<Gradient> OutGradient = new();


    public DefineGradient()
    {
        OutGradient.UpdateAction += Update;
            
        _slots = new List<Pair>
                     {
                         new() {PositionSlot = Color1Pos,ColorSlot= Color1},
                         new() {PositionSlot = Color2Pos,ColorSlot= Color2},
                         new() {PositionSlot = Color3Pos,ColorSlot= Color3},
                         new() {PositionSlot = Color4Pos,ColorSlot= Color4},
                     };
    }

    private void Update(EvaluationContext context)
    {
        _gradient.Steps.Clear();
        foreach (var slots in _slots)
        {
            var pos = slots.PositionSlot.GetValue(context);
            if (pos < 0)
                continue;
                
            _gradient.Steps.Add(new Gradient.Step
                                    {
                                        NormalizedPosition = pos,
                                        Color = slots.ColorSlot.GetValue(context),
                                        Id = slots.PositionSlot.Id
                                    });
        }

        if (_gradient.Steps.Count == 0)
        {
            _gradient.Steps.Add(new Gradient.Step
                                    {
                                        NormalizedPosition = 0,
                                        Color = Color1.GetValue(context),
                                        Id = Color1Pos.Id
                                    });
        }
        _gradient.SortHandles();

        _gradient.Interpolation = (Gradient.Interpolations)Interpolation.GetValue(context);

        OutGradient.Value = _gradient;    //FIXME: This might not be efficient or required
    }

    private readonly Gradient _gradient = new();

    private class Pair
    {
        public InputSlot<float> PositionSlot;
        public InputSlot<Vector4> ColorSlot;
    }

    private List<Pair> _slots;
        
    [Input(Guid = "4723C177-9941-4FCB-9D5A-9A4ED18F3B5F")]
    public readonly InputSlot<Vector4> Color1 = new();

    [Input(Guid = "35470c79-275b-4a88-bc4b-4681a35d804d")]
    public readonly InputSlot<float> Color1Pos = new();
        
    [Input(Guid = "97147836-0858-4FD0-9532-A5636FF8567D")]
    public readonly InputSlot<Vector4> Color2 = new();

    [Input(Guid = "FE6C6818-4AA9-439F-91FA-98DB8C6DC1E6")]
    public readonly InputSlot<float> Color2Pos = new();
        
    [Input(Guid = "F62EACF0-5237-4F20-9FB2-C59B63B24DAD")]
    public readonly InputSlot<Vector4> Color3 = new();

    [Input(Guid = "2823FED8-9B37-4521-A37C-FCC3B9C4E6F2")]
    public readonly InputSlot<float> Color3Pos = new();
        
    [Input(Guid = "F90BF3A5-3AD0-4DFE-82E0-1630CD8E27D7")]
    public readonly InputSlot<Vector4> Color4 = new();

    [Input(Guid = "C3C4D160-61BD-483A-ADA8-0395D160A009")]
    public readonly InputSlot<float> Color4Pos = new();
        
    [Input(Guid = "E3EF0E14-6ED1-482E-BD31-0C9CBA5AA126", MappedType = typeof(Gradient.Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}