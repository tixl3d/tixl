#nullable enable
using System.Diagnostics;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Editor.Gui.Windows;
using Device = SharpDX.Direct3D11.Device;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Utilities = T3.Core.Utils.Utilities;

namespace T3.Editor.Gui.OutputUi;

internal sealed class CommandOutputUi : OutputUi<Command>
{
    public bool GizmosEnabled { get; set; } = false;

    internal CommandOutputUi()
    {
        _onGridInstanceDisposed = OnGridInstanceDisposed;
            
        // ensure op exists for drawing grid
        var outputWindowGridSymbolId = Guid.Parse("e5588101-5686-4b02-ab7d-e58199ba552e");
            
        if(!SymbolRegistry.TryGetSymbol(outputWindowGridSymbolId, out var outputWindowGridSymbol))
        {
            Log.Error("CommandOutputUi: Could not find grid Gizmo symbol");
            return;
        }
            
        _outputWindowGridSymbol = outputWindowGridSymbol;
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        if (!EnsureGridOutputsExist())
        {
            return;
        }

        var originalCamMatrix = context.WorldToCamera;
        var originalViewMatrix = context.CameraToClipSpace;

        // Invalidate
        StartInvalidation(slot);

        // Setup render target - TODO: this should not be done for all 'Command' outputs as most of them don't produce image content
        var device = ResourceManager.Device;

        var size = context.RequestedResolution;
        UpdateTextures(device, size, Format.R16G16B16A16_Float);
        var deviceContext = device.ImmediateContext;
        var prevViewports = deviceContext.Rasterizer.GetViewports<RawViewportF>();

        RenderTargetView?[]? prevTargetViews = deviceContext.OutputMerger.GetRenderTargets(2);
        deviceContext.Rasterizer.SetViewport(new SharpDX.Viewport(0, 0, size.Width, size.Height, 0.0f, 1.0f));
        deviceContext.OutputMerger.SetTargets(_msaaDepthBufferDsv, _msaaColorBufferRtv);

        var colorRgba = new RawColor4(context.BackgroundColor.X,
                                      context.BackgroundColor.Y,
                                      context.BackgroundColor.Z,
                                      context.BackgroundColor.W);
        deviceContext.ClearRenderTargetView(_msaaColorBufferRtv, colorRgba);
        if (_msaaDepthBufferDsv != null)
            deviceContext.ClearDepthStencilView(_msaaDepthBufferDsv, DepthStencilClearFlags.Depth, 1.0f, 0);

        // Evaluate the operator
        slot.Update(context);

        if (context.ShowGizmos != T3.Core.Operator.GizmoVisibility.Off)
        {
            context.WorldToCamera = originalCamMatrix;
            context.CameraToClipSpace = originalViewMatrix;

            if(_gridOutputs != null && _gridOutputs.Count > 0)
            {
                var outputSlot = _gridOutputs[0];
                outputSlot.InvalidateGraph();
                outputSlot.Update(context);
            }
        }

        if (MsaaSampleCount > 1 && _msaaColorBuffer != null && _resolvedColorBuffer != null)
        {
            try
            {
                deviceContext.ResolveSubresource(_msaaColorBuffer, 0, _resolvedColorBuffer, 0,
                                                 _resolvedColorBuffer.Description.Format);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to resolve MSAA buffer: " + e.Message);
            }
        }

        // Restore previous setup
        deviceContext.Rasterizer.SetViewports(prevViewports);
        deviceContext.OutputMerger.SetTargets(prevTargetViews);

        if (prevTargetViews == null)
        {
            Log.Warning("Can't dispose obsolete RenderTargetView after draw. This indicates corrupted a render context.");
        }
        else
        {
            // Clean up ref counts for RTVs
            foreach (var t in prevTargetViews)
            {
                if (t == null || t.IsDisposed)
                    continue;

                t.Dispose();
            }
        }
    }

    private bool EnsureGridOutputsExist()
    {
        if (_gridOutputs != null) 
            return true;
            
        if (_outputWindowGridSymbol == null || !_outputWindowGridSymbol.TryGetParentlessInstance(out var gridInstance))
        {
            Log.Error($"{nameof(CommandOutputUi)} Could not create grid instance");
            return false;
        }

        gridInstance.Disposing += _onGridInstanceDisposed;
        _gridInstance = gridInstance;
        _gridOutputs = gridInstance.Outputs;
        return true;
    }
        
    private void OnGridInstanceDisposed(IResourceConsumer gridInstanceAsResourceConsumer)
    {
        _gridOutputs = null;
        _gridInstance!.Disposing -= _onGridInstanceDisposed;
        _gridInstance = null;
    }

    public override IOutputUi Clone()
    {
        return new CommandOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        var canvas = ImageOutputCanvas.Current;
        if (canvas == null)
            return;

        Debug.Assert(slot is Slot<Command>);

        var outputTexture = _resolvedColorBuffer ?? _msaaColorBuffer;
        if (outputTexture != null)
        {
            canvas.DrawTexture(outputTexture);
        }
    }

    private void UpdateTextures(Device device, Int2 size, Format format)
    {
        try
        {
            if (_msaaColorBuffer != null)
            {
                var currentDesc = _msaaColorBuffer.Description;
                if (currentDesc.Width == size.Width
                    && currentDesc.Height == size.Height
                    && currentDesc.Format == format
                    && currentDesc.SampleDescription.Count == MsaaSampleCount)
                    return;

                Utilities.Dispose(ref _msaaColorBufferRtv);
                Utilities.Dispose(ref _msaaColorBuffer);
            }

            Utilities.Dispose(ref _resolvedColorBufferSrv);
            Utilities.Dispose(ref _resolvedColorBufferRtv);
            Utilities.Dispose(ref _resolvedColorBuffer);

            var msaaDesc = _defaultColorDescription with
                               {
                                   Format = format,
                                   Width = size.Width,
                                   Height = size.Height,
                                   SampleDescription = new SampleDescription(MsaaSampleCount, 0),
                               };

            _msaaColorBuffer = Texture2D.CreateTexture2D(msaaDesc);
            _msaaColorBufferRtv = new RenderTargetView(device, _msaaColorBuffer,
                                                       new RenderTargetViewDescription
                                                           {
                                                               Format = format,
                                                               Dimension = RenderTargetViewDimension.Texture2DMultisampled
                                                           });

            var resolvedDesc = _defaultColorDescription with
                                   {
                                       Format = format,
                                       Width = size.Width,
                                       Height = size.Height,
                                       SampleDescription = new SampleDescription(1, 0),
                                   };

            _resolvedColorBuffer = Texture2D.CreateTexture2D(resolvedDesc);
            _resolvedColorBufferSrv = new ShaderResourceView(device, _resolvedColorBuffer);
            _resolvedColorBufferRtv = new RenderTargetView(device, _resolvedColorBuffer);

            Utilities.Dispose(ref _msaaDepthBufferDsv);
            Utilities.Dispose(ref _msaaDepthBuffer);

            var depthDesc = _defaultDepthDescription with
                                {
                                    Width = size.Width,
                                    Height = size.Height,
                                    SampleDescription = new SampleDescription(MsaaSampleCount, 0),
                                };

            _msaaDepthBuffer = Texture2D.CreateTexture2D(depthDesc);
            var depthViewDesc = new DepthStencilViewDescription
                                    {
                                        Format = Format.D32_Float,
                                        Dimension = DepthStencilViewDimension.Texture2DMultisampled
                                    };

            _msaaDepthBufferDsv = new DepthStencilView(device, _msaaDepthBuffer, depthViewDesc);
        }
        catch (Exception e)
        {
            Log.Warning("Failed to generate texture: " + e.Message);
        }
    }

    private static readonly Texture2DDescription _defaultColorDescription = new()
                                                                               {
                                                                                   ArraySize = 1,
                                                                                   BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                                                   CpuAccessFlags = CpuAccessFlags.None,
                                                                                   MipLevels = 1,
                                                                                   OptionFlags = ResourceOptionFlags.None,
                                                                                   SampleDescription = new SampleDescription(1, 0),
                                                                                   Usage = ResourceUsage.Default
                                                                               };

    private static readonly Texture2DDescription _defaultDepthDescription = new()
                                                                               {
                                                                                   ArraySize = 1,
                                                                                   BindFlags = BindFlags.DepthStencil,
                                                                                   CpuAccessFlags = CpuAccessFlags.None,
                                                                                   Format = Format.R32_Typeless,
                                                                                   Width = 1,
                                                                                   Height = 1,
                                                                                   MipLevels = 1,
                                                                                   OptionFlags = ResourceOptionFlags.None,
                                                                                   SampleDescription = new SampleDescription(1, 0),
                                                                                   Usage = ResourceUsage.Default
                                                                               };

    private const int MsaaSampleCount = 4;
    private Texture2D? _msaaColorBuffer;
    private RenderTargetView? _msaaColorBufferRtv;
    private Texture2D? _resolvedColorBuffer;
    private ShaderResourceView? _resolvedColorBufferSrv;
    private RenderTargetView? _resolvedColorBufferRtv;
    private Texture2D? _msaaDepthBuffer;
    private DepthStencilView? _msaaDepthBufferDsv;

    // instance management
    private readonly Symbol? _outputWindowGridSymbol;
    private Instance? _gridInstance;
    private readonly Action<IResourceConsumer> _onGridInstanceDisposed;
    private IReadOnlyList<ISlot>? _gridOutputs;
}