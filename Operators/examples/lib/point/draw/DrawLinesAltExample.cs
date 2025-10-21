using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.draw{
    [Guid("fc7fdcf6-8efa-4cec-82b6-cb6ce0696cc0")]
    internal sealed class DrawLinesAltExample : Instance<DrawLinesAltExample>
    {
        [Output(Guid = "9840db25-c425-4f3c-a163-20f535855b03")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

