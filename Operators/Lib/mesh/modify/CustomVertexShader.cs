using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.mesh.modify{
    [Guid("b722014e-402f-4089-a876-16aefab3c012")]
    internal sealed class CustomVertexShader :Instance<CustomVertexShader>{
        [Output(Guid = "7c7944ab-cb25-4bdc-9359-269836526821")]
        public readonly Slot<MeshBuffers> MeshBuffers = new Slot<MeshBuffers>();

        [Output(Guid = "97de5979-cc40-49b5-baad-0269eac61b46")]
        public readonly Slot<string> GeneratedCode = new Slot<string>();

        [Input(Guid = "ad6e1865-1e63-4a85-9eee-36172e5f6a19")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "fbd20ff6-1e88-4d1a-9cb2-1767fc351cbc")]
        public readonly InputSlot<bool> SplitVertices = new InputSlot<bool>();

        [Input(Guid = "b7dfa99a-9e7c-42aa-8706-62ef6c4ebb17")]
        public readonly InputSlot<bool> RecomputeNormals = new InputSlot<bool>();

        [Input(Guid = "3104dab4-8d71-44aa-a582-47416aa977b5")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "0359d61a-4058-43ab-b997-1dadf5c2af35")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "353f527e-7593-45fd-9daf-eb46b965ee6e")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "946d47f3-9fcf-4f2c-b7c9-26d94ddb4613")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "b49f95cf-94c5-46cb-96c0-684f7e6669ed")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a76dbb53-092c-4398-9e51-889d4785d60d")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "c99fa997-9f97-4b5f-a90f-801280662268")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "074aed08-271d-4f2b-869f-1e2e869fdba5")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "e5930f87-e17b-48d5-9fb2-451da46968b5")]
        public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();

        [Input(Guid = "db40c298-be64-4710-846e-f7fd75b48bbf")]
        public readonly InputSlot<string> ShaderTemplate = new InputSlot<string>();

        [Input(Guid = "e3cbd904-b9c2-4461-8be3-c097bf7f70f6")]
        public readonly InputSlot<bool> IgnoreTemplate = new InputSlot<bool>();

        [Input(Guid = "eecf7eeb-fefc-4821-b280-be2abd79a2b0")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ShaderGraph = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();


    }
}

