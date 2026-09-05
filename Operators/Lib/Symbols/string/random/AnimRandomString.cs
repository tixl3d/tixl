namespace Lib.@string.random;

[Guid("dd7fa7ee-266a-43c8-b29f-3357488b26be")]
internal sealed class AnimRandomString : Instance<AnimRandomString>
{
    [Output(Guid = "3a769380-1586-4d7f-a881-e509d5c14c1b")]
    public readonly Slot<string> Fragments = new();

    [Input(Guid = "4e36b984-43ff-447b-8e1d-a099fefd4d74")]
    public readonly InputSlot<float> Rate = new();

    [Input(Guid = "9b705b88-a644-498a-b831-5d0243e01c41", MappedType = typeof(MockStrings.Categories))]
    public readonly InputSlot<int> Category = new();


}