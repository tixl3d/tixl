namespace Examples.Lib.point;

[Guid("25d26231-730e-4376-b256-e34eca6290ce")]
 internal sealed class GetPointDataFromListExample : Instance<GetPointDataFromListExample>
{
    [Output(Guid = "ca72b890-96df-40ee-bb64-240b54edf483")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}