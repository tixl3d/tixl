using T3.Core.Utils;
using T3.Core.Utils.Math3d;

namespace Lib.render.transform;

[Guid("5c553e6a-923d-4b7b-970b-ecdfa45a88a7")]
internal sealed class RepeatTransform : Instance<RepeatTransform>, ITransformable
{
    [Output(Guid = "0bf333fa-8d25-4c3b-880b-9a0a09992311")]
    public readonly Slot<Command> Output = new();

    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public RepeatTransform()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context);

        var count = Count.GetValue(context).Clamp(0, MaxCount);
        if (count == 0)
            return;

        var mode = Mode.GetEnumValue<Modes>(context);
        var pivot = Pivot.GetValue(context);
        var scale = Scale.GetValue(context) * UniformScale.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var translation = Translation.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        var gradient = Gradient.GetValue(context);
        var scatterTranslation = ScatterTranslation.GetValue(context);
        var scatterRotation = ScatterRotation.GetValue(context);
        var scatterScale = ScatterScale.GetValue(context);
        var seed = Seed.GetValue(context);
        var strength = Strength.GetValue(context);
        translation *= strength;
        rotation *= strength;
        scale = PowScale(scale, strength);
        scatterTranslation *= strength;
        scatterRotation *= strength;
        scatterScale *= strength;

        var hasScatter = scatterTranslation != Vector3.Zero || scatterRotation != Vector3.Zero || scatterScale != 0;

        // Shifting every copy by a whole index yields the same set of positions, so only the fraction matters.
        // Ramping it moves the copies along the path while the last one wraps back to the start.
        var indexOffset = IndexOffset.GetValue(context);
        var wholeOffset = MathF.Floor(indexOffset);
        var offsetFraction = indexOffset - wholeOffset;

        var parentToWorld = context.ObjectToWorld;
        var parentColor = context.ForegroundColor;

        var step = Matrix4x4.Identity;
        var fractionalStep = Matrix4x4.Identity;
        if (mode == Modes.Accumulate)
        {
            step = CreateMatrix(pivot, scale, rotation, translation);
            fractionalStep = CreateMatrix(pivot, PowScale(scale, offsetFraction), rotation * offsetFraction, translation * offsetFraction);
        }

        var accumulatedSteps = Matrix4x4.Identity;
        var lastDrawnColor = parentColor;
        var progressRange = Math.Max(count - 1, 1);

        for (var index = 0; index < count; index++)
        {
            var progress = (index + offsetFraction) / progressRange;

            Matrix4x4 objectToParent;
            if (mode == Modes.Accumulate)
            {
                objectToParent = Matrix4x4.Multiply(fractionalStep, accumulatedSteps);
                accumulatedSteps = Matrix4x4.Multiply(step, accumulatedSteps);
            }
            else
            {
                // Beyond the last copy, keep moving linearly until the copy wraps to the start.
                var f = progress <= 1 ? progress.ApplyGainAndBias(gainAndBias.X, gainAndBias.Y) : progress;

                // Lerping euler angles instead of slerping keeps rotations beyond 180° spinning all the way.
                objectToParent = CreateMatrix(pivot, Vector3.Lerp(Vector3.One, scale, f), rotation * f, translation * f);
            }

            if (hasScatter)
            {
                // Keyed to the copy rather than its slot, so scrolling copies keep their scatter until they wrap.
                var copyId = index - (int)wholeOffset;
                var scatter = CreateMatrix(pivot,
                                           Vector3.One * MathF.Max(0, 1 + scatterScale * SignedRandom(seed, copyId, 6)),
                                           scatterRotation * SignedRandom3(seed, copyId, 3),
                                           scatterTranslation * SignedRandom3(seed, copyId, 0));
                objectToParent = Matrix4x4.Multiply(scatter, objectToParent);
            }

            var color = parentColor * gradient.Sample(progress);
            if (color.W <= 0)
                continue;

            context.ForegroundColor = color;

            // Draw ops cache the foreground color until their graph is invalidated, which costs a walk per copy.
            if (index > 0 && color != lastDrawnColor)
            {
                DirtyFlag.GlobalInvalidationTick++;
                Command.InvalidateGraph();
            }

            lastDrawnColor = color;

            context.ObjectToWorld = Matrix4x4.Multiply(objectToParent, parentToWorld);
            Command.GetValue(context);
        }

        context.ObjectToWorld = parentToWorld;
        context.ForegroundColor = parentColor;
    }

    private static Matrix4x4 CreateMatrix(Vector3 pivot, Vector3 scale, Vector3 rotationDegrees, Vector3 translation)
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(rotationDegrees.Y.ToRadians(),
                                                         rotationDegrees.X.ToRadians(),
                                                         rotationDegrees.Z.ToRadians());
        return GraphicsMath.CreateTransformationMatrix(scalingCenter: pivot,
                                                       scalingRotation: Quaternion.Identity,
                                                       scaling: scale,
                                                       rotationCenter: pivot,
                                                       rotation: rotation,
                                                       translation: translation);
    }

    private static Vector3 SignedRandom3(int seed, int copyId, int channel)
    {
        return new Vector3(SignedRandom(seed, copyId, channel),
                           SignedRandom(seed, copyId, channel + 1),
                           SignedRandom(seed, copyId, channel + 2));
    }

    /** A stable random value in -1..1 for each seed, copy and channel. */
    private static float SignedRandom(int seed, int copyId, int channel)
    {
        var hash = MathUtils.XxHash((uint)(copyId * ScatterChannelCount + channel) ^ MathUtils.XxHash((uint)seed));
        return (float)((double)hash / uint.MaxValue) * 2 - 1;
    }

    /** Scaling by s^f for a fraction of a step keeps the spacing of accumulated scales even. */
    private static Vector3 PowScale(Vector3 scale, float fraction)
    {
        return new Vector3(PowScale(scale.X, fraction), PowScale(scale.Y, fraction), PowScale(scale.Z, fraction));
    }

    private static float PowScale(float scale, float fraction)
    {
        return scale > 0 ? MathF.Pow(scale, fraction) : MathUtils.Lerp(1, scale, fraction);
    }

    private enum Modes
    {
        Accumulate,
        Lerp,
    }

    /** Guards against a typo like 100000 stalling the editor with one draw call per copy. */
    private const int MaxCount = 10000;

    private const int ScatterChannelCount = 7;

    [Input(Guid = "8dbe4378-7903-42c6-b143-5f2cdc8ea0c4")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "71fdc9bc-b32b-4f55-9d24-18cca0d24d89")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "396fcf8c-af38-4e7e-9bc9-7a9df0d96a47")]
    public readonly InputSlot<float> IndexOffset = new();

    [Input(Guid = "9b04714f-e5c6-43cc-9d0c-bf83e9bea601", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "3ed374ce-87f8-4a89-964d-c12500e7da80")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "3a5ae751-df04-4378-849b-ab141b3ab340")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "62b3b341-7dea-4817-873a-81d0b7aedf01")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "81f91ea5-ce6b-4d04-9d60-2522deba6dd2")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "b96a55d9-135b-4b43-8d7a-16a35ab9586d")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "2a8f6300-412e-46cb-8660-3b9483c1d7d2")]
    public readonly InputSlot<Vector3> Pivot = new();

    [Input(Guid = "52faaff0-cfc0-46ae-8010-e33e38cd5153")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "c3e18172-2496-48ce-b7f7-3d9b5e1340a3")]
    public readonly InputSlot<Vector3> ScatterTranslation = new();

    [Input(Guid = "6171ecb0-c774-4f85-a5c9-0d56cbef0723")]
    public readonly InputSlot<Vector3> ScatterRotation = new();

    [Input(Guid = "a1d06737-6411-4021-a3c4-fcf973866c0c")]
    public readonly InputSlot<float> ScatterScale = new();

    [Input(Guid = "b528461b-e8d0-4525-a628-08da4b60493c")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "c874d12d-f799-474f-86b6-afe763fa9ad1")]
    public readonly InputSlot<Gradient> Gradient = new();
}
