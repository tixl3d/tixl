using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui.Styling;
using Device = SharpDX.Direct3D11.Device;
using Icon = System.Drawing.Icon;
using Rectangle = System.Drawing.Rectangle;
using Resource = SharpDX.Direct3D11.Resource;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.App;

/// <summary>
/// Functions and properties related to rendering DX11 content into  RenderForm windows
/// </summary>
internal sealed class AppWindow
{
    public IntPtr HwndHandle => Form.Handle;
    public Int2 Size => new(Width, Height);
    public int Width => Form.ClientSize.Width;
    public int Height => Form.ClientSize.Height;
    public bool IsFullScreen => Form.FormBorderStyle == FormBorderStyle.None;

    internal SwapChain SwapChain { get => _swapChain; private set => _swapChain = value; }
    internal RenderTargetView RenderTargetView { get => _renderTargetView; private set => _renderTargetView = value; }
    internal ImGuiDx11RenderForm Form { get; private set; }

    /// <summary>
    /// Set before swap-chain creation. When true, the swap chain is created with
    /// <see cref="SwapChainFlags.FrameLatencyWaitAbleObject"/>; the latency handle is captured
    /// after creation and <see cref="WaitForFrameLatency"/> can be called once per frame as the
    /// pacing primitive — the proper replacement for the accidental pacing that was happening
    /// via the secondary Viewer's `Present(1)` call.
    /// </summary>
    internal bool UseFrameLatencyWaitable { get; set; }

    internal SwapChainDescription SwapChainDescription => new()
                                                              {
                                                                  ModeDescription = new ModeDescription(Width,
                                                                                                        Height,
                                                                                                        new Rational(60, 1),
                                                                                                        Format.R8G8B8A8_UNorm),
                                                                  IsWindowed = true,
                                                                  OutputHandle = Form.Handle,
                                                                  SampleDescription = new SampleDescription(1, 0),

                                                                  BufferCount = 2,
                                                                  SwapEffect = SwapEffect.FlipDiscard,
                                                                  Usage = Usage.RenderTargetOutput,
                                                                  Flags = UseFrameLatencyWaitable
                                                                              ? SwapChainFlags.FrameLatencyWaitAbleObject
                                                                              : SwapChainFlags.None,
                                                              };

    internal bool IsMinimized => Form.WindowState == FormWindowState.Minimized;
    internal bool IsCursorOverWindow => Form.Bounds.Contains(CoreUi.Instance.Cursor.Position);
    public Texture2D Texture { get; set; }

    internal AppWindow(string windowTitle, bool disableClose)
    {
        CreateRenderForm(windowTitle, disableClose);
    }

    public void SetVisible(bool isVisible)
    {
        // Form can be disposed mid-frame (shutdown / display reconfigure);
        // Visible= on a disposed Form throws via CreateHandle.
        if (Form.IsDisposed)
            return;
        Form.Visible = isVisible;
    }

    public void SetSizeable()
    {
        Form.FormBorderStyle = FormBorderStyle.Sizable;
        if (_boundsBeforeFullscreen.Height != 0 && _boundsBeforeFullscreen.Width != 0)
        {
            Form.Bounds = _boundsBeforeFullscreen;
        }
    }

    public void Show() => Form.Show();

    public Vector2 GetDpi()
    {
        using Graphics graphics = Form.CreateGraphics();
        Vector2 dpi = new(graphics.DpiX, graphics.DpiY);
        return dpi;
    }

    internal void SetFullScreen(int screenIndex)
    {
        _boundsBeforeFullscreen = Form.Bounds;
        Form.FormBorderStyle = FormBorderStyle.Sizable;
        Form.WindowState = FormWindowState.Normal;
        Form.FormBorderStyle = FormBorderStyle.None;
        Form.Bounds = Screen.AllScreens[screenIndex].Bounds;
  
    }

    internal void UpdateSpanningBounds(int x, int y, int width, int height)
    {
        if (Form.FormBorderStyle == FormBorderStyle.None)
        {
            Form.Bounds = new Rectangle(x, y, width, height);
        }
        else
        {
            _boundsBeforeFullscreen = Form.Bounds;
            Form.FormBorderStyle = FormBorderStyle.None;
            Form.Bounds = new Rectangle(x, y, width, height);
        }
    }

    internal void InitViewSwapChain(Factory factory)
    {
        SwapChain = new SwapChain(factory, _device, SwapChainDescription);
        SwapChain.ResizeBuffers(bufferCount: 3, Width, Height,
                                SwapChain.Description.ModeDescription.Format, SwapChain.Description.Flags);
        CaptureFrameLatencyWaitableHandleIfEnabled();
    }

    internal void PrepareRenderingFrame()
    {
        _deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, Width, Height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(RenderTargetView);

        var color = UiColors.WindowBackground.ToByte4();
        var sharpDxColor = new SharpDX.Color(color.X, color.Y, color.Z, color.W);
        _deviceContext.ClearRenderTargetView(RenderTargetView, sharpDxColor);
    }

    internal void RunRenderLoop(Action callback)
    {
        _renderCallback = callback;
        RenderLoop.Run(Form, () => callback());
    }

    internal void SetSize(int width, int height) => Form.ClientSize = new Size(width, height);

    internal void SetBorderStyleSizable() => Form.FormBorderStyle = FormBorderStyle.Sizable;

    internal void InitializeWindow(FormWindowState windowState, FormClosingEventHandler handleClose, bool handleKeys)
    {
        InitRenderTargetsAndEventHandlers();

        if (handleKeys)
        {
            MsForms.MsForms.TrackKeysOf(Form);
        }

        MsForms.MsForms.TrackMouseOf(Form);

        if (handleClose != null)
            Form.FormClosing += handleClose;

        Form.WindowState = windowState;
    }

