namespace Lib.field.generate.sdf._;

[Guid("cea24093-69f0-4905-a695-a0da2e47abc9")]
internal sealed class JonBakerSDFLoader : Instance<JonBakerSDFLoader>
{

        [Input(Guid = "66a854bc-0152-4efc-b8e6-155ac2beb982")]
        public readonly InputSlot<int> ListSelect = new InputSlot<int>();

        [Input(Guid = "de7e613a-1fb9-4939-b423-a15a6a64c328")]
        public readonly InputSlot<int> RowSelect = new InputSlot<int>();

        [Input(Guid = "16b6b3d0-d367-428f-baca-916cce98232b")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "033447bc-93c2-4014-a614-579613ca3386")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "0bb61cf3-1ed4-4236-ba5c-8b05f066df0b")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "ff2623fe-6f91-497b-99db-fa11acbb6257")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Output(Guid = "77767d68-9c21-4dae-ad64-4fc6e268592b")]
        public readonly Slot<T3.Core.DataTypes.ShaderGraphNode> Result = new Slot<T3.Core.DataTypes.ShaderGraphNode>();


}