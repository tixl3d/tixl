using T3.Core.Rendering;

namespace Lib.render.shading.@_;

[Guid("e0fb7629-b49d-4aac-9bdd-b746e0a58699")]
internal sealed class SetShadowParameters : Instance<SetShadowParameters>
{
    [Output(Guid = "501b2de6-0d8e-4c64-8948-94bfc5484f23")]
    public readonly Slot<Command> Output = new();

    public SetShadowParameters()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var color = Color.GetValue(context);
        var hasLightMatrix = TryGetMatrix(WorldToLightClipSpace.GetValue(context), out var worldToLightClipSpace);
        if (!hasLightMatrix)
            color.W = 0;

        var parameters = new ShadowSettings.ShadowParameters
                             {
                                 WorldToLightClipSpace = worldToLightClipSpace,
                                 Color = color,
                                 Bias = Bias.GetValue(context),
                                 NormalOffset = NormalOffset.GetValue(context),
                             };

        if (_parameterBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _parameterBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _parameterBuffer);
        }

        var previousShadowMap = context.ShadowMap;
        var previousParameters = context.ShadowParameters;
        context.ShadowMap = ShadowMap.GetValue(context);
        context.ShadowParameters = _parameterBuffer;

        SubTree.GetValue(context);

        context.ShadowMap = previousShadowMap;
        context.ShadowParameters = previousParameters;
    }

    /** [SetMatrixVar] stores a matrix as its four rows. */
    private static bool TryGetMatrix(Vector4[] rows, out Matrix4x4 matrix)
    {
        if (rows == null || rows.Length < 4)
        {
            matrix = Matrix4x4.Identity;
            return false;
        }

        matrix = new Matrix4x4(rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
                               rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
                               rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
                               rows[3].X, rows[3].Y, rows[3].Z, rows[3].W);
        return true;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _parameterBuffer);
    }

    private Buffer _parameterBuffer;

    [Input(Guid = "d342a295-34d7-4120-aa53-b4a2c04be509")]
    public readonly InputSlot<Command> SubTree = new();

    [Input(Guid = "b838fceb-f2dd-49d1-ab99-642ada52d5a7")]
    public readonly InputSlot<ShaderResourceView> ShadowMap = new();

    [Input(Guid = "5f776e79-4b23-49c3-b678-ecd945e62656")]
    public readonly InputSlot<Vector4[]> WorldToLightClipSpace = new();

    [Input(Guid = "cb509c95-2d3c-4bb0-8a94-7595c6bebe84")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "da489523-8ca0-489a-ab99-02e7362a1483")]
    public readonly InputSlot<float> Bias = new();

    [Input(Guid = "392730c4-bdd5-4a6d-866f-630d26a4fc69")]
    public readonly InputSlot<float> NormalOffset = new();
}
