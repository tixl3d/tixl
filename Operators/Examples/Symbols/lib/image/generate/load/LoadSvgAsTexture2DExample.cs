using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.generate.load{
    [Guid("88c63a7f-7dc7-4e9d-8122-7c0a0dc29faa")]
    internal sealed class LoadSvgAsTexture2DExample : Instance<LoadSvgAsTexture2DExample>
    {
        [Output(Guid = "f7176f9f-9bb7-4833-b373-ea2122280fb1")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

