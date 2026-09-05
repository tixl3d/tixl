using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lib.flow.skillQuest{
    [Guid("16e30ce0-20c9-4677-9837-0200454e05ae")]
    internal sealed class DrawQuiz :Instance<DrawQuiz>    {
        [Output(Guid = "63a74fd8-a6e7-4d6f-b63d-179d7ff8ca4f")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();

        [Input(Guid = "a6f445ac-1be1-4921-baee-cf2caf5a6a90")]
        public readonly InputSlot<T3.Core.DataTypes.Command> YourSolution = new InputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "8f4f0caa-b710-47c1-94dd-346f03fac3c8")]
        public readonly InputSlot<T3.Core.DataTypes.Command> DoNotChange = new InputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "804aba22-3b3d-4121-b754-82c3ed2d4566")]
        public readonly InputSlot<System.Numerics.Vector2> DifferenceRange = new InputSlot<System.Numerics.Vector2>();

    }
}

