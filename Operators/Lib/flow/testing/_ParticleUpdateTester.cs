using T3.Core.DataTypes;
using System;
using T3.Core.DataTypes;
using System;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.flow.testing{
    [Guid("9b550333-1caa-43d4-9e6f-245c55c2d24a")]
    internal sealed class _ParticleUpdateTester : Instance<_ParticleUpdateTester>
    {
        [Output(Guid = "b0e73991-3f04-401d-b909-ab29ed6f4c13")]
        public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "5ec058e5-e6cb-492c-8ab3-58fd2948feee")]
        public readonly InputSlot<int> FrameDropRate = new InputSlot<int>();

        [Input(Guid = "d4e3dafa-3595-489e-a7db-cf0cb9cd258d")]
        public readonly InputSlot<System.Numerics.Vector4> PointColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "4b21554e-a551-43aa-8fb1-b3b2794977e4")]
        public readonly InputSlot<float> PointSize = new InputSlot<float>();

    }
}

