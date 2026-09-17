#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ImGuiNET;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiContentDrawing;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.App;

/// <summary>
/// Keeps the main window alive while the main thread is stalled by long synchronous work
/// (compiling, loading packages, an operator blocking in its update).
/// </summary>
/// <remarks>
/// A second thread watches the frame heartbeat. Once the main thread neither renders nor pumps
/// window messages, it presents the last captured frame with <see cref="StallOverlay"/> on top,
/// until the main thread renders again. The main thread may be stalled in the middle of a frame, so
/// this thread must not touch ImGui and only reaches the immediate context through one atomic
/// <see cref="DeviceContext.ExecuteCommandList"/> that restores the pipeline state.
/// </remarks>
internal static class StallWatchdog
{
    /// <summary>
    /// Guards everything the watchdog reads while drawing: the back buffer view, the captured frame
    /// and the font atlas. The main thread takes it when replacing any of these and when presenting.
    /// </summary>
    internal static readonly Lock PresentLock = new();

    internal static void Start(Device device, AppWindow mainWindow, WindowsUiContentDrawer contentDrawer)
    {
        if (_thread != null)
            return;

        _device = device;
        _mainWindow = mainWindow;
        _contentDrawer = contentDrawer;

        // The handle getter of a Form throws when called from a foreign thread.
        _mainWindowHandle = mainWindow.HwndHandle;

        try
        {
            using var multithread = device.QueryInterface<Multithread>();
            multithread.SetMultithreadProtected(true);
        }
        catch (Exception e)
        {
            Log.Warning($"Stall overlay disabled, because the graphics device can't be shared between threads: {e.Message}");
            return;
        }

        // A ghosted window is replaced by a frozen copy and would hide everything presented here.
        DisableProcessWindowsGhosting();

        MainThreadActivity.InitializeForMainThread();
        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        _thread = new Thread(Run) { IsBackground = true, Name = "StallWatchdog", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    internal static void Stop()
    {
        lock (PresentLock)
        {
            _isStopped = true;
            DisposeDeviceObjects();
        }
    }

    /// <summary>To be called by the main thread at the beginning of each frame.</summary>
    internal static void NotifyFrameStarted()
    {
        Volatile.Write(ref _lastHeartbeatTimestamp, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// To be called by the main thread right before it renders into the back buffer. Ends a takeover.
    /// </summary>
    /// <returns>True if the watchdog presented since the frame started, which discarded the back buffer's content.</returns>
    internal static bool NotifyRenderingBackBuffer()
    {
        lock (PresentLock)
        {
            Volatile.Write(ref _lastHeartbeatTimestamp, Stopwatch.GetTimestamp());
            var hasPresented = _hasPresentedSinceLastFrame;
            _hasPresentedSinceLastFrame = false;
            return hasPresented;
        }
    }

    private static void Run()
    {
        var takeoverStartTimestamp = 0L;

        while (!_isStopped && !Program.IsShuttingDown)
        {
            if (!IsMainThreadStalled())
            {
                takeoverStartTimestamp = 0;
                Thread.Sleep(IdlePollMilliseconds);
                continue;
            }

            if (takeoverStartTimestamp == 0)
                takeoverStartTimestamp = Stopwatch.GetTimestamp();

            try
            {
                PresentOverlay(Stopwatch.GetElapsedTime(takeoverStartTimestamp).TotalSeconds);
            }
            catch (Exception e)
            {
                Log.Warning($"Stall overlay disabled after failing to present: {e.Message}");
                return;
            }

            Thread.Sleep(OverlayFrameMilliseconds);
        }
    }

    private static bool IsMainThreadStalled()
    {
        var lastHeartbeat = Volatile.Read(ref _lastHeartbeatTimestamp);
        if (Stopwatch.GetElapsedTime(lastHeartbeat).TotalSeconds < StallThresholdSeconds)
            return false;

        if (IsIconic(_mainWindowHandle))
            return false;

        // Modal loops (dragging the window, menus, message boxes, file dialogs) stop the render
        // loop as well, but they still answer messages. Only an unresponsive thread is a stall.
        var isResponding = SendMessageTimeout(_mainWindowHandle, WmNull, IntPtr.Zero, IntPtr.Zero,
                                              SmtoAbortIfHung, HungCheckTimeoutMilliseconds, out _) != IntPtr.Zero;
        return !isResponding;
    }

    private static void PresentOverlay(double secondsSinceTakeover)
    {
        lock (PresentLock)
        {
            // The main thread might have resumed while we waited for the lock.
            var lastHeartbeat = Volatile.Read(ref _lastHeartbeatTimestamp);
            if (_isStopped || Stopwatch.GetElapsedTime(lastHeartbeat).TotalSeconds < StallThresholdSeconds)
                return;

            var capturedFrameSrv = ProgramWindows.UiCopyTextureSrv;
            var renderTargetView = _mainWindow!.RenderTargetView;
            if (capturedFrameSrv == null || capturedFrameSrv.IsDisposed || renderTargetView == null || renderTargetView.IsDisposed)
                return;

            var textureDescription = ProgramWindows.UiCopyTextureDescription;
            var size = new Vector2(textureDescription.Width, textureDescription.Height);

            var vertexCount = StallOverlay.Build(size, secondsSinceTakeover);
            if (vertexCount == 0)
                return;

            InitDeviceObjects();

            var context = _deferredContext!;
            context.MapSubresource(_vertexBuffer, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out var vertexStream);
            vertexStream.WriteRange(StallOverlay.Vertices, 0, vertexCount);
            vertexStream.Dispose();
            context.UnmapSubresource(_vertexBuffer, 0);

            var projection = Matrix4x4.CreateOrthographicOffCenter(0, size.X, size.Y, 0, -1, 1);
            context.MapSubresource(_projectionBuffer, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out var projectionStream);
            projectionStream.Write(projection);
            projectionStream.Dispose();
            context.UnmapSubresource(_projectionBuffer, 0);

            if (!_contentDrawer!.TryBindUiPipeline(context, out var fontAtlasSrv))
                return;

            context.OutputMerger.SetTargets(renderTargetView);
            context.Rasterizer.SetViewport(0, 0, size.X, size.Y);
            context.Rasterizer.SetScissorRectangle(0, 0, (int)size.X, (int)size.Y);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Unsafe.SizeOf<ImDrawVert>(), 0));
            context.VertexShader.SetConstantBuffer(0, _projectionBuffer);

            // The captured frame carries arbitrary alpha and must replace the discarded back buffer.
            context.OutputMerger.SetBlendState(null, null, -1);
            context.PixelShader.SetShaderResource(0, capturedFrameSrv);
            context.Draw(StallOverlay.ScreenVertexCount, 0);

            if (vertexCount > StallOverlay.ScreenVertexCount)
            {
                // Restores alpha blending for the overlay.
                _contentDrawer.TryBindUiPipeline(context, out _);
                context.PixelShader.SetShaderResource(0, fontAtlasSrv);
                context.Draw(vertexCount - StallOverlay.ScreenVertexCount, StallOverlay.ScreenVertexCount);
            }

            context.PixelShader.SetShaderResource(0, null);

            using var commandList = context.FinishCommandList(false);
            _device!.ImmediateContext.ExecuteCommandList(commandList, true);
            _mainWindow.SwapChain.Present(0, PresentFlags.None);
            _hasPresentedSinceLastFrame = true;
        }
    }

    private static void InitDeviceObjects()
    {
        if (_deferredContext != null)
            return;

        _deferredContext = new DeviceContext(_device);
        _vertexBuffer = new Buffer(_device, new BufferDescription
                                                {
                                                    SizeInBytes = StallOverlay.Vertices.Length * Unsafe.SizeOf<ImDrawVert>(),
                                                    Usage = ResourceUsage.Dynamic,
                                                    BindFlags = BindFlags.VertexBuffer,
                                                    CpuAccessFlags = CpuAccessFlags.Write
                                                });
        _projectionBuffer = new Buffer(_device, new BufferDescription
                                                    {
                                                        SizeInBytes = Unsafe.SizeOf<Matrix4x4>(),
                                                        Usage = ResourceUsage.Dynamic,
                                                        BindFlags = BindFlags.ConstantBuffer,
                                                        CpuAccessFlags = CpuAccessFlags.Write
                                                    });
    }

    private static void DisposeDeviceObjects()
    {
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _projectionBuffer?.Dispose();
        _projectionBuffer = null;
        _deferredContext?.Dispose();
        _deferredContext = null;
    }

    [DllImport("user32.dll")]
    private static extern void DisableProcessWindowsGhosting();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam,
                                                    uint flags, uint timeoutMilliseconds, out IntPtr result);

    private const uint WmNull = 0x0000;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint HungCheckTimeoutMilliseconds = 100;

    private const double StallThresholdSeconds = 0.5;
    private const int IdlePollMilliseconds = 100;
    private const int OverlayFrameMilliseconds = 33;

    private static Thread? _thread;
    private static Device? _device;
    private static AppWindow? _mainWindow;
    private static WindowsUiContentDrawer? _contentDrawer;
    private static IntPtr _mainWindowHandle;
    private static DeviceContext? _deferredContext;
    private static Buffer? _vertexBuffer;
    private static Buffer? _projectionBuffer;
    private static long _lastHeartbeatTimestamp;
    private static bool _hasPresentedSinceLastFrame;
    private static volatile bool _isStopped;
}
