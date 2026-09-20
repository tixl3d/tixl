#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using T3.Graphics.Compat;
using T3.Graphics;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.SystemUi;
using Device = T3.Graphics.Compat.Device;
using Color = SharpDX.Color;
using Viewport = T3.Graphics.Viewport;
using Texture2D = T3.Core.DataTypes.Texture2D;

using System.Numerics;

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

            var display = displays[binding.DisplayIndex];
            var displayId = _displayProvider.GetDisplayId(binding.DisplayIndex);
            if (_mainWindowOutputId == Guid.Empty)
            {
                // The main window already exists and runs the render loop, so it carries the first binding.
                _mainWindowOutputId = output.Id;
                _mainWindow.MoveToDisplay(displayId);
                _isFullScreen = true;
                _mainWindow.SetFullscreen(true);
                Log.Info($"Output \"{output.Name}\" presents on display {binding.DisplayIndex + 1}.");
                continue;
            }

            var extra = TryOpenOutputWindow(output.Id, output.Name, display, displayId);
            if (extra != null)
            {
                _outputWindows.Add(extra);
                Log.Info($"Output \"{output.Name}\" presents on display {binding.DisplayIndex + 1}.");
            }
        }
    }

    private static OutputWindow? TryOpenOutputWindow(Guid outputId, string outputName, DisplayInfo display, SDL.SDL_DisplayID displayId)
    {
        PlayerWindow? window = null;
        try
        {
            var size = new Size(display.CurrentMode.Width, display.CurrentMode.Height);
            window = new PlayerWindow(outputName, size, displayId, null);
            window.SetFullscreen(true);
            window.Show();
            window.CreateSwapChain(_device);
            return new OutputWindow(outputId, window);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not open a window for output \"{outputName}\": {e.Message}");
            window?.Dispose();
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
            window.Window.EnsureBackBufferSize(_device, null);
            var composite = OutputCompositor.RenderOutput(window.OutputId);
            if (composite == null)
                continue;

            window.EnsureTextureView(_device, composite);
            if (window.TextureView == null)
                continue;

            var backBufferSize = window.Window.BackBufferSize;
            _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, backBufferSize.Width, backBufferSize.Height, 0f, 1f));
            _deviceContext.OutputMerger.SetTargets(window.Window.RenderTargetView);
            _deviceContext.ClearRenderTargetView(window.Window.RenderTargetView, new Vector4(0, 0, 0, 1));

            _deviceContext.Rasterizer.State = _rasterizerState;
            if (_fullScreenVertexShaderResource?.Value != null)
                _deviceContext.VertexShader.Set(_fullScreenVertexShaderResource.Value);

            if (_fullScreenPixelShaderResource?.Value != null)
                _deviceContext.PixelShader.Set(_fullScreenPixelShaderResource.Value);

            _deviceContext.PixelShader.SetShaderResource(0, window.TextureView);
            _deviceContext.InputAssembler.PrimitiveTopology = T3.Graphics.Compat.PrimitiveTopology.TriangleList;
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
            _outputWindows[i].Window.SwapChain.Present(_vsyncInterval);
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

    private sealed class OutputWindow(Guid outputId, PlayerWindow window)
    {
        public Guid OutputId { get; } = outputId;
        public PlayerWindow Window { get; } = window;
        public ShaderResourceView? TextureView { get; private set; }

        /// <summary>Rebuilds the view only when a different composite arrives; the texture is reused across frames.</summary>
        public void EnsureTextureView(Device device, Texture2D composite)
        {
            if (composite.IsDisposed)
                return;

            var nativePointer = ((T3.Graphics.Compat.Texture2D)composite).NativePointer;
            if (TextureView != null && !TextureView.IsDisposed && _viewedTexture == nativePointer)
                return;

            TextureView?.Dispose();
            TextureView = new ShaderResourceView(device, composite);
            _viewedTexture = nativePointer;
        }

        public void Dispose()
        {
            TextureView?.Dispose();
            Window.Dispose();
        }

        private IntPtr _viewedTexture;
    }

    private static readonly List<OutputWindow> _outputWindows = [];
}
