using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.mesh.modify{
    [Guid("f97bfe16-dd7f-49b9-8238-cea1ee13a66f")]
    internal sealed class CustomFaceShader :Instance<CustomFaceShader>    {
        [Output(Guid = "a4246b02-cacb-4c3d-9684-f34c9d7c07b5")]
        public readonly Slot<MeshBuffers> MeshBuffers = new Slot<MeshBuffers>();

        [Output(Guid = "f0663f25-ddaf-4e87-8a1e-086e923586be")]
        public readonly Slot<string> GeneratedCode = new Slot<string>();

        [Input(Guid = "495a3bac-394b-4f2d-a741-0cb923fc4ef3")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "63fcdf71-45ce-4abd-a0f4-28364939e065")]
        public readonly InputSlot<bool> SplitVertices = new InputSlot<bool>();

        [Input(Guid = "54059d29-e8d9-457c-9dff-25b23753872d")]
        public readonly InputSlot<bool> RecomputeNormals = new InputSlot<bool>();

        [Input(Guid = "cb9f9b0e-0486-400e-9fa1-5d2a997bb7d6")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "ee09aa68-6191-494f-9900-f0dff16df27d")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "043EF621-40FB-49DD-895B-EA8226D42D20")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "6643b344-a69d-4142-85a6-f7c642a1e93a")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "e7f9c927-5d70-40d2-b5e3-43bd06731bf7")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "13abde1f-3173-4614-a5d8-e2848e94236e")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "c79b7cd6-ca23-4e3b-8eb7-3468e157f592")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "0957ec56-8c74-4e72-aee3-c14abddcc655")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "1dc7a589-93f7-4e0b-acbd-02670c97ebf8")]
        public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();

        [Input(Guid = "a14d4ec8-33b8-487d-8855-a33088c8885b")]
        public readonly InputSlot<string> ShaderTemplate = new InputSlot<string>();

        [Input(Guid = "68ae89bb-31b4-4516-aeb5-6b9ac132cf38")]
        public readonly InputSlot<bool> IgnoreTemplate = new InputSlot<bool>();


    }
}

