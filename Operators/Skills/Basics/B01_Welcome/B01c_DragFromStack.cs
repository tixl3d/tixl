namespace Skills.Basics.B01_Welcome;

[Guid("468cafa2-d5ef-46f3-b0b7-626cdb0322cf")]
internal sealed class B01c_DragFromStack : Instance<B01c_DragFromStack>
{
    [Output(Guid = "1fd4183f-2c57-430c-8c06-9c001b635e02")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}