using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lib.flow.skillQuest{
    [Guid("b1cb8864-0d51-4b7f-96eb-4c6267d4b216")]
    internal sealed class ImageQuiz : Instance<ImageQuiz>
    {
        [Output(Guid = "25709fa8-cbd8-4e54-9f9e-f92a4b3e2f65")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();

        [Input(Guid = "a8d44123-99b4-4285-82f5-84531f0e27d3")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> YourSolution = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "629ec47f-64d7-4c05-a5b1-29fe9303c8eb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> DoNotChange = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "cf267a38-5504-4d0c-b149-17bbe62c70cf")]
        public readonly InputSlot<System.Numerics.Vector2> DifferenceRange = new InputSlot<System.Numerics.Vector2>();

    }
}