    internal void SetDevice(Device device, DeviceContext deviceContext, SwapChain swapChain = null)
    {
        if (_hasSetDevice)
            throw new InvalidOperationException("Device has already been set");

        _hasSetDevice = true;
        _device = device;
        _deviceContext = deviceContext;
        _swapChain = swapChain;
        CaptureFrameLatencyWaitableHandleIfEnabled();
    }

    /// <summary>
    /// Captures the swap chain's frame-latency waitable handle (if the chain was created with the
    /// matching flag) and sets a low maximum frame latency. The handle must then be waited on once
    /// per frame via <see cref="WaitForFrameLatency"/> as the loop's pacing primitive.
    /// </summary>
    private void CaptureFrameLatencyWaitableHandleIfEnabled()
    {
        if (!UseFrameLatencyWaitable || _swapChain == null)
            return;
        try
        {
            using var swapChain2 = _swapChain.QueryInterface<SwapChain2>();
            _frameLatencyWaitableHandle = swapChain2.FrameLatencyWaitableObject;
            // 1 = lowest latency, 2 = small queue depth (less risk of stalls under jitter).
            swapChain2.MaximumFrameLatency = 2;
        }
        catch (SharpDX.SharpDXException e)
        {
            // QueryInterface or property access can fail on very old DXGI runtimes.
            // Falls back to no-pacing — frame still works, just no waitable benefit.
            Log.Warning($"Could not enable FrameLatencyWaitableObject pacing: {e.Message}");
            _frameLatencyWaitableHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Block until DXGI signals "you may submit the next frame." Should be called once at the very
    /// start of each frame iteration. No-op if <see cref="UseFrameLatencyWaitable"/> wasn't enabled
    /// before swap-chain creation, or if the handle couldn't be obtained.
    /// </summary>
    public void WaitForFrameLatency()
    {
        if (_frameLatencyWaitableHandle == IntPtr.Zero)
            return;
        WaitForSingleObjectEx(_frameLatencyWaitableHandle, 1000, false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObjectEx(IntPtr hHandle, uint dwMilliseconds, bool bAlertable);

    private IntPtr _frameLatencyWaitableHandle;

    internal void Release()
    {
        _renderTargetView.Dispose();
        _backBufferTexture.Dispose();
        _swapChain.Dispose();
    }

    private void CreateRenderForm(string windowTitle, bool disableClose)
    {
        var fileName = Path.Combine(SharedResources.EditorResourcesDirectory, SharedResources.EditorResourcesDirectory, "images", "t3.ico");
        Form = disableClose
                   ? new NoCloseRenderForm(windowTitle)
                         {
                             ClientSize = new Size(640, 360 + 20),
                             Icon = new Icon(fileName, 48, 48),
                             FormBorderStyle = FormBorderStyle.None,
                         }
                   : new ImGuiDx11RenderForm(windowTitle)
                         {
                             ClientSize = new Size(640, 480),
                             Icon = new Icon(fileName, 48, 48)
                         };
    }

    private void InitRenderTargetsAndEventHandlers()
    {
        var device = _device;
        _backBufferTexture = Resource.FromSwapChain<Texture2D>(SwapChain, 0);
        RenderTargetView = new RenderTargetView(device, _backBufferTexture);

        Form.ResizeBegin += (sender, args) => _isResizingRightNow = true;
        Form.ResizeEnd += (sender, args) => _isResizingRightNow = false;
        Form.ClientSizeChanged += (sender, args) =>
                                  {
                                      if (Form.ClientSize.Width == 0 || Form.ClientSize.Height == 0)
                                          return;

                                      RebuildBackBuffer();
                                      // Only Main runs the render loop; Viewer has no callback
                                      if (_isResizingRightNow)
                                          _renderCallback?.Invoke();
                                  };
    }

    private void RebuildBackBuffer()
    {
        // ResizeBuffers requires that no reference to the back buffer survives - including a
        // binding on the output merger. A still-bound RTV leaves the pipeline in undefined
        // state which can escalate to DXGI_ERROR_DEVICE_HUNG on the next Present.
        _deviceContext.OutputMerger.SetTargets((RenderTargetView)null);
        _renderTargetView.Dispose();
        _backBufferTexture.Dispose();

        // Preserve the swap chain's existing flags (in particular FrameLatencyWaitableObject must
        // be carried across resize, otherwise the waitable handle becomes invalid).
        _swapChain.ResizeBuffers(3, Form.ClientSize.Width, Form.ClientSize.Height, Format.Unknown, _swapChain.Description.Flags);
        _backBufferTexture = Resource.FromSwapChain<Texture2D>(_swapChain, 0);
        _renderTargetView = new RenderTargetView(_device, _backBufferTexture);
    }

    /// <summary>
    /// We prevent closing the secondary viewer window for now because
    /// this will cause a SwapChain related crash
    /// </summary>
    private sealed class NoCloseRenderForm : ImGuiDx11RenderForm
    {
        private const int CpNocloseButton = 0x200;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams myCp = base.CreateParams;
                myCp.ClassStyle = myCp.ClassStyle | CpNocloseButton;
                return myCp;
            }
        }

        public NoCloseRenderForm(string title) : base(title)
        {
        }
    }

    private bool _hasSetDevice;
    private Device _device;
    private DeviceContext _deviceContext;
    private SwapChain _swapChain;
    private RenderTargetView _renderTargetView;
    private Texture2D _backBufferTexture;
    public Texture2D BackBufferTexture => _backBufferTexture;
    private bool _isResizingRightNow;
    private Action _renderCallback;
    private Rectangle _boundsBeforeFullscreen;

    public void SetTexture(Texture2D texture)
    {
        Texture = texture;
    }
}