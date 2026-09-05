using System.Globalization;

namespace Lib.@string.convert;

[Guid("06ae6091-ce81-4e8f-ae96-83c189c70b10")]
internal sealed class Vec3ToString : Instance<Vec3ToString>
{
    [Output(Guid = "bc59ac99-0e6f-4d8a-8896-661b45c86ecd")]
    public readonly Slot<string> Output = new();

    public Vec3ToString()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Vector.GetValue(context);
        var s = Format.GetValue(context);

        try
        {
            if (string.IsNullOrEmpty(s))
            {
                // Fixed width: 7 chars total (e.g., "  -1.50" or "   1.50")
                Output.Value = string.Format(CultureInfo.InvariantCulture,
                    "X: {0,7:F2}\nY: {1,7:F2}\nZ: {2,7:F2}", v.X, v.Y, v.Z);
            }
            else
            {
                // Replace literal \n with actual newline characters
                var formatWithNewlines = s.Replace("\\n", "\n");
                Output.Value = string.Format(CultureInfo.InvariantCulture, formatWithNewlines, v.X, v.Y, v.Z);
            }
        }
        catch (FormatException)
        {
            Output.Value = "Invalid Format";
        }
    }

    [Input(Guid = "0035b185-2e0b-4854-842a-4965180177f1")]
    public readonly InputSlot<Vector3> Vector = new();

    [Input(Guid = "045e84e3-2456-46fb-af43-30549e08afbd")]
    public readonly InputSlot<string> Format = new();
}