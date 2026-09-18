#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.SystemUi;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;
using Rectangle = System.Drawing.Rectangle;
using Color = SharpDX.Color;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Player;

/// <summary>
/// Presenting a setup onto the displays it is bound to. The main window takes the first binding, so an
/// installation with one projector opens exactly one window; every further binding gets a borderless window of
/// its own. Bindings cannot change while a player runs, so the windows are opened once and never reshuffled.
/// </summary>
internal static partial class Program
{
    /// <summary>The output the main window shows, or Guid.Empty when no binding claims it.</summary>
    private static Guid _mainWindowOutputId;

    /// <summary>
    /// Opens a window per display binding beyond the first. Called once the device and the setup are both up.
    /// Bindings naming a display this machine doesn't have are reported and skipped — an installation should
    /// say so in its log rather than come up dark and silent.
    /// </summary>
    private static void InitializeOutputWindows(IReadOnlyList<DisplayInfo> displays)
    {
        var setup = ActiveSetup.Current;
        var machine = ActiveSetup.Machine;
        if (setup == null || machine == null)
            return;

        foreach (var binding in machine.Bindings)
        {
            if (binding.IsStream)
                continue;

            var output = setup.FindOutput(binding.OutputId);
            if (output == null || !output.IsSending)
                continue;

            if (binding.DisplayIndex < 0 || binding.DisplayIndex >= displays.Count)
            {
                Log.Warning($"Output \"{output.Name}\" is bound to display {binding.DisplayIndex + 1}, "
                            + $"but this machine has {displays.Count}. It will not be shown.");
                continue;
            }

            var bounds = displays[binding.DisplayIndex].Bounds;
            if (_mainWindowOutputId == Guid.Empty)
            {
                // The main window already exists and runs the render loop, so it carries the first binding.
                _mainWindowOutputId = output.Id;
                _renderForm.FormBorderStyle = FormBorderStyle.None;
                _renderForm.Bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                Log.Info($"Output \"{output.Name}\" presents on display {binding.DisplayIndex + 1}.");
                continue;
            }

            var extra = TryOpenOutputWindow(output.Id, output.Name, bounds);
            if (extra != null)
            {
                _outputWindows.Add(extra);
                Log.Info($"Output \"{output.Name}\" presents on display {binding.DisplayIndex + 1}.");
            }
        }
    }

    private static OutputWindow? TryOpenOutputWindow(Guid outputId, string outputName, Rectangle bounds)
    {
        try
        {
            var form = new RenderForm(outputName)
                           {
                               FormBorderStyle = FormBorderStyle.None,
                               StartPosition = FormStartPosition.Manual,
                               Bounds = bounds,
                               AllowUserResizing = false,
                           };
            form.Show();

            var description = new SwapChainDescription
                                  {
                                      BufferCount = 3,
                                      ModeDescription = new ModeDescription(form.ClientSize.Width, form.ClientSize.Height,
                                                                            new Rational(60, 1), Format.R8G8B8A8_UNorm),
                                      IsWindowed = true,
                                      OutputHandle = form.Handle,
                                      SampleDescription = new SampleDescription(1, 0),
                                      SwapEffect = SwapEffect.FlipDiscard,
                                      Flags = SwapChainFlags.AllowModeSwitch,
                                      Usage = Usage.RenderTargetOutput,
                                  };

            using var factory = _swapChain.GetParent<Factory>();
            var swapChain = new SwapChain(factory, _device, description);
            factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll);

            var backBuffer = Resource.FromSwapChain<SharpDX.Direct3D11.Texture2D>(swapChain, 0);
            return new OutputWindow(outputId, form, swapChain, backBuffer, new RenderTargetView(_device, backBuffer));
        }
        catch (Exception e)
        {
            Log.Warning($"Could not open a window for output \"{outputName}\": {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Composites and shows every output beyond the one in the main window. Runs before the main window claims
    /// the back buffer, because the compositor binds render targets of its own and leaves them bound.
    /// </summary>
    private static void DrawOutputWindows()
    {
        for (var i = 0; i < _outputWindows.Count; i++)
        {
            var window = _outputWindows[i];
            var composite = OutputCompositor.RenderOutput(window.OutputId);
            if (composite == null)
                continue;

            window.EnsureTextureView(_device, composite);
            if (window.TextureView == null)
                continue;

            _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, window.Form.ClientSize.Width, window.Form.ClientSize.Height, 0f, 1f));
            _deviceContext.OutputMerger.SetTargets(window.RenderTargetView);
            _deviceContext.ClearRenderTargetView(window.RenderTargetView, new Color(0, 0, 0, 1));

            _deviceContext.Rasterizer.State = _rasterizerState;
            if (_fullScreenVertexShaderResource?.Value != null)
                _deviceContext.VertexShader.Set(_fullScreenVertexShaderResource.Value);

            if (_fullScreenPixelShaderResource?.Value != null)
                _deviceContext.PixelShader.Set(_fullScreenPixelShaderResource.Value);

            _deviceContext.PixelShader.SetShaderResource(0, window.TextureView);
            _deviceContext.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            _deviceContext.Draw(3, 0);
            _deviceContext.PixelShader.SetShaderResource(0, null);
        }
    }

