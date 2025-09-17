namespace Lib.image.analyze{
    [Guid("830df85c-51df-4d55-956c-e6810e8e3081")]
    internal sealed class DetectMotion : Instance<DetectMotion>
    {
        [Output(Guid = "6bbed7aa-310a-481b-b16e-ef5e398911a8")]
        public readonly Slot<Texture2D> TextureOutput = new Slot<Texture2D>();

        [Input(Guid = "89e7a9a3-b73b-4296-aece-821e0c87362c")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> VideoTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "e8ac51d9-1a0b-42c0-9695-f21ef7e7dcbc")]
        public readonly InputSlot<int> VideoFrameIndex = new InputSlot<int>();

        [Input(Guid = "d0f131d3-80d7-45a5-b827-dd9cd8a514f7", MappedType = typeof(Methods))]
        public readonly InputSlot<int> Method = new InputSlot<int>();

        [Input(Guid = "f0da83e6-cd5a-46e7-80cb-4ff4c995009d")]
        public readonly InputSlot<float> BackgroundFade = new InputSlot<float>();

        [Input(Guid = "96f0f8da-bb16-439a-bc0c-41c18cc42a20")]
        public readonly InputSlot<bool> RemapColor = new InputSlot<bool>();

        [Input(Guid = "0181b151-2601-4d65-9d69-5b0efd4044f3")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> RemapGradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        private enum Methods
        {
            FrameDifference,
            MotionHistory,
            OpticalFlow,
        }

    }
}

