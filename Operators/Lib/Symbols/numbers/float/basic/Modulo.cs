namespace Lib.numbers.@float.basic;

[Guid("5202d3f6-c970-4006-933d-3c60d6c202dc")]
internal sealed class Modulo : Instance<Modulo>
{
    [Output(Guid = "4e4ebbcf-6b12-4ce7-9bec-78cd9049e239")]
    public readonly Slot<float> Result = new();

    public Modulo()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var mod2 = ModuloValue.GetValue(context);

        if (mod2 != 0)
        {
            Result.Value = v - mod2 * (float)Math.Floor(v/mod2);
        }
        else
        {
            T3.Core.Logging.Log.Debug("Modulo caused division by zero", this);
            Result.Value = 0;
        }
    }
        
    [Input(Guid = "8a401e5d-295d-4403-a3af-1d6b91ce3dba")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "62a8185f-32c0-41d2-b8be-d8c1d7178c00")]
    public readonly InputSlot<float> ModuloValue = new();
}