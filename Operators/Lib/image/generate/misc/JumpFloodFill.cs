namespace Lib.image.generate.misc{

    [Guid("e9b69525-4df8-40a6-86de-938597bd33a3")]

    internal sealed class JumpFloodFill : Instance<JumpFloodFill>
    {


        [Input(Guid = "12651b8c-42af-4de6-b37d-6383f0da22cc")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "43d8985c-47b4-4cc7-8bad-b30574508ead")]
        public readonly InputSlot<int> MaxIterationCount = new InputSlot<int>();

        [Output(Guid = "f8b068db-6b55-434f-8c0e-aba5c6835311")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ImageOutput = new Slot<T3.Core.DataTypes.Texture2D>();


    }

}


