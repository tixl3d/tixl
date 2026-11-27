#nullable enable
using T3.Core.Operator.Interfaces;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Math3d;
using T3.Graphics.Compat;

namespace Lib.render.shading;

/// <remarks>
/// Draws the subtree twice: first from the light into a depth-only shadow map with
/// <see cref="RenderPassState.IsShadowPass"/> set, then normally with the shadow map in
/// <see cref="EvaluationContext.ShadowMap"/>, where [DrawMesh] receives it.
/// </remarks>
[Guid("facb7925-176a-4eae-bedc-cdbf532ff6ff")]
internal sealed class SetShadow : Instance<SetShadow>, ICustomDropdownHolder, IStatusProvider
{
    [Output(Guid = "a0a1b038-8637-45af-89b5-dcef99f872f7", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "86d41b9d-fde7-41da-91e9-3708625b8b81")]
    public readonly Slot<Texture2D?> ShadowMap = new();

    public SetShadow()
    {
        Output.UpdateAction += Update;
        ShadowMap.UpdateAction += UpdateShadowMapOutput;
    }

    private void Update(EvaluationContext context)
    {
        CollectLightOptions(context);

        var resolution = Math.Clamp(Resolution.GetValue(context), MinResolution, MaxResolution);
        if (!TryGetLightCamera(context, out var worldToLight, out var lightToClipSpace) || !TryEnsureShadowMap(resolution))
        {
            Command.GetValue(context);
            return;
        }

        RenderShadowPass(context, resolution, worldToLight, lightToClipSpace);
        RenderReceivingPass(context, worldToLight * lightToClipSpace, 2 / (lightToClipSpace.M11 * resolution));
    }

    private void UpdateShadowMapOutput(EvaluationContext context)
    {
        ShadowMap.Value = _shadowMapTexture;
    }

    #region light camera
    private bool TryGetLightCamera(EvaluationContext context, out Matrix4x4 worldToCamera, out Matrix4x4 cameraToClipSpace)
    {
        _statusLevel = IStatusProvider.StatusLevel.Success;
        _statusMessage = null;
        _spotCosInner = NoSpotCone;
        _spotCosOuter = NoSpotCone;
        _tanHalfFieldOfView = 0;

        var depthRange = DepthRange.GetValue(context);
        var near = MathF.Max(depthRange.X, 0.0001f);
        var far = MathF.Max(depthRange.Y, near + 0.001f);
        var position = LightPosition.GetValue(context);
        var target = LightTarget.GetValue(context);
        var fieldOfViewDegrees = FieldOfView.GetValue(context);

        if ((Modes)Mode.GetValue(context) == Modes.FromLight)
        {
            if (TryFindLight(context, out var light))
            {
                switch (light.Type)
                {
                    case LightType.Spot:
                        position = light.Position;
                        target = light.Position + light.Direction;
                        fieldOfViewDegrees = GetSpotFieldOfViewDegrees(light);
                        _spotCosInner = light.SpotCosInner;
                        _spotCosOuter = light.SpotCosOuter;
                        break;

                    case LightType.Directional:
                        return TryBuildDirectionalCamera(light.Direction, target, AreaSize.GetValue(context), near, far,
                                                         out worldToCamera, out cameraToClipSpace);

                    default:
                        position = light.Position;
                        SetStatus(IStatusProvider.StatusLevel.Notice, PointLightMessage);
                        break;
                }
            }
            else
            {
                SetStatus(IStatusProvider.StatusLevel.Warning, NoLightMessage);
            }
        }

        return TryBuildPerspectiveCamera(position, target, fieldOfViewDegrees, near, far, out worldToCamera, out cameraToClipSpace);
    }

    private bool TryBuildPerspectiveCamera(Vector3 position, Vector3 target, float fieldOfViewDegrees, float near, float far,
                                           out Matrix4x4 worldToCamera, out Matrix4x4 cameraToClipSpace)
    {
        var direction = target - position;
        if (direction.LengthSquared() < 1e-10f)
        {
            SetStatus(IStatusProvider.StatusLevel.Warning, SamePositionMessage);
            worldToCamera = Matrix4x4.Identity;
            cameraToClipSpace = Matrix4x4.Identity;
            return false;
        }

        var fieldOfView = Math.Clamp(fieldOfViewDegrees, MinFieldOfViewDegrees, MaxFieldOfViewDegrees).ToRadians();
        worldToCamera = GraphicsMath.LookAtRH(position, target, GetUpVector(direction));
        cameraToClipSpace = GraphicsMath.PerspectiveFovRH(fieldOfView, 1, near, far);
        _tanHalfFieldOfView = MathF.Tan(fieldOfView * 0.5f);
        return true;
    }

    /** The box is centered on <paramref name="center"/> and reaches half the far distance towards the light. */
    private static bool TryBuildDirectionalCamera(Vector3 direction, Vector3 center, float areaSize, float near, float far,
                                                  out Matrix4x4 worldToCamera, out Matrix4x4 cameraToClipSpace)
    {
        var size = MathF.Max(areaSize, 0.001f);
        var eye = center - direction * (far * 0.5f);
        worldToCamera = GraphicsMath.LookAtRH(eye, center, GetUpVector(direction));
        cameraToClipSpace = Matrix4x4.CreateOrthographic(size, size, near, far);
        return true;
    }

    /** The cone plus a margin, so its soft edge doesn't reach the border of the shadow map. */
    private static float GetSpotFieldOfViewDegrees(in Light light)
    {
        var halfAngle = MathF.Acos(Math.Clamp(light.SpotCosOuter, -1f, 1f));
        return 2 * halfAngle * SpotFieldOfViewMargin * MathUtils.ToDegree;
    }

    private static Vector3 GetUpVector(Vector3 direction)
    {
        var isVertical = MathF.Abs(Vector3.Normalize(direction).Y) > 0.99f;
        return isVertical ? Vector3.UnitZ : Vector3.UnitY;
    }
    #endregion

    #region passes
    private void RenderShadowPass(EvaluationContext context, int resolution, Matrix4x4 worldToCamera, Matrix4x4 cameraToClipSpace)
    {
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.PushState(StateGroups.Rasterizer | StateGroups.OutputMerger);

        var keepWorldToCamera = context.WorldToCamera;
        var keepCameraToClipSpace = context.CameraToClipSpace;
        var keepBypassCameras = context.BypassCameras;
        var keepShowGizmos = context.ShowGizmos;
        var keepRenderPass = context.RenderPass;

        deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, resolution, resolution, 0.0f, 1.0f));
        deviceContext.OutputMerger.SetTargets(_shadowMapDsv);
        deviceContext.ClearDepthStencilView(_shadowMapDsv!, DepthStencilClearFlags.Depth, 1.0f, 0);

        context.WorldToCamera = worldToCamera;
        context.CameraToClipSpace = cameraToClipSpace;

        // Cameras inside would replace the light's view, and selected ops would draw their gizmos a second time.
        context.BypassCameras = true;
        context.ShowGizmos = GizmoVisibility.Off;

        context.RenderPass.IsShadowPass = true;
        context.RenderPass.VelocityRequested = false;
        context.RenderPass.MaterialRequested = false;
        context.RenderPass.JitterOffset = Vector2.Zero;
        context.RenderPass.ProjectionJitter = Vector2.Zero;

        EvaluateSubTree(context);

        context.RenderPass = keepRenderPass;
        context.ShowGizmos = keepShowGizmos;
        context.BypassCameras = keepBypassCameras;
        context.CameraToClipSpace = keepCameraToClipSpace;
        context.WorldToCamera = keepWorldToCamera;
        deviceContext.PopState();
    }

    private void RenderReceivingPass(EvaluationContext context, Matrix4x4 worldToLightClipSpace, float texelSizePerW)
    {
        var parameters = new ShadowSettings.ShadowParameters
                             {
                                 WorldToLightClipSpace = Matrix4x4.Transpose(worldToLightClipSpace),
                                 Color = ShadowColor.GetValue(context),
                                 Bias = ShadowBias.GetValue(context),
                                 NormalOffset = NormalOffset.GetValue(context),
                                 TexelSizePerW = texelSizePerW,
                                 SpotCosInner = _spotCosInner,
                                 SpotCosOuter = _spotCosOuter,
                                 TanHalfFieldOfView = _tanHalfFieldOfView,
                             };

        if (_parameterBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _parameterBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _parameterBuffer);
        }

        var keepShadowMap = context.ShadowMap;
        var keepParameters = context.ShadowParameters;
        context.ShadowMap = _shadowMapSrv;
        context.ShadowParameters = _parameterBuffer;

        // Back-compat for [DrawMeshWithShadow], which reads the shadow map and the light matrix by name.
        var matrix = parameters.WorldToLightClipSpace;
        _legacyLightMatrixRows[0] = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
        _legacyLightMatrixRows[1] = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
        _legacyLightMatrixRows[2] = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
        _legacyLightMatrixRows[3] = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
        var hadLegacyMap = context.ContextTextures.TryGetValue(LegacyShadowMapName, out var keepLegacyMap);
        var hadLegacyMatrix = context.ObjectVariables.TryGetValue(LegacyLightMatrixName, out var keepLegacyMatrix);
        context.ContextTextures[LegacyShadowMapName] = _shadowMapTexture!;
        context.ObjectVariables[LegacyLightMatrixName] = _legacyLightMatrixRows;

        EvaluateSubTree(context);

        if (hadLegacyMap)
        {
            context.ContextTextures[LegacyShadowMapName] = keepLegacyMap!;
        }
        else
        {
            context.ContextTextures.Remove(LegacyShadowMapName);
        }

        if (hadLegacyMatrix)
        {
            context.ObjectVariables[LegacyLightMatrixName] = keepLegacyMatrix!;
        }
        else
        {
            context.ObjectVariables.Remove(LegacyLightMatrixName);
        }

        context.ShadowMap = keepShadowMap;
        context.ShadowParameters = keepParameters;
    }

    private void EvaluateSubTree(EvaluationContext context)
    {
        // Without invalidation the second pass would get the subtree's cached result of the first.
        DirtyFlag.GlobalInvalidationTick++;
        Command.InvalidateGraph();
        Command.GetValue(context);
    }

    private bool TryEnsureShadowMap(int resolution)
    {
        if (_shadowMapTexture != null && _shadowMapResolution == resolution)
            return true;

        if (_failedResolution == resolution)
            return false;

        DisposeShadowMap();
        try
        {
            var device = ResourceManager.Device;
            _shadowMapTexture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                              {
                                                                  ArraySize = 1,
                                                                  BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
                                                                  CpuAccessFlags = CpuAccessFlags.None,
                                                                  Format = Format.R32_Typeless,
                                                                  Width = resolution,
                                                                  Height = resolution,
                                                                  MipLevels = 1,
                                                                  OptionFlags = ResourceOptionFlags.None,
                                                                  SampleDescription = new SampleDescription(1, 0),
                                                                  Usage = ResourceUsage.Default
                                                              });
            _shadowMapDsv = new DepthStencilView(device, _shadowMapTexture,
                                                 new DepthStencilViewDescription
                                                     {
                                                         Format = Format.D32_Float,
                                                         Dimension = DepthStencilViewDimension.Texture2D
                                                     });
            _shadowMapSrv = new ShaderResourceView(device, _shadowMapTexture,
                                                   new ShaderResourceViewDescription
                                                       {
                                                           Format = Format.R32_Float,
                                                           Dimension = ShaderResourceViewDimension.Texture2D,
                                                           Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                                           {
                                                                               MipLevels = 1,
                                                                               MostDetailedMip = 0
                                                                           }
                                                       });
            _shadowMapResolution = resolution;
            _failedResolution = 0;
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create a {resolution}x{resolution} shadow map: {e.Message}", this);
            DisposeShadowMap();
            _failedResolution = resolution;
            return false;
        }
    }

    private void DisposeShadowMap()
    {
        GraphicsUtilities.Dispose(ref _shadowMapSrv);
        GraphicsUtilities.Dispose(ref _shadowMapDsv);
        GraphicsUtilities.Dispose(ref _shadowMapTexture);
        _shadowMapResolution = 0;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        DisposeShadowMap();
        GraphicsUtilities.Dispose(ref _parameterBuffer);
    }
    #endregion

    #region light selection
    /** A light picked by position ("1st Light") or by the id of the light operator in the graph. */
    private bool TryFindLight(EvaluationContext context, out Light light)
    {
        var lights = context.Lights;
        var selection = SourceLight.GetValue(context);

        if (TryGetOrdinalIndex(selection, out var ordinalIndex))
        {
            if (ordinalIndex < lights.PushedCount)
            {
                light = lights.GetPushedLight(ordinalIndex, out var ignoredOwner);
                return true;
            }
        }
        else if (Guid.TryParse(selection, out var lightOpId))
        {
            for (var index = 0; index < lights.PushedCount; index++)
            {
                var candidate = lights.GetPushedLight(index, out var owner);
                for (var instance = owner; instance != null; instance = instance.Parent)
                {
                    if (instance.SymbolChildId != lightOpId)
                        continue;

                    light = candidate;
                    return true;
                }
            }
        }

        light = default;
        return false;
    }

    /** Remembers the light operators above for the dropdown, as the ops the user sees in the graph. */
    private void CollectLightOptions(EvaluationContext context)
    {
        var lights = context.Lights;
        _collectedLightCount = Math.Min(lights.PushedCount, LightStack.MaxLights);
        for (var index = 0; index < _collectedLightCount; index++)
        {
            lights.GetPushedLight(index, out var owner);
            var opInGraph = FindOpVisibleFromHere(owner);
            _collectedLightIds[index] = opInGraph?.SymbolChildId ?? Guid.Empty;
            _collectedLightNames[index] = opInGraph?.SymbolChild.ReadableName;
        }
    }

    /**
     * Light ops wrapped in other ops (e.g. [SetSpotLight] inside [SpotLight]) report the inner op. This walks up to the
     * op that sits in this op's composition or in one of its parents.
     */
    private Instance? FindOpVisibleFromHere(Instance? owner)
    {
        for (var candidate = owner; candidate != null; candidate = candidate.Parent)
        {
            for (var composition = Parent; composition != null; composition = composition.Parent)
            {
                if (candidate.Parent == composition)
                    return candidate;
            }
        }

        return null;
    }

    private static bool TryGetOrdinalIndex(string? selection, out int index)
    {
        if (string.IsNullOrEmpty(selection))
        {
            index = 0;
            return true;
        }

        for (index = 0; index < OrdinalLabels.Length; index++)
        {
            if (selection == OrdinalLabels[index])
                return true;
        }

        return false;
    }

    private string GetLightLabel(int index)
    {
        var name = _collectedLightNames[index] ?? "Light";
        var sameNameCount = 0;
        for (var other = 0; other < index; other++)
        {
            if (_collectedLightNames[other] == name)
                sameNameCount++;
        }

        return sameNameCount == 0 ? name : $"{name} ({sameNameCount + 1})";
    }

    string? ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId != SourceLight.Input.Id)
            return null;

        var value = SourceLight.TypedInputValue.Value;
        if (string.IsNullOrEmpty(value))
            return OrdinalLabels[0];

        if (!Guid.TryParse(value, out var lightOpId))
            return value;

        for (var index = 0; index < _collectedLightCount; index++)
        {
            if (_collectedLightIds[index] == lightOpId)
                return GetLightLabel(index);
        }

        return MissingLightLabel;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != SourceLight.Input.Id)
            yield break;

        var ordinalCount = Math.Clamp(_collectedLightCount, 1, OrdinalLabels.Length);
        for (var index = 0; index < ordinalCount; index++)
        {
            yield return OrdinalLabels[index];
        }

        for (var index = 0; index < _collectedLightCount; index++)
        {
            if (_collectedLightIds[index] != Guid.Empty)
                yield return GetLightLabel(index);
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId != SourceLight.Input.Id || selected == null)
            return;

        if (TryGetOrdinalIndex(selected, out var ordinalIndex))
        {
            SourceLight.SetTypedInputValue(ordinalIndex == 0 ? string.Empty : selected);
            return;
        }

        for (var index = 0; index < _collectedLightCount; index++)
        {
            if (_collectedLightIds[index] == Guid.Empty || GetLightLabel(index) != selected)
                continue;

            SourceLight.SetTypedInputValue(_collectedLightIds[index].ToString());
            return;
        }

        SourceLight.SetTypedInputValue(selected);
    }
    #endregion

    private void SetStatus(IStatusProvider.StatusLevel level, string message)
    {
        _statusLevel = level;
        _statusMessage = message;
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusLevel;
    string? IStatusProvider.GetStatusMessage() => _statusMessage;

    private enum Modes
    {
        FromLight,
        Manual,
    }

    private static readonly string[] OrdinalLabels =
        ["1st Light", "2nd Light", "3rd Light", "4th Light", "5th Light", "6th Light", "7th Light", "8th Light"];

    private const string MissingLightLabel = "Missing light";
    private const string NoLightMessage = "No matching light above. The shadow is cast from LightPosition towards LightTarget.";
    private const string PointLightMessage = "Point lights cast their shadow towards LightTarget, within FieldOfView.";
    private const string SamePositionMessage = "The light position and target are the same, so no shadow is drawn.";

    /** Context names [DrawMeshWithShadow] reads. */
    private const string LegacyShadowMapName = "ShadowMap";
    private const string LegacyLightMatrixName = "_WorldToLightClipSpace";

    private const int MinResolution = 16;
    private const int MaxResolution = 8192;
    private const float MinFieldOfViewDegrees = 1;
    private const float MaxFieldOfViewDegrees = 170;
    private const float SpotFieldOfViewMargin = 1.1f;

    /** A cosine below any real angle: the shadow is not faded by a cone. */
    private const float NoSpotCone = -2;

    private readonly Guid[] _collectedLightIds = new Guid[LightStack.MaxLights];
    private readonly string?[] _collectedLightNames = new string?[LightStack.MaxLights];
    private int _collectedLightCount;

    private float _spotCosInner;
    private float _spotCosOuter;
    private float _tanHalfFieldOfView;

    private readonly Vector4[] _legacyLightMatrixRows = new Vector4[4];

    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Success;
    private string? _statusMessage;

    private Texture2D? _shadowMapTexture;
    private DepthStencilView? _shadowMapDsv;
    private ShaderResourceView? _shadowMapSrv;
    private Buffer? _parameterBuffer;
    private int _shadowMapResolution;
    private int _failedResolution;

    [Input(Guid = "be6dc055-d4c8-4c75-a084-12c22a268034")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "75814c4f-ca49-4103-9096-32c9f8326830", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "11ff37c1-46f4-4b5c-a12a-22c25c73fa38")]
    public readonly InputSlot<string> SourceLight = new();

    [Input(Guid = "298be67d-77eb-49b3-a65a-dc3e2d5bef7c")]
    public readonly InputSlot<Vector4> ShadowColor = new();

    [Input(Guid = "244b972a-2d18-4c2d-bcba-a2b480ecbada")]
    public readonly InputSlot<float> ShadowBias = new();

    [Input(Guid = "df4d2b87-2ec0-457f-ba69-8ae0ae684a02")]
    public readonly InputSlot<float> NormalOffset = new();

    [Input(Guid = "3ee1e726-b5b8-4479-92cc-1a46886ddfd2")]
    public readonly InputSlot<int> Resolution = new();

    [Input(Guid = "5fb309a1-f164-4fe0-95e2-3bfc37823f78")]
    public readonly InputSlot<Vector2> DepthRange = new();

    [Input(Guid = "6196074f-4770-4e81-812a-012fcaab207b")]
    public readonly InputSlot<Vector3> LightPosition = new();

    [Input(Guid = "76a6a0e4-168d-4172-9946-e843df9d0ca0")]
    public readonly InputSlot<Vector3> LightTarget = new();

    [Input(Guid = "d903772c-8dff-4147-868c-a461edaf3f9f")]
    public readonly InputSlot<float> FieldOfView = new();

    [Input(Guid = "76cd5b13-b23c-4cbc-bdab-c7d1d6222f48")]
    public readonly InputSlot<float> AreaSize = new();
}
