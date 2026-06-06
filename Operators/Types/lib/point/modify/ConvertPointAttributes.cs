using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Operators.Types.Id_c154b616_8aaf_4d43_ae95_035951b531c8
{
    public class ConvertPointAttributes : Instance<ConvertPointAttributes>
    {

        [Output(Guid = "5ea9c3fa-7c27-4d4f-a056-c37b70ae4796")]
        public readonly Slot<T3.Core.DataTypes.BufferWithViews> Output = new();

        [Input(Guid = "cace5c6d-5411-4835-97c0-b2690b4e1b26")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "c1631db3-ba6f-4bd5-badf-30309b001d60", MappedType =typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "ec70d9cc-d0ed-4490-8f50-542e442f38cd", MappedType = typeof(ConvertOptions))]
        public readonly InputSlot<int> ConvertFrom = new InputSlot<int>();

        [Input(Guid = "035af505-295a-4ecd-9230-8e1e22f38c4a", MappedType = typeof(ConvertOptions))]
        public readonly InputSlot<int> ConvertTo = new InputSlot<int>();

        [Input(Guid = "ab049e91-c918-43f2-97b5-457c4eaffa89")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "2d55ff51-5f1b-421a-95ab-9c18d34b0efe")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();



        private enum ConvertOptions
        {
            Position_X,
            Position_Y,
            Position_Z,
            Rotation_X,
            Rotation_Y,
            Rotation_Z,
            Stretch_X,
            Stretch_Y,
            Stretch_Z,
            Color_R,
            Color_G,
            Color_B,
            Color_A,
            W,
        }
        
        private enum Modes
        {
            Replace,
            Add,
            Multiply,
        }
    }
}