    /// <summary>
    /// Sends every stream-bound output to its sender. Like the extra windows it runs before the main window claims
    /// the back buffer, since compositing binds render targets of its own.
    /// </summary>
    private static void SendStreams()
    {
        var setup = ActiveSetup.Current;
        var machine = ActiveSetup.Machine;
        if (setup == null || machine == null)
            return;

        OutputStreaming.BeginFrame();
        foreach (var binding in machine.Bindings)
        {
            if (!binding.IsStream)
                continue;

            var output = setup.FindOutput(binding.OutputId);
            if (output == null || !output.IsSending)
                continue;

            OutputStreaming.Send(machine, output, binding);
        }

        OutputStreaming.EndFrame();
    }

    /// <summary>Shows what was drawn. Called right after the main window presents, so all displays flip together.</summary>
    private static void PresentOutputWindows()
    {
        for (var i = 0; i < _outputWindows.Count; i++)
        {
            _outputWindows[i].SwapChain.Present(_vsyncInterval, PresentFlags.None);
        }
    }

    private static void DisposeOutputWindows()
    {
        for (var i = 0; i < _outputWindows.Count; i++)
        {
            _outputWindows[i].Dispose();
        }

        _outputWindows.Clear();
    }

    private sealed class OutputWindow(Guid outputId, RenderForm form, SwapChain swapChain,
                                      SharpDX.Direct3D11.Texture2D backBuffer, RenderTargetView renderTargetView)
    {
        public Guid OutputId { get; } = outputId;
        public RenderForm Form { get; } = form;
        public SwapChain SwapChain { get; } = swapChain;
        public RenderTargetView RenderTargetView { get; } = renderTargetView;
        public ShaderResourceView? TextureView { get; private set; }

        /// <summary>Rebuilds the view only when a different composite arrives; the texture is reused across frames.</summary>
        public void EnsureTextureView(Device device, Texture2D composite)
        {
            if (composite.IsDisposed)
                return;

            var nativePointer = ((SharpDX.Direct3D11.Texture2D)composite).NativePointer;
            if (TextureView != null && !TextureView.IsDisposed && _viewedTexture == nativePointer)
                return;

            TextureView?.Dispose();
            TextureView = new ShaderResourceView(device, composite);
            _viewedTexture = nativePointer;
        }

        public void Dispose()
        {
            TextureView?.Dispose();
            RenderTargetView.Dispose();
            backBuffer.Dispose();
            SwapChain.Dispose();
            Form.Dispose();
        }

        private IntPtr _viewedTexture;
    }

    private static readonly List<OutputWindow> _outputWindows = [];
}
