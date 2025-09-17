namespace Lib.field.render;

[Guid("9323e32f-078c-4156-941b-203f4c265ff5")]
public class RaymarchField : Instance<RaymarchField>
{
    [Output(Guid = "e178ef02-c9ac-48cd-a8cb-df3aec5941bb")]
    public readonly Slot<Command> DrawCommand = new();

        [Output(Guid = "8e293517-dc6f-4b1f-9710-069420f9da09")]
        public readonly Slot<string> ShaderCode = new Slot<string>();

        [Input(Guid = "340ca675-9356-4548-ba64-732181bebeef")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "9715075b-b02b-4290-9332-9bbfe67933f2")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "e3a85c27-b94c-4e77-b0c2-4644cd3a22d4")]
        public readonly InputSlot<System.Numerics.Vector4> AmbientOcclusion = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "ffb73f4d-6d24-4f4c-866b-5bdd6f876e6f")]
        public readonly InputSlot<float> AoDistance = new InputSlot<float>();

        [Input(Guid = "89218016-a0ca-4150-95d4-23f415cf07f0")]
        public readonly InputSlot<float> TextureScale = new InputSlot<float>();

        [Input(Guid = "3a23730d-09b9-44bd-84b7-c252dd83e1f9", MappedType = typeof(MappingModes))]
        public readonly InputSlot<int> UVMapping = new InputSlot<int>();

        [Input(Guid = "f14e7a2f-cd4e-4399-b137-ea0b87c7dfbd")]
        public readonly InputSlot<float> NormalSamplingD = new InputSlot<float>();

        [Input(Guid = "3148d927-8779-47ab-9e0a-fa63206f3002")]
        public readonly InputSlot<float> MaxSteps = new InputSlot<float>();

        [Input(Guid = "0b4d60de-261f-4dbf-ad44-6395cda3a496")]
        public readonly InputSlot<float> MinDistance = new InputSlot<float>();

        [Input(Guid = "adeb374b-bce0-4af2-867b-efb3ce6289c9")]
        public readonly InputSlot<float> MaxDistance = new InputSlot<float>();

        [Input(Guid = "561768f6-adf6-4d3d-a36a-20b6f35ff151")]
        public readonly InputSlot<float> StepSize = new InputSlot<float>();

        [Input(Guid = "1251368b-f8f4-4210-be1e-4d05223caf21")]
        public readonly InputSlot<float> DistToColor = new InputSlot<float>();

        [Input(Guid = "0700d5cb-6a1e-43ad-b7fb-b9b7b1415584")]
        public readonly InputSlot<bool> WriteDepth = new InputSlot<bool>();

        private enum MappingModes
        {
            Global_Triplanar,
            Local_Triplanar,
            Local_XY,
            Local_YZ,
            Local_XZ,
        }
        
}