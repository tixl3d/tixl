namespace Examples.Lib.io.file;

[Guid("1d56e2c6-9199-41e7-9404-24f4f6b75044")]
 internal sealed class PlayVideoExample : Instance<PlayVideoExample>
{
    [Output(Guid = "36bf11a1-668d-41f0-8107-d6304b82430f")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "d4d111e2-01f2-4a71-b2f1-927557a13a6f")]
    public readonly InputSlot<string> FolderWithVideos = new();


}