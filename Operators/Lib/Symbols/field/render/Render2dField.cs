namespace Lib.field.render;

[Guid("0a2d0f9f-2737-4f19-9b2e-38348b2ab54e")]
public class Render2dField : Instance<Render2dField>
{
    [Output(Guid = "d22a2c50-29f3-4660-8bcc-239abca7c90c")]
    public readonly Slot<Command> DrawCommand = new();

    [Input(Guid = "d97e99b2-dbb5-4356-a961-1eeecdadee5e")]
    public readonly InputSlot<float> MaxSteps = new();

    [Input(Guid = "fd469850-5c71-4a22-add4-d6a61d3ce6e7")]
    public readonly InputSlot<float> StepSize = new();

    [Input(Guid = "4306eb13-37d2-4901-a134-24c18d66bea4")]
    public readonly InputSlot<float> MinDistance = new();

    [Input(Guid = "15cbc08f-bb79-4ad3-a330-fb912ce6e31b")]
    public readonly InputSlot<float> MaxDistance = new();

    [Input(Guid = "56114154-e78f-4ff7-a19e-9a0d448fc7e9")]
    public readonly InputSlot<System.Numerics.Vector4> Specular = new();

    [Input(Guid = "20b308a9-dad2-48fa-8cc6-0cdcd1d3afe2")]
    public readonly InputSlot<System.Numerics.Vector4> Glow_ = new();

    [Input(Guid = "4abd70b5-0302-4d07-bcc1-86f4629e66fb")]
    public readonly InputSlot<System.Numerics.Vector4> AmbientOcclusion = new();

    [Input(Guid = "d3fde3e0-850c-417b-84f3-6c9c4bdcc143")]
    public readonly InputSlot<System.Numerics.Vector4> Background = new();

    [Input(Guid = "d6d2b97d-1722-4190-a7f9-ebe07cded454")]
    public readonly InputSlot<System.Numerics.Vector2> Spec = new();

    [Input(Guid = "cff144b3-bb6f-412a-89cf-6ef1b70d4ffd")]
    public readonly InputSlot<float> AoDistance = new();

    [Input(Guid = "2119f1d1-c08e-4fed-af9d-4e54b6c3cbb1")]
    public readonly InputSlot<float> Fog = new();

    [Input(Guid = "76a5c2d6-a1a8-4d76-80f1-d6f16d7114c7")]
    public readonly InputSlot<System.Numerics.Vector3> LightPos = new();

    [Input(Guid = "6404b161-74da-429d-be09-8a677c8beb7e")]
    public readonly InputSlot<float> DistToColor = new();

        [Input(Guid = "7e97e88d-4b66-4232-9c65-357948554fdd")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ColorField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();
}