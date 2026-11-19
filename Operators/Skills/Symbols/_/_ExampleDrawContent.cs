using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Skills._{
    [Guid("dfae06ee-91b8-4834-85c5-a8a59d0ba55e")]
    internal sealed class _ExampleDrawContent :Instance<_ExampleDrawContent>{

        [Output(Guid = "56fd9a58-6e11-4322-b8d9-bb99b6f3b45f")]
        public readonly Slot<T3.Core.DataTypes.Command> DrawOutput = new Slot<T3.Core.DataTypes.Command>();

        [Input(Guid = "ce75d9a9-e03a-4275-83c7-c6095e5f4575")]
        public readonly InputSlot<int> Type = new InputSlot<int>();


    }
}

