using System.Globalization;

namespace Lib.@string.combine;

[Guid("abf1ec99-049d-474c-9023-5302d5a5c804")]
internal sealed class FloatListToString : Instance<FloatListToString>
{
    [Output(Guid = "fcd2597b-31cb-443e-a9a9-6647bc406763")]
    public readonly Slot<string> Output = new();

    public FloatListToString()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var values = Value.GetValue(context);
        if (values == null || values.Count == 0)
            Output.Value = "";

        var format = Format.GetValue(context);
        var sep = Separator.GetValue(context);
        if (string.IsNullOrWhiteSpace(format))
        {
            format = "{0:0.00}";
        }

        if (sep == null)
        {
            sep = "";
        }
        else
        {
            sep = sep.Replace("\\n", "\n");
        }

        try
        {
            _stringBuilder.Clear();
                
            if (values != null)
            {
                foreach (var v in values)
                {
                    _stringBuilder.Append(v.ToString(format, CultureInfo.InvariantCulture));
                    _stringBuilder.Append(sep);
                }
            }

            Output.Value = _stringBuilder
               .ToString(); //string.IsNullOrEmpty(format) ? values.ToString(CultureInfo.InvariantCulture) : string.Format(CultureInfo.InvariantCulture, format, values);
        }
        catch (FormatException)
        {
            Output.Value = "Invalid Format";
        }
    }

    private StringBuilder _stringBuilder = new();

    [Input(Guid = "010C814B-BE7D-4BF9-82BE-1869217BD1AD")]
    public readonly InputSlot<List<float>> Value = new();

    [Input(Guid = "ab4eef8d-8b76-48ff-8a3c-d9fc352b5a6c")]
    public readonly InputSlot<string> Format = new();

    [Input(Guid = "FA19703A-BE58-4C51-8674-A0EAFBCF96C4")]
    public readonly InputSlot<string> Separator = new();
}