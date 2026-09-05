using T3.Core.Utils;

namespace Lib.numbers.anim.animators;

[Guid("d8010a72-68ac-4fde-9b9d-0984628b8a56")]
public sealed class AnimFloatList : Instance<AnimFloatList>
{
        
    public AnimFloatList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var phases = Phase.GetValue(context);
        var rates = Rate.GetValue(context);
        var ratio = Ratio.GetValue(context);
        var rateFactorFromContext = AnimMath.GetSpeedOverrideFromContext(context, AllowSpeedFactor);
        _shape = (AnimMath.Shapes)Shape.GetValue(context).Clamp(0, Enum.GetNames(typeof(AnimMath.Shapes)).Length);
        var amplitudes = Amplitude.GetValue(context);
        var offsets = Offset.GetValue(context);
        var bias = Bias.GetValue(context);
        var time = OverrideTime.HasInputConnections
                       ? OverrideTime.GetValue(context)
                       : context.LocalFxTime;
        
        var offsetNumber = OffsetNumber.GetValue(context);
        var offsetCycle = OffsetCycle.GetValue(context);

        // Check if numOfOut is positive
        if (offsetNumber <= 0)
        {
            Result.Value = new List<float>(); // Return empty list if no outputs are required
            return;
        }

        List<float> floatList = new List<float>(offsetNumber);

        for (int i = 0; i < offsetNumber; i++)
        {
            // Calculate the adjusted time for each output based on offsetNumber and offsetCycle
            float adjustedTime = (float)(time + phases + (i * offsetCycle) + offsetNumber);

            var v = AnimMath.CalcValueForNormalizedTime(_shape, ((adjustedTime * rateFactorFromContext * rates)), 0, bias, ratio) * amplitudes + offsets;
            floatList.Add(v);
        }

        Result.Value = floatList;
    }
    

    [Input(Guid = "a63db03e-fe05-436e-99b8-1b67a5568a32")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "60ad85b2-eb0e-4b08-9fcc-1f315d1b3f7f", MappedType = typeof(AnimMath.Shapes))]
    public readonly InputSlot<int> Shape = new();

    [Input(Guid = "e05405cf-276f-4433-a5c4-0f2495b054f1")]
    public readonly InputSlot<float> Bias = new();

    [Input(Guid = "8159019a-3ef7-44ce-8ebb-a461ff893f0c")]
    public readonly InputSlot<float> Ratio = new();

    [Input(Guid = "0042a89e-2cdc-49e5-8366-5f6631fe04ed", MappedType = typeof(AnimMath.SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new();

        [Input(Guid = "0846a4b7-2b47-4a05-a6ee-e8628adbceff")]
        public readonly InputSlot<int> OffsetNumber = new InputSlot<int>();

        [Input(Guid = "9119408b-c753-45ab-b67a-4381d4b0ffa3")]
        public readonly InputSlot<float> OffsetCycle = new InputSlot<float>();

        [Input(Guid = "aff68f72-4c4d-4c38-b223-4c558e87d66d")]
        public readonly InputSlot<float> Rate = new InputSlot<float>();

        [Input(Guid = "10eb53c5-44c7-4e33-ab5a-2025f5ad7d4a")]
        public readonly InputSlot<float> Amplitude = new InputSlot<float>();

        [Input(Guid = "02ddcf2e-6e4a-48eb-95b2-66e91e2a1a24")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "1d87c85c-c089-4884-b8be-b4229a707a73")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();
        
        

        [Output(Guid = "8f508dce-6541-4388-a924-f7933047a2b8")]
        public readonly Slot<List<float>> Result = new Slot<List<float>>();
        
        
        public AnimMath.Shapes _shape;
        
}