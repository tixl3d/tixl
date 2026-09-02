using System.Numerics;
using T3.Core.DataTypes;
using System;
using System;
using System.Numerics;
using System;
using System;
using System;
using System.Numerics;
using System.Numerics;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.mesh.generate{
    [Guid("fcd806aa-1b17-4e2a-a5a5-57940f2411fd")]
    internal sealed class RadialRepeatMesh :Instance<RadialRepeatMesh>    {
        [Output(Guid = "88f12fdf-e093-4b04-94ea-a3af712ce814")]
        public readonly Slot<MeshBuffers> Result = new Slot<MeshBuffers>();

        [Input(Guid = "b4765f71-c9ed-4eb0-bb8b-37db6427eed9")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> InputMesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "5e514c3a-e7bf-4fe7-990c-4c9d0f8807ba")]
        public readonly InputSlot<int> Count = new InputSlot<int>();

        [Input(Guid = "c0ec973d-06bc-4d59-bb6d-1eaf595107e0")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "60789cd7-2ca3-4518-898c-1745fa4ce189")]
        public readonly InputSlot<float> OffsetRadius = new InputSlot<float>();

        [Input(Guid = "65bf5bbd-26c1-4661-84fc-822282031d88")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "f44902d8-1d0d-4a20-8e58-76fe49b87841")]
        public readonly InputSlot<System.Numerics.Vector3> Axis = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a9ff182f-ba2a-4760-b70a-c548c8f9f23b")]
        public readonly InputSlot<System.Numerics.Vector3> OffsetCenter = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "d47cf4f1-2bc0-47d9-9926-1acdedfe7f20")]
        public readonly InputSlot<float> StartAngle = new InputSlot<float>();

        [Input(Guid = "e7243123-c822-4b54-b0db-cc1fefda02e2")]
        public readonly InputSlot<float> Rotations = new InputSlot<float>();

        [Input(Guid = "0fc874f8-bb10-4de5-b9bd-b54f0362e50e")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "0ac072c5-e4f4-4f67-8149-b9f2fdcbb6e4")]
        public readonly InputSlot<System.Numerics.Vector3> Stretch = new InputSlot<System.Numerics.Vector3>();

    }
}

