using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.mesh.generate{
    [Guid("1bd6ebd0-0414-4d29-99df-4f4e123e0570")]
    internal sealed class LoadAnimatedGltf : Instance<LoadAnimatedGltf>
    {
        [Output(Guid = "dd53cc7e-736c-44dc-8c3e-ad79c919f35a")]
        public readonly Slot<MeshBuffers> Result = new Slot<MeshBuffers>();

        [Input(Guid = "b9ee9f01-0c73-45c7-a72d-5ce28cb56e3c")]
        public readonly InputSlot<int> ClipIndex = new InputSlot<int>();


    }
}

