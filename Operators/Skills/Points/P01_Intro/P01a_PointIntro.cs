namespace Skills.Points.P01_Intro;

[Guid("c6ae0c1e-b598-4bd2-81f7-97cf2af4f729")]
internal sealed class P01a_PointIntro :Instance<P01a_PointIntro>{
    [Output(Guid = "aa08e511-c700-41ed-866e-eab616612717")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}