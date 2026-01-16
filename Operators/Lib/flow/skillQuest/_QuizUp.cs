using T3.Core.DataTypes;
using T3.Core.DataTypes;
using System;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.flow.skillQuest{
    [Guid("b9460cd7-c363-4a6e-8c3e-5c2f84a4aae3")]
    internal sealed class _QuizUp : Instance<_QuizUp>
    {
        [Output(Guid = "09e96462-2b6c-4884-9dd5-1812f7112eca")]
        public readonly Slot<Command> Output = new Slot<Command>();

        [Output(Guid = "7b47a1bb-d523-455c-b3dd-69ad3dcaa3ca")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ImageOutput = new Slot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "a0fe04f8-9b1c-4097-a6f9-2c9d30828706")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> UserAttempt = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "94b5aa62-3dc4-4edb-a257-55326eb25dfb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> GoalTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "3e038d4b-6bbc-4ca2-85ac-0401cc750f4a")]
        public readonly InputSlot<float> Difference = new InputSlot<float>();

        [Input(Guid = "088ca109-0959-4e1d-a926-722e51ab8062")]
        public readonly InputSlot<System.Numerics.Vector2> DifferenceRange = new InputSlot<System.Numerics.Vector2>();

    }
}

