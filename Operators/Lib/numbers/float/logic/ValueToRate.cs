using System.Globalization;
using T3.Core.Utils;

namespace Lib.numbers.@float.logic;

[Guid("8171c2f5-96df-41f6-884c-dbd004ae8a17")]
internal sealed class ValueToRate : Instance<ValueToRate>
{
    [Output(Guid = "c21800d0-ee83-43f6-8f1c-9cee2e495056")]
    public readonly Slot<float> Result = new();

    public ValueToRate()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var ratesIsDirty = Rates.IsDirty;
        var ratesString = Rates.GetValue(context);
        UpdateRatios(ratesIsDirty, ratesString);
        
        var stepCount = _ratios.Count;
        var f = Value.GetValue(context).Clamp(0, 0.99f);

        var result = stepCount switch
                         {
                             0 => 1,
                             _ => _ratios[(int)((stepCount - 1) * f + 0.5f)]
                         };

        Result.Value = result;
    }

    private void UpdateRatios(bool ratesIsDirty, string ratesString)
    {
        if (!ratesIsDirty || ratesString == _ratesStrings) 
            return;
        
        _ratesStrings = ratesString;
        _ratios. Clear();
        if (string.IsNullOrEmpty(_ratesStrings))
            return;
        
        foreach (var line in ratesString.Split('\n'))
        {
            if (float.TryParse(line, CultureInfo.InvariantCulture.NumberFormat, out var f))
            {
                _ratios.Add(f);
            }
            else
            {
                Log.Warning($"Can't convert {line} to float ratio", this);
            }
        }
            
    }

    private readonly List<float> _ratios = [];
    private string _ratesStrings;

    [Input(Guid = "4f2dad75-0f45-498a-9a1a-7571dc9f0b09")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "1AD90698-0D84-488B-B969-4D727E173AFF")]
    public readonly InputSlot<string> Rates = new();
}