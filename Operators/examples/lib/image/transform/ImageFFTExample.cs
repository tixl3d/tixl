using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.transform{
    [Guid("9f3e7a8b-83e9-45f5-a347-7a143f31da78")]
    internal sealed class ImageFFTExample : Instance<ImageFFTExample>
    {

        [Output(Guid = "e83e75b9-ff3e-493d-b954-e990b351a031")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ColorBuffer = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

