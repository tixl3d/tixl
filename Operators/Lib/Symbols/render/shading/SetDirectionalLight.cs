using T3.Core.Rendering;

namespace Lib.render.shading;

[Guid("39b8b6a2-59a8-49b2-b375-bed3bf3914d6")]
internal sealed class SetDirectionalLight : Instance<SetDirectionalLight>, ITransformable
{
    [Output(Guid = "e1c1793a-5443-45e9-926d-26047eaac263", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TransformCallbackSlot<Command> Output = new();

    public SetDirectionalLight()
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
        var light = Light.CreateDirectional(Target.GetValue(context) - Position.GetValue(context),
                                            Intensity.GetValue(context),
                                            Color.GetValue(context));
        var wasPushed = lights.Push(light, this);

        Command.GetValue(context); // Evaluate sub-tree

        if (wasPushed)
            lights.Pop();
    }

    [Input(Guid = "36a4ed94-7236-4bfb-8eb4-eab22b733186")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "51920d4b-5769-4c55-8c9b-534e7a237fa6")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "ffb89029-2133-4b78-9444-776d2394d485")]
    public readonly InputSlot<Vector3> Target = new();

    [Input(Guid = "290fa10c-5ff3-481e-a2c8-b01c9b792ff6")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "558129dc-8c41-4218-8ba0-9a9c9d4ea59f")]
    public readonly InputSlot<Vector4> Color = new();
}
