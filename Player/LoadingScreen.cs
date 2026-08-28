#nullable enable
using System;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.Logging;
using AlphaMode = SharpDX.Direct2D1.AlphaMode;
using D2DFactory = SharpDX.Direct2D1.Factory;
using DWriteFactory = SharpDX.DirectWrite.Factory;
using Texture2D = SharpDX.Direct3D11.Texture2D;

namespace T3.Player;

/// <summary>
/// Draws the dark start-up screen (title, status, progress bar, last log line) with Direct2D onto the
/// swap chain's back buffer while the player loads operators and warms up shaders.
/// </summary>
internal sealed class LoadingScreen : IDisposable
{
    public LoadingScreen(string title)
    {
        _title = title;
        try
        {
            _d2dFactory = new D2DFactory(SharpDX.Direct2D1.FactoryType.SingleThreaded);
            _dwriteFactory = new DWriteFactory();
        }
        catch (Exception e)
        {
            Log.Warning($"Loading screen text disabled: {e.Message}");
        }
    }

    /// <summary>
    /// Draws onto <paramref name="backBuffer"/>. <paramref name="progress"/> is 0..1; <paramref name="status"/> is the
    /// current step, <paramref name="lastLogLine"/> the most recent log message.
    /// </summary>
    public void Draw(Texture2D backBuffer, int width, int height, string status, float progress, string? lastLogLine, bool cancelRequested)
    {
        if (_d2dFactory == null || _dwriteFactory == null)
            return;

        if (!EnsureRenderTarget(backBuffer, width, height))
            return;

        var renderTarget = _renderTarget!;
        var brushes = _brushes!;
        renderTarget.BeginDraw();
        renderTarget.Clear(BackgroundColor);

        // Layout scales with the window height so the screen reads the same from 720p to 4K.
        var unit = height / 100f;
        var centerY = height * 0.5f;
        var barWidth = width * 0.4f;
        var barHeight = MathF.Max(2, unit * 0.6f);
        var barLeft = (width - barWidth) * 0.5f;
        var barTop = centerY + unit * 4;

        if (!string.IsNullOrEmpty(_title))
        {
            renderTarget.DrawText(_title, _titleFormat, new RawRectangleF(0, centerY - unit * 14, width, centerY - unit * 4), brushes.Title);
        }

        var statusText = cancelRequested ? "Cancelling..." : status;
        renderTarget.DrawText(statusText, _statusFormat, new RawRectangleF(0, centerY - unit * 2, width, centerY + unit * 3), brushes.Status);

        renderTarget.FillRectangle(new RawRectangleF(barLeft, barTop, barLeft + barWidth, barTop + barHeight), brushes.BarBackground);
        var clamped = Math.Clamp(progress, 0, 1);
        if (clamped > 0)
        {
            renderTarget.FillRectangle(new RawRectangleF(barLeft, barTop, barLeft + barWidth * clamped, barTop + barHeight), brushes.Bar);
        }

        renderTarget.DrawText("Press Esc to cancel", _smallFormat, new RawRectangleF(0, barTop + unit * 2, width, barTop + unit * 5), brushes.Hint);

        if (!string.IsNullOrEmpty(lastLogLine))
        {
            renderTarget.DrawText(lastLogLine, _smallFormat, new RawRectangleF(unit * 2, height - unit * 5, width - unit * 2, height - unit * 1.5f), brushes.Log);
        }

        try
        {
            renderTarget.EndDraw();
        }
        catch (SharpDXException e)
        {
            // D2DERR_RECREATE_TARGET after a device change; the next draw rebuilds the target
            Log.Debug($"Loading screen draw failed: {e.Message}");
            ReleaseRenderTarget();
        }
    }

    public void Dispose()
    {
        ReleaseRenderTarget();
        _titleFormat?.Dispose();
        _statusFormat?.Dispose();
        _smallFormat?.Dispose();
        _dwriteFactory?.Dispose();
        _d2dFactory?.Dispose();
    }

    private bool EnsureRenderTarget(Texture2D backBuffer, int width, int height)
    {
        if (_renderTarget != null && ReferenceEquals(_backBuffer, backBuffer))
            return true;

        ReleaseRenderTarget();
        try
        {
            using var surface = backBuffer.QueryInterface<Surface>();
            var properties = new RenderTargetProperties(new PixelFormat(Format.Unknown, AlphaMode.Premultiplied));
            _renderTarget = new RenderTarget(_d2dFactory, surface, properties);
            _renderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Grayscale;
            _backBuffer = backBuffer;
            _brushes = new Brushes(_renderTarget);

            var unit = height / 100f;
            _titleFormat?.Dispose();
            _statusFormat?.Dispose();
            _smallFormat?.Dispose();
            _titleFormat = CreateFormat(unit * 4.5f, FontWeight.Light);
            _statusFormat = CreateFormat(unit * 2.2f, FontWeight.Normal);
            _smallFormat = CreateFormat(unit * 1.6f, FontWeight.Normal);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to create loading screen render target: {e.Message}");
            ReleaseRenderTarget();
            _d2dFactory?.Dispose();
            _d2dFactory = null;
            return false;
        }
    }

    private TextFormat CreateFormat(float size, FontWeight weight)
    {
        var format = new TextFormat(_dwriteFactory, "Segoe UI", weight, FontStyle.Normal, size)
                         {
                             TextAlignment = TextAlignment.Center,
                             ParagraphAlignment = ParagraphAlignment.Center,
                             WordWrapping = WordWrapping.NoWrap,
                         };
        var trimming = new Trimming { Granularity = TrimmingGranularity.Character };
        format.SetTrimming(trimming, null);
        return format;
    }

    /// <summary>Must be called before the swap chain resizes: the render target holds a reference to the back buffer.</summary>
    public void ReleaseBackBufferResources() => ReleaseRenderTarget();

    private void ReleaseRenderTarget()
    {
        _brushes?.Dispose();
        _brushes = null;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _backBuffer = null;
    }

    private sealed class Brushes : IDisposable
    {
        public Brushes(RenderTarget renderTarget)
        {
            Title = new SolidColorBrush(renderTarget, new RawColor4(0.85f, 0.85f, 0.85f, 1));
            Status = new SolidColorBrush(renderTarget, new RawColor4(0.6f, 0.6f, 0.6f, 1));
            Hint = new SolidColorBrush(renderTarget, new RawColor4(0.3f, 0.3f, 0.3f, 1));
            Log = new SolidColorBrush(renderTarget, new RawColor4(0.35f, 0.35f, 0.35f, 1));
            Bar = new SolidColorBrush(renderTarget, new RawColor4(0.75f, 0.75f, 0.75f, 1));
            BarBackground = new SolidColorBrush(renderTarget, new RawColor4(0.16f, 0.16f, 0.16f, 1));
        }

        public readonly SolidColorBrush Title;
        public readonly SolidColorBrush Status;
        public readonly SolidColorBrush Hint;
        public readonly SolidColorBrush Log;
        public readonly SolidColorBrush Bar;
        public readonly SolidColorBrush BarBackground;

        public void Dispose()
        {
            Title.Dispose();
            Status.Dispose();
            Hint.Dispose();
            Log.Dispose();
            Bar.Dispose();
            BarBackground.Dispose();
        }
    }

    private static readonly RawColor4 BackgroundColor = new(0.05f, 0.05f, 0.05f, 1);

    private readonly string _title;
    private D2DFactory? _d2dFactory;
    private readonly DWriteFactory? _dwriteFactory;
    private RenderTarget? _renderTarget;
    private Texture2D? _backBuffer;
    private Brushes? _brushes;
    private TextFormat? _titleFormat;
    private TextFormat? _statusFormat;
    private TextFormat? _smallFormat;
}
