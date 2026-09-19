namespace Examples.user.still.worksforeverybody.scenes;

[Guid("1026538b-f021-43f0-b6bd-dda89c57de94")]
internal sealed class _WfeFinalPart : Instance<_WfeFinalPart>
{

    [Output(Guid = "4d077ee1-277d-474d-889e-30db32471ce8")]
    public readonly Slot<T3.Core.DataTypes.Command> CommandOutput = new();

    [Input(Guid = "fb0adf5b-f16a-4ccc-b270-9fb87f0c9529")]
    public readonly InputSlot<System.Collections.Generic.List<string>> Scores = new();

    [Input(Guid = "2301f6d0-f715-4f92-be99-f22dca8aac12")]
    public readonly InputSlot<int> LastScore = new();


}