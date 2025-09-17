namespace Examples.user.pixtur.research;

[Guid("0dbc4f3f-8fdb-402b-a7cb-6949cf21a98a")]
public class VideoClipLayer : Instance<VideoClipLayer>
{
    [Output(Guid = "44A513CA-AB20-46DF-BB6B-F28B42DC86E0")]
    public readonly TimeClipSlot<Command> Output = new();

    [Input(Guid = "31721e18-556b-452b-a8aa-18dbd44af74d")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "28f27625-37fe-409a-b6c1-d4eabf6c1eb8")]
    public readonly InputSlot<float> Volume = new();

    [Input(Guid = "5EB10090-AE6A-4AE7-9FBD-5BD9FFD13B1B")]
    public readonly InputSlot<float> ResyncThreshold = new();


}