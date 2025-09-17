namespace Lib.io.video{
    [Guid("b4288f74-7997-40f4-bc4c-2833bf3797de")]
    internal sealed class VideoClip : Instance<VideoClip>
    {

        [Output(Guid = "1503d637-b9fe-4323-8626-e54252e997ee")]
        public readonly TimeClipSlot<T3.Core.DataTypes.Command> Output2 = new TimeClipSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "c064afe1-0078-48eb-be6a-4616f7c5a383")]
        public readonly InputSlot<string> FilePath = new InputSlot<string>();

        [Input(Guid = "a5f0efc1-2f2d-41a5-91f4-162febf046b2")]
        public readonly InputSlot<float> ResynchThreshold = new InputSlot<float>();

        [Input(Guid = "69c0c1fd-ca53-4943-9e26-6c78d79555be")]
        public readonly InputSlot<bool> Loop = new InputSlot<bool>();

        [Input(Guid = "88ca47b1-66af-4f59-9266-c1781d9101e5")]
        public readonly InputSlot<bool> ShowTimeCode = new InputSlot<bool>();


    }
}

