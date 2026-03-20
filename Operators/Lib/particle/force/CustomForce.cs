namespace Lib.particle.force;

[Guid("2db5e90a-f02a-47ce-b1c0-3c5c4513f8fd")]
internal sealed class CustomForce :Instance<CustomForce>{
    [Output(Guid = "e2358e9b-9b37-44f7-b4f8-b56ef0eb7d83")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "18b352f3-0863-4ad4-a569-a63000841736")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "65f44e17-9aa0-4166-9cab-587ab80f6b4c")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "695907d4-fc4e-4326-9bdc-618784c84173")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "2261a69e-0862-41d2-b3a2-48b77ab8b873")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "38de95a0-1ef5-4bc6-b930-fb16c278c359")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "bf811157-9055-4d8c-94e9-db28893ef207")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "74befd45-8f75-4d01-afe9-689f5fd7ad85")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "7e4e6038-7d45-491b-a653-468f50a89f81")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "5de0657c-119a-441c-9876-e47191b5ac46")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "76299c6d-3add-4f26-a81c-ed628257ed8f")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "b70f8651-8a88-45da-87de-e5a5e8bd0163")]
        public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();

        [Input(Guid = "da22402b-decf-4f02-afc4-a0ce984062c1")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "c7e31ead-9bab-441e-af0b-bdaea7a60a79")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "76ecc997-76a8-477c-9d35-643d12994adb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}