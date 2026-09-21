#nullable enable
using System;
using T3.Graphics.Compat;
using T3.Graphics;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Output.Rendering;
using T3.Core.Output;
using T3.Core.Operator.Slots;
using T3.Core.Stats;
using Texture2D = T3.Core.DataTypes.Texture2D;

using System.Numerics;

namespace T3.Player;

internal static partial class Program
{
    // todo - share this function with the editor ? is that possible? it could have delegate arguments
    private static void RenderCallback()
    {
        EnsureBackBufferSize();

        // The backends record into a frame and submit it at the end; D3D11's immediate context needed no such
        // bracketing, which is why the render loop never had it.
        _device.BeginFrame();
        WasapiAudioInput.StartFrame(_playback.Settings);
        _playback.Update();

        //Log.Debug($" render at playback time {_playback.TimeInSecs:0.00}s");
        // Register every cached clip so the engine plays them all in parallel.
        var timeInSecs = _playback.TimeInSecs;
        foreach (var handle in _allSoundtrackHandles)
        {
            AudioEngine.UseSoundtrackClip(handle, timeInSecs);
        }

        // Op-provided audio registers itself each frame, like in the editor: [AudioClip] ops with AutoPlay
        // (the canonical soundtrack form — the settings list is migration-source-only) and loose audio-graph
        // sources playing through the implicit default bus.
        AudioClipCollector.RegisterAutoPlayClips(_project, _playback.TimeInBars, timeInSecs);
        AudioGraphCollector.CollectLooseSources(_project);

        // End-of-timeline check is driven by the main soundtrack only.
        if (_soundtrackHandle != null)
        {
            // An explicitly trimmed clip end wins over the file's length — the demo ends where the
            // soundtrack was trimmed to, not where the source file happens to stop.
            var clip = _soundtrackHandle.Clip;
            var clipStartSecs = _playback.SecondsFromBars(clip.TimeRange.Start);
            var endInSecs = clip.TimeRange.End > clip.TimeRange.Start
                                ? _playback.SecondsFromBars(clip.TimeRange.End)
                                : clip.LengthInSeconds + clipStartSecs;
            if (timeInSecs >= endInSecs)
            {
                if (_startupOptions.Loop)
                {
                    _playback.TimeInSecs = 0.0;
                }
                else
                {
                    throw new TimelineEndedException();
                }
            }
        }

        // Update
        AudioEngine.CompleteFrame(_playback, Playback.LastFrameDuration);

        DirtyFlag.IncrementGlobalTicks();
        DirtyFlag.GlobalInvalidationTick++;

        // Under Vulkan this is what acquires the image for this frame, so it has to happen every frame
        // and after the graph has been updated, not once at start-up.
        _mainWindow.AcquireBackBuffer(_device);
        EvaluateAndDrawOutput(_resolution, _deviceContext, _mainWindow.RenderTargetView);

        _mainWindow.SwapChain.Present(_vsyncInterval);
        PresentOutputWindows();
        _device.EndFrame();


        PerformanceMetrics.RecordFrame((float)(Playback.LastFrameDuration * 1000.0));
    }
    
    private class TimelineEndedException : Exception
    {
    }

    /// <summary>
    /// What the main window shows: its bound output's composite, else the first sending output's. A project whose
    /// sends reach no output — no setup, or nothing routed yet — shows its first send's own texture instead, so a
    /// quick export works before anyone has opened the output setup.
    /// </summary>
    private static Texture2D? RenderMainWindowTexture(T3.Core.DataTypes.Vector.Int2 resolution)
    {
        var setup = ActiveSetup.Current;
        if (setup != null)
        {
            // Every other display is drawn first, while the back buffer is still free.
            DrawOutputWindows();

            if (_mainWindowOutputId != Guid.Empty)
                return OutputCompositor.RenderOutput(_mainWindowOutputId);

            for (var i = 0; i < setup.Outputs.Count; i++)
            {
                var output = setup.Outputs[i];
                if (output.Kind == OutputDefinition.Kinds.Default || !output.IsSending)
                    continue;

                var composite = OutputCompositor.RenderOutput(output.Id);
                if (composite != null)
                    return composite;
            }
        }

        if (_sends.Count == 0 || _sends[0] is not IContentSupplier firstSend)
            return null;

        OutputContentResolver.PrepareContext(resolution);
        return OutputContentResolver.PullContent(firstSend);
    }

    private static bool EvaluateAndDrawOutput(T3.Core.DataTypes.Vector.Int2 resolution,
                                              DeviceContext deviceContext,
                                              RenderTargetView renderView)
    {
        // One token per frame for everything the compositing path memoises, advanced before anything asks.
        OutputFrame.Advance();

        // Operators that render off-screen save and restore the bound viewports, and SharpDX's GetViewports
        // throws when none is bound at all — so one is bound before any content runs, as the editor always has.
        var backBufferSize = _mainWindow.BackBufferSize;
        var backBufferViewport = new Viewport(0, 0, backBufferSize.Width, backBufferSize.Height, 0.0f, 1.0f);
        deviceContext.Rasterizer.SetViewport(backBufferViewport);

        // Composited first: the compositor binds render targets of its own and leaves them bound, so the back
        // buffer is claimed after it is done rather than before.
        var outputTexture = RenderMainWindowTexture(resolution);
        SendStreams();

        // The output is rendered at the requested resolution and stretched onto the back buffer,
        // whose size follows the window (borderless fullscreen may differ from the requested size).
        deviceContext.Rasterizer.SetViewport(backBufferViewport);
        deviceContext.OutputMerger.SetTargets(renderView);

        // Clear before evaluating: with a flip-model swap chain an un-drawn back buffer is undefined
        // (typically white), which hides the fact that the output produced nothing.
        deviceContext.ClearRenderTargetView(renderView, new Vector4(0.45f, 0.55f, 0.6f, 1.0f));

        if (outputTexture == null)
        {
            if (!_loggedNullOutput)
            {
                _loggedNullOutput = true;
                Log.Warning("Output texture is null - nothing to draw.");
            }
            return false;
        }

        EnsureOutputTextureSrv(outputTexture);

        deviceContext.Rasterizer.State = _rasterizerState;
        if (_fullScreenVertexShaderResource?.Value != null)
            deviceContext.VertexShader.Set(_fullScreenVertexShaderResource.Value);
        if (_fullScreenPixelShaderResource?.Value != null)
            deviceContext.PixelShader.Set(_fullScreenPixelShaderResource.Value);

        var pixelShader = deviceContext.PixelShader;
        pixelShader.SetShaderResource(0, _outputTextureSrv);

        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        deviceContext.Draw(3, 0);
        pixelShader.SetShaderResource(0, null);
        return true;
    }

    private static void EnsureOutputTextureSrv(Texture2D outputTexture)
    {
        if (_outputTextureSrv != null && outputTexture == _outputTexture)
        {
            return;
        }

        _outputTexture = outputTexture;
        _outputTextureSrv?.Dispose();
        Log.Debug("Creating new srv...");
        _outputTextureSrv = new ShaderResourceView(_device, _outputTexture);
    }
}