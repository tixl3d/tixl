using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("784b280f-bbde-4c8e-9ca7-66de5b567672")]
internal sealed class ColorListToInts : Instance<ColorListToInts>
{
    [Output(Guid = "252C064C-A380-4940-9A9F-CFF58F7E17B5")]
    public readonly Slot<List<int>> Result = new();

    public ColorListToInts()
    {
        Result.UpdateAction += Update;
    }
    
    private void Update(EvaluationContext context)
    {
        Result.Value ??= []; 
        var list = Result.Value;

        _outputMode = OutputMode.GetEnumValue<Modes>(context);
        
        var inputListSlots = ColorLists.GetCollectedTypedInputs();
        list.Clear();
        
        foreach (var inputSlot in inputListSlots)
        {
            var l = inputSlot.GetValue(context);
            if (l == null || l.Count == 0)
                continue;

            foreach (var c in l)
            {
                AppendChannelValues(c);
            }
        }
        ColorLists.DirtyFlag.Clear();
    }


    private void AppendChannelValues(Vector4 c)
    {
        switch (_outputMode)
        {
            case Modes.RGBA:
                AppendAsInt(c.X);
                AppendAsInt(c.Y);
                AppendAsInt(c.Z);
                AppendAsInt(c.W);
                break;
            case Modes.ARGB:
                AppendAsInt(c.W);
                AppendAsInt(c.X);
                AppendAsInt(c.Y);
                AppendAsInt(c.Z);
                break;
            case Modes.RGB:
                AppendAsInt(c.X);
                AppendAsInt(c.Y);
                AppendAsInt(c.Z);
                break;
            case Modes.R:
                AppendAsInt(c.X);
                break;
            case Modes.A:
                AppendAsInt(c.W);
                break;
        }
    }

    private void AppendAsInt(float f)
    {
        Result.Value.Add((int)(f * 255).Clamp(0,255));
    }
    
    private Modes _outputMode = Modes.RGB;
    

    [Input(Guid = "4F8B8E1C-520C-49C4-829E-62D34887958E")]
    public readonly MultiInputSlot<List<Vector4>> ColorLists = new();
    
    [Input(Guid = "589BB6F6-F7A5-49E9-A11F-A0F2B0BA8DBF", MappedType = typeof(Modes))]
    public readonly InputSlot<int> OutputMode = new();

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private enum Modes
    {
        RGBA,
        ARGB,
        RGB,
        R,
        A,
    }
}