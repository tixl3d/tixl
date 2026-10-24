#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.App.Gestures;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Graphics;
using T3.Graphics.Compat;
using Buffer = T3.Graphics.Compat.Buffer;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Shows a cube map in the output window as a single face, an unfolded cross, a ball seen from outside or the
/// view from inside. Every mode renders one preview pixel per screen pixel, so zoomed texels stay sharp.
/// </summary>
internal sealed class CubeMapPreview
{
    private enum Modes
    {
        Side,
        Cross,
        Sphere,
        Environment,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PreviewParams
    {
        public Matrix4x4 CameraToWorld;

        public Vector2 ContentOrigin;
        public Vector2 TexelsPerPixel;

        public Vector2 PreviewSize;
        public float TanHalfFov;
        public float CameraDistance;

        public int Mode;
        public int Face;
        public int Mip;
        public int MipSize;

        public int EquiAngular;
        private Vector3 _padding;
    }

    public void Draw(Texture2D cubeMap)
    {
        var canvas = ImageOutputCanvas.Current;
        if (canvas == null)
            return;

        var controlsPos = ImGui.GetCursorScreenPos();
        var description = cubeMap.Description;
        _mip = _mip.Clamp(0, description.MipLevels - 1);
        var mipSize = Math.Max(1, description.Width >> _mip);
        var is3d = _mode is Modes.Sphere or Modes.Environment;

        // Whole pixels, so the unfiltered texels land exactly on the screen grid.
        var previewPos = new Vector2(MathF.Floor(canvas.WindowPos.X), MathF.Floor(canvas.WindowPos.Y));
        var previewSize = new Vector2(MathF.Max(1, MathF.Floor(canvas.WindowSize.X)), MathF.Max(1, MathF.Floor(canvas.WindowSize.Y)));
        var drawList = ImGui.GetWindowDrawList();
        CustomComponents.FillWithStripes(drawList, new ImRect(previewPos, previewPos + previewSize), canvas.Scale.X);

        var contentOrigin = Vector2.Zero;
        var texelsPerPixel = Vector2.One;
        if (!is3d)
        {
            var contentSize = _mode == Modes.Side ? new Vector2(mipSize) : new Vector2(mipSize * 4, mipSize * 3);
            canvas.FitAreaIfFitted(new ImRect(Vector2.Zero, contentSize));
            contentOrigin = canvas.TransformPosition(Vector2.Zero) - previewPos;
            texelsPerPixel = Vector2.One / canvas.TransformDirection(Vector2.One);
        }

        var parameters = new PreviewParams
                             {
                                 CameraToWorld = Matrix4x4.Transpose(Matrix4x4.CreateFromYawPitchRoll(_yawDegrees.ToRadians(), _pitchDegrees.ToRadians(), 0)),
                                 ContentOrigin = contentOrigin,
                                 TexelsPerPixel = texelsPerPixel,
                                 PreviewSize = previewSize,
                                 TanHalfFov = MathF.Tan((_mode == Modes.Sphere ? SphereFovDegrees : _environmentFovDegrees).ToRadians() / 2),
                                 CameraDistance = _sphereDistance,
                                 Mode = (int)_mode,
                                 Face = _face,
                                 Mip = _mip,
                                 MipSize = mipSize,
                                 EquiAngular = cubeMap.CubeMapLayout == Texture2D.CubeMapLayouts.EquiAngular ? 1 : 0,
                             };

        if (TryRender(cubeMap, (int)previewSize.X, (int)previewSize.Y, parameters))
        {
            ImGui.SetCursorScreenPos(previewPos);
            ImGui.Image((IntPtr)SrvManager.GetSrvForTexture(_preview!).ImGuiTextureId, previewSize);
        }

        if (UserSettings.Config.ShowToolbar)
        {
            var layout = parameters.EquiAngular != 0 ? "  EAC" : "";
            var zoom = is3d ? "" : $" ×{canvas.Scale.X:G2}";
            canvas.DrawCaption($"{description.Width}x{description.Height}×6  {ImageOutputCanvas.GetFormatLabel(description.Format)}{layout}  mip {_mip}{zoom}");
        }

        ImGui.SetCursorScreenPos(controlsPos);
        DrawControls(description.MipLevels, description.Width);
        var controlsMax = ImGui.GetItemRectMax();

        if (is3d)
        {
            canvas.BlockInteractionThisFrame();
            HandleOrbitAndZoom(isOverControls: ImGui.IsMouseHoveringRect(controlsPos, controlsMax));
        }
    }

    private void DrawControls(int mipLevels, int faceSize)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8 * scale);
        CustomComponents.SegmentedButton(ref _mode);

        if (_mode == Modes.Side)
        {
            ImGui.SameLine(0, 8 * scale);
            ImGui.SetNextItemWidth(60 * scale);
            if (ImGui.BeginCombo("##CubeMapFace", _faceLabels[_face], ImGuiComboFlags.HeightLargest))
            {
                for (var faceIndex = 0; faceIndex < _faceLabels.Length; faceIndex++)
                {
                    if (CustomComponents.DrawMenuItem(faceIndex, _faceLabels[faceIndex], isChecked: faceIndex == _face))
                        _face = faceIndex;
                }

                ImGui.EndCombo();
            }
        }

        ImGui.SameLine(0, 8 * scale);
        if (mipLevels <= 1)
        {
            ImGui.AlignTextToFramePadding();
            CustomComponents.StylizedText("No mipmaps", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        ImGui.SetNextItemWidth(110 * scale);
        if (ImGui.BeginCombo("##CubeMapMip", GetMipLabel(_mip, faceSize), ImGuiComboFlags.HeightLargest))
        {
            for (var mip = 0; mip < mipLevels; mip++)
            {
                if (CustomComponents.DrawMenuItem(mip, GetMipLabel(mip, faceSize), isChecked: mip == _mip))
                    _mip = mip;
            }

            ImGui.EndCombo();
        }
    }

    private static string GetMipLabel(int mip, int faceSize) => $"Mip {mip}  ({Math.Max(1, faceSize >> mip)})";

    /** Left drag or a trackpad scroll orbits; the wheel or a pinch zooms. */
    private void HandleOrbitAndZoom(bool isOverControls)
    {
        // The preview image is an item itself, so only the controls row is excluded.
        var isHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !isOverControls;
        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            _isOrbiting = true;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _isOrbiting = false;

        var orbitDelta = Vector2.Zero;
        if (_isOrbiting)
        {
            orbitDelta = ImGui.GetIO().MouseDelta;
        }
        else if (isHovered && PointerGestures.PansWithScroll)
        {
            orbitDelta = new Vector2(-PointerGestures.PanDelta.X, PointerGestures.PanDelta.Y);
        }

        _yawDegrees -= orbitDelta.X * OrbitDegreesPerPixel;
        _pitchDegrees = (_pitchDegrees - orbitDelta.Y * OrbitDegreesPerPixel).Clamp(-89, 89);

        if (!isHovered)
            return;

        var zoomFactor = 1 / PointerGestures.ZoomFactor;
        if (!PointerGestures.PansWithScroll)
            zoomFactor *= MathF.Pow(ZoomPerWheelNotch, -ImGui.GetIO().MouseWheel);

        if (_mode == Modes.Sphere)
        {
            _sphereDistance = (_sphereDistance * zoomFactor).Clamp(1.05f, 20);
        }
        else
        {
            _environmentFovDegrees = (_environmentFovDegrees * zoomFactor).Clamp(10, 150);
        }
    }

    private bool TryRender(Texture2D cubeMap, int width, int height, in PreviewParams parameters)
    {
        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(Path.Combine(SharedResources.EditorResourcesDirectory, ShaderPath),
                                                                                 null, () => "main");
        var shader = _shaderResource.Value;
        if (shader == null || !TryUpdateViews(cubeMap, width, height))
            return false;

        if (_paramBuffer == null)
        {
            ResourceManager.SetupConstBuffer(parameters, ref _paramBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(parameters, _paramBuffer);
        }

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramBuffer);
        csStage.SetShaderResource(0, SrvManager.GetSrvForTexture(cubeMap));
        csStage.SetShaderResource(1, _facesSrv);
        csStage.SetSampler(0, ResourceManager.DefaultSamplerState);
        csStage.SetUnorderedAccessView(0, _previewUav, 0);
        deviceContext.Dispatch((width + ThreadGroupSize - 1) / ThreadGroupSize, (height + ThreadGroupSize - 1) / ThreadGroupSize, 1);

        deviceContext.PopState();
        return true;
    }

    private bool TryUpdateViews(Texture2D cubeMap, int width, int height)
    {
        try
        {
            if (cubeMap != _facesSource || _facesSrv == null)
            {
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _facesSrv);
                var description = cubeMap.Description;
                _facesSrv = new ShaderResourceView(ResourceManager.Device, cubeMap,
                                                   new ShaderResourceViewDescription
                                                       {
                                                           Format = description.Format,
                                                           Dimension = ShaderResourceViewDimension.Texture2DArray,
                                                           Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                                                                                {
                                                                                    MostDetailedMip = 0,
                                                                                    MipLevels = description.MipLevels,
                                                                                    FirstArraySlice = 0,
                                                                                    ArraySize = 6,
                                                                                },
                                                       });
                _facesSource = cubeMap;
            }

            if (_preview is { IsDisposed: false } && _preview.Description.Width == width && _preview.Description.Height == height)
                return true;

            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _previewUav);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _preview);
            _preview = Texture2D.CreateTexture2D(new Texture2DDescription
                                                     {
                                                         ArraySize = 1,
                                                         BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = Format.R16G16B16A16_Float,
                                                         Width = width,
                                                         Height = height,
                                                         MipLevels = 1,
                                                         OptionFlags = ResourceOptionFlags.None,
                                                         SampleDescription = new SampleDescription(1, 0),
                                                         Usage = ResourceUsage.Default,
                                                     });
            _previewUav = new UnorderedAccessView(ResourceManager.Device, _preview);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning("Failed to create cube map preview: " + e.Message);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _facesSrv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _previewUav);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _preview);
            return false;
        }
    }

    private const string ShaderPath = "shaders/render-cubemap-preview-cs.hlsl";
    private const int ThreadGroupSize = 8;
    private const float OrbitDegreesPerPixel = 0.3f;
    private const float ZoomPerWheelNotch = 1.1f;
    private const float SphereFovDegrees = 40;

    private static readonly string[] _faceLabels = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];

    private Modes _mode;
    private int _face;
    private int _mip;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _sphereDistance = 3;
    private float _environmentFovDegrees = 90;
    private bool _isOrbiting;

    private Resource<ComputeShader>? _shaderResource;
    private Buffer? _paramBuffer;
    private Texture2D? _preview;
    private UnorderedAccessView? _previewUav;
    private Texture2D? _facesSource;
    private ShaderResourceView? _facesSrv;
}
