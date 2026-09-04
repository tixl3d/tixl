namespace Lib.geometry;

/// <summary>
/// Composes a ScalarField with a RemapCurve: the resulting field evaluates the
/// input field, then remaps its value. Without a curve the field passes through.
/// </summary>
[Guid("f29bd702-4ebf-4bd2-9416-884c72fbc769")]
internal sealed class RemapFieldValues : Instance<RemapFieldValues>
{
    [Output(Guid = "5f1f2a83-cacc-4e72-8a4c-171a6f62fe1a")]
    public readonly Slot<ScalarField> Result = new();

    public RemapFieldValues()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var field = Field.GetValue(context);
        var curve = Curve.GetValue(context);
        if (field == null || curve == null)
        {
            Result.Value = field;
            return;
        }

        var evaluate = field.Evaluate;
        var remap = curve.Remap;
        Result.Value = new ScalarField((in FieldSample sample) => remap(evaluate(in sample)));
    }

    [Input(Guid = "72d1fc6b-fddc-4011-a6d3-b78cbff5a4ce")]
    public readonly InputSlot<ScalarField> Field = new();

    [Input(Guid = "0454c682-e307-4455-861b-00c40b3d3dfc")]
    public readonly InputSlot<RemapCurve> Curve = new();
}
