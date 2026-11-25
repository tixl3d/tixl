using T3.Core.Rendering;

namespace Lib.render.shading;

[Guid("0c17e5f9-266a-480f-a48b-9bf2f69eb383")]
internal sealed class SetSpotLight : Instance<SetSpotLight>, ITransformable
{
    [Output(Guid = "c33f48f2-ad65-48ec-80e0-2407e902a421", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TransformCallbackSlot<Command> Output = new();

    public SetSpotLight()
    {
        Output.TransformableOp = this;
        Output.UpdateAction += Update;
    }

    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    IInputSlot ITransformable.AimTargetInput => Target;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var lights = context.Lights;
        var light = Light.CreateSpot(Position.GetValue(context),
                                     Target.GetValue(context),
                                     Intensity.GetValue(context),
                                     Color.GetValue(context),
                                     Range.GetValue(context),
                                     Decay.GetValue(context),
                                     ConeAngle.GetValue(context),
                                     Softness.GetValue(context));
        var wasPushed = lights.Push(light);

        Command.GetValue(context); // Evaluate sub-tree

        if (wasPushed)
            lights.Pop();
    }

    [Input(Guid = "24eb2ff1-8a20-4178-ac1a-b43ad6001347")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "482d4b4e-403f-4f7d-beb1-36165acb620b")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "aff99952-a008-45c9-9705-f5106b702313")]
    public readonly InputSlot<Vector3> Target = new();

    [Input(Guid = "f4d0b3b9-359e-426c-b91a-28e6965f49f6")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "1d5ad697-5590-4d06-a1e9-caa3356b641d")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "cf141c64-fd2f-405e-bee1-38d294999dc2")]
    public readonly InputSlot<float> ConeAngle = new();

    [Input(Guid = "84cb4172-56c2-4b91-86b0-951b3656be2a")]
    public readonly InputSlot<float> Softness = new();

    [Input(Guid = "a71b26df-b98a-42d5-bdf9-de1fba8798d2")]
    public readonly InputSlot<float> Range = new();

    [Input(Guid = "91e532b4-3507-449c-a61f-01826b064611")]
    public readonly InputSlot<float> Decay = new();
}
