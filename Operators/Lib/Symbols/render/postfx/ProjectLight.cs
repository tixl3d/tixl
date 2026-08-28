using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.render.postfx{
    [Guid("7eb5ee83-2b5c-43bc-95d4-af5e18c85bc5")]
    internal sealed class ProjectLight : Instance<ProjectLight>
    {
        [Output(Guid = "22d25ec9-0e24-429e-982b-e59e9cd3707f")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();

        [Input(Guid = "85f6f611-b977-43dd-910b-82f9d2a3e677")]
        public readonly InputSlot<T3.Core.DataTypes.Command> Scene = new InputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "7fbd1079-1b2d-4987-9635-8f52b9f52bbd")]
        public readonly InputSlot<Object> ViewCamReference = new InputSlot<Object>();

        [Input(Guid = "6e694b7a-6da8-4bf1-9687-c6fe37747ae6")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "394ee482-17f8-427f-9264-8a49b6c11d52", MappedType = typeof(ProjectorTypes))]
        public readonly InputSlot<int> ProjectorType = new InputSlot<int>();

        [Input(Guid = "04ae22f3-e6eb-49d8-b52a-e51c210afd13")]
        public readonly InputSlot<System.Numerics.Vector3> Position = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "c01e066b-5e18-4367-8dc7-e31775798f0d")]
        public readonly InputSlot<System.Numerics.Vector3> Target = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "7244df39-5b78-4cfb-9755-cd238df6c466")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "89ecd151-6b40-48aa-b5ea-a4fdd75769a2")]
        public readonly InputSlot<float> Roll = new InputSlot<float>();

        [Input(Guid = "2d7ab50a-6a59-4031-b2e7-dbc33483d8e0")]
        public readonly InputSlot<System.Numerics.Vector4> LightColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "c358b13e-7715-4342-b6e9-54cfcb60d03c")]
        public readonly InputSlot<float> RayIntensity = new InputSlot<float>();

        [Input(Guid = "e0321f7f-b694-432f-a13a-85ea68dcd300")]
        public readonly InputSlot<float> RaysDecay = new InputSlot<float>();

        [Input(Guid = "03813cbd-06d7-4937-a843-d97d83170c05")]
        public readonly InputSlot<int> StepCount = new InputSlot<int>();

        [Input(Guid = "584ded98-20a2-4ee3-963d-168fc9920abe")]
        public readonly InputSlot<System.Numerics.Vector4> AmbientColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "b4a19644-701a-48aa-94bc-f140b407b267")]
        public readonly InputSlot<float> SurfaceIntensity = new InputSlot<float>();

        [Input(Guid = "01b6437a-a4ce-4311-afcb-68640300ec64")]
        public readonly InputSlot<int> ShadowResolution = new InputSlot<int>();

        [Input(Guid = "e14dcc6d-da26-4950-940f-260a58f731e7")]
        public readonly InputSlot<float> ShadowBias = new InputSlot<float>();

        [Input(Guid = "cfd8c330-34ce-44de-8604-814b8594c801")]
        public readonly InputSlot<float> ShadowForm = new InputSlot<float>();


        private enum ProjectorTypes
        {
            Orthographic,
            SpotLight,
        }
    }
}

