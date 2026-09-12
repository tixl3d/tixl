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
    [Guid("638b60a4-5c91-46a6-8210-30d85478787c")]
    internal sealed class GridRepeatMesh :Instance<GridRepeatMesh>{
        [Output(Guid = "bf925a49-d65d-45cf-beff-300ae2443a6b")]
        public readonly Slot<MeshBuffers> Result = new Slot<MeshBuffers>();

        [Input(Guid = "a1584c44-1709-4212-b365-6200d8a40711")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> InputMesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "ce6259cf-7384-4192-8e82-ea528da6ea8f")]
        public readonly InputSlot<int> CountX = new InputSlot<int>();

        [Input(Guid = "9d3c333a-be60-4152-a28c-90239041c92a")]
        public readonly InputSlot<int> CountY = new InputSlot<int>();

        [Input(Guid = "3c0bd4c4-6547-4fba-83ba-76b0e6bbdc18")]
        public readonly InputSlot<int> CountZ = new InputSlot<int>();

        [Input(Guid = "4c27b9bb-05bf-4895-86c1-fe78e21b55d1")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "f8a33ecb-3ea4-4bad-8650-7047fe703b83")]
        public readonly InputSlot<System.Numerics.Vector3> Stretch = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "88fcb28a-e8ff-4d55-ab95-d42ba64f1338")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "5cff5cf0-dcc8-46dc-850d-06d91bb0491c")]
        public readonly InputSlot<System.Numerics.Vector3> OffsetCenter = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a8100674-c59c-4a80-909d-407bf405bfc4")]
        public readonly InputSlot<float> RandomizeStrength = new InputSlot<float>();

        [Input(Guid = "ed498e82-9712-4bda-b64b-c720f230e8c3")]
        public readonly InputSlot<System.Numerics.Vector3> RandomizePosition = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "9d4cacc5-a44a-4c41-b640-5af8ca7f8a67")]
        public readonly InputSlot<System.Numerics.Vector3> RandomizeRotation = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "72cc9d33-541a-40b1-811c-af6de9db67a0")]
        public readonly InputSlot<float> RandomPhase = new InputSlot<float>();

    }
}

