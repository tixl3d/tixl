namespace Lib.numbers.vec3;

[Guid("15ac7153-69af-45f8-bcdd-50cdef0c9ae1")]
internal sealed class AddVec3 : Instance<AddVec3>
{
    [Output(Guid = "C8942BE3-53CF-4764-8663-35159C8E0F6D")]
    public readonly Slot<Vector3> Result = new();

    //         [Output(Guid = "{DF114783-6C8D-47E2-99B0-8C97217657A5}")]
    //         public readonly Slot<float> Result2 = new Slot<float>();

    public AddVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = Input1.GetValue(context) + Input2.GetValue(context);
    }


    [Input(Guid = "F933C566-CBB2-4D2C-9E3B-F7AD3B2F7292")]
    public readonly InputSlot<Vector3> Input1 = new();

    [Input(Guid = "08624CA6-8B69-48F5-8896-A483B403778E")]
    public readonly InputSlot<Vector3> Input2 = new();

    //         [Input(Guid = "{D7478BAA-41B4-4F83-873B-6267AA93BFA9}")]
    //         public readonly InputSlot<float> Input3 = new InputSlot<float>();
    // 
    //         [Input(Guid = "{99A53560-8F62-4240-9ED4-800525CF2EF3}")]
    //         public readonly InputSlot<float> Input4 = new InputSlot<float>();
}