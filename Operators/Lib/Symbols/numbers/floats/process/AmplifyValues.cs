using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("4def850e-3627-46d8-ae2b-58b513843885")]
internal sealed class AmplifyValues : Instance<AmplifyValues>
{
    [Output(Guid = "341496D9-D292-41BA-B2E7-468F03FE0BBB")]
    public readonly Slot<List<float>> Output = new();

    public AmplifyValues()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //Log.Debug(" global time " + EvaluationContext.BeatTime);
        var list = Input.GetValue(context);
        if (list == null || list.Count == 0)
        {
            return;
        }

        if (_averagedValues.Length != list.Count)
        {
            _averagedValues = new float[list.Count];
            _lastValues = new float[list.Count];
            _output = new List<float>(list.Count);
            _output.AddRange(Repeat(0f, list.Count));
        }

        int index2 = 0;
        var hasChanged = false;
        for (; index2 < list.Count; index2++)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (list[index2] != _lastValues[index2])
            {
                hasChanged = true;
                break;
            }
        }

        if (hasChanged)
        {
            float smoothing = Smoothing.GetValue(context);
            var mixAverage = MixAverage.GetValue(context);
            var mixCurrent = MixCurrent.GetValue(context);
            var mixAboveAverage = MixAboveAverage.GetValue(context);

            for (var index = 0; index < list.Count; index++)
            {
                var v = list[index];
                _lastValues[index] = v;
                if (double.IsNaN(v))
                    v = 0;

                    
                var smoothed = MathUtils.Lerp(v, _averagedValues[index], smoothing);
                if (float.IsNaN(smoothed) || float.IsInfinity(smoothed))
                {
                    smoothed = 0;
                }

                _averagedValues[index] = smoothed;

                _output[index] = (v - smoothed).Clamp(0, 1000) * mixAboveAverage
                                 + v * mixCurrent
                                 + smoothed * mixAverage;
            }
        }

        Output.Value = _output;
    }
        
    private float[] _averagedValues = new float[0];
    private float[] _lastValues = new float[0];
    private List<float> _output = new();

    [Input(Guid = "2C6C16E9-2037-4B5E-A4AC-6C5AAB0FC582")]
    public readonly InputSlot<float> Smoothing = new(0);

    [Input(Guid = "DCF2D659-7B51-4A87-8378-CA01419E4B7C")]
    public readonly InputSlot<float> MixAverage = new(0);

    [Input(Guid = "38D415D3-2AC1-4256-929D-CFDB3FA5C7A9")]
    public readonly InputSlot<float> MixCurrent = new(0);

    [Input(Guid = "C8F0B975-676F-46A4-B7EF-39D385C82CEF")]
    public readonly InputSlot<float> MixAboveAverage = new(0);

    [Input(Guid = "813a6eab-947d-47a0-af8e-7a92c880d338")]
    public readonly InputSlot<List<float>> Input = new(new List<float>(20));
}