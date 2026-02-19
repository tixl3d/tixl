using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.image.transform{
    [Guid("40fc6dd9-5480-414d-8204-07e8142215a9")]
    internal sealed class ImageFFT :Instance<ImageFFT>    {

        [Input(Guid = "4f4727d7-f4f6-430d-968b-bc5afcddb176")]
        public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

        [Input(Guid = "132ab261-66d9-4e1d-98e5-b719d40b4668")]
        public readonly InputSlot<bool> Inverse = new InputSlot<bool>();

        [Input(Guid = "421fe2f3-7700-4479-9842-080106c4eb6d", MappedType = typeof(FFTDirection))]
        public readonly InputSlot<int> Direction = new InputSlot<int>();

        [Input(Guid = "21860604-3c19-4265-87d2-425c2f421646", MappedType = typeof(FFTNormalization))]
        public readonly InputSlot<int> Normalization = new InputSlot<int>();

        [Output(Guid = "d007f87a-487d-4d7c-a41e-21ce01bc89b1")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Output = new Slot<T3.Core.DataTypes.Texture2D>();

    private enum FFTDirection
    {
        Horizontal,
        Vertical,
        Both,
    }
    private enum FFTNormalization
    {
        Ortho,
        Backward,
        Forward,
    }
    
    }

}

