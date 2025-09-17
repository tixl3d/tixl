namespace Examples.Lib.io.artnet{
    [Guid("10818dcf-87b2-4e17-b8dd-36cf0c2fee69")]
    internal sealed class PointsToArtNetLightsExample : Instance<PointsToArtNetLightsExample>
    {
        [Output(Guid = "0b1a2056-7fa3-4d22-8f7e-f0deaa94c68d")]
        public readonly Slot<Command> Output = new Slot<Command>();


    }
}

