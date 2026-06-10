using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.io.video{
    [Guid("f98d28ed-5f30-4416-93be-f0d7a15a7f77")]
    internal sealed class VideoClipPlayer :Instance<VideoClipPlayer>    {
        [Output(Guid = "7edfda73-1877-4463-85ad-7ec4182c0385")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();

        [Input(Guid = "7b80b49f-c5f5-4c86-8c12-f854fff027c2")]
        public readonly MultiInputSlot<T3.Core.DataTypes.Texture2D> VideoClips = new MultiInputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "ac80c531-90ff-449a-8d60-f6a4fa27b818")]
        public readonly InputSlot<bool> AutoCollect = new InputSlot<bool>();

        [Input(Guid = "f2ca9226-b302-4a77-8c8b-1b1db6022eee", MappedType = typeof(ScaleModes))]
        public readonly InputSlot<int> ScaleMode = new InputSlot<int>();

        public enum ScaleModes
        {
            FitHeight,
            FitWidth,
            FitBoth,
            Cover,
            Stretch,
            MatchPixelResolution,
        }
    }
}

