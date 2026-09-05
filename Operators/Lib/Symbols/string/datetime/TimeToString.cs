namespace Lib.@string.datetime;

[Guid("075612b1-8760-4858-ad6b-6c85a7716794")]
internal sealed class TimeToString : Instance<TimeToString>
{
    [Output(Guid = "d45912c1-1c26-4a80-bfff-57de6ae6ccdf")]
    public readonly Slot<string> Result = new();

    [Input(Guid = "ecc27b89-e89a-4c01-93ad-b4750d09f2f7")]
    public readonly InputSlot<float> Input = new();


}