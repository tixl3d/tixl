using System;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Stats;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Player;

internal static partial class Program
{
    // todo - share this function with the editor ? is that possible? it could have delegate arguments
    private static void RenderCallback()
    {
        EnsureBackBufferSize();
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

        EvaluateAndDrawOutput(_evalContext, _resolution, _textureOutput, _deviceContext, _renderView);

        _swapChain.Present(_vsyncInterval, PresentFlags.None);

        PerformanceMetrics.RecordFrame((float)(Playback.LastFrameDuration * 1000.0));
    }
    
    private class TimelineEndedException : Exception
    {
    }

    private static bool EvaluateAndDrawOutput(EvaluationContext evalContext,
                                              T3.Core.DataTypes.Vector.Int2 resolution,
                                              Slot<Texture2D> textureOutput,
                                              DeviceContext deviceContext,
                                              RenderTargetView renderView)
    {
        // The output is rendered at the requested resolution and stretched onto the back buffer,
        // whose size follows the window (borderless fullscreen may differ from the requested size).
        deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, _backBufferSize.Width, _backBufferSize.Height, 0.0f, 1.0f));
        deviceContext.OutputMerger.SetTargets(renderView);

        // Clear before evaluating: with a flip-model swap chain an un-drawn back buffer is undefined
        // (typically white), which hides the fact that the output produced nothing.
        deviceContext.ClearRenderTargetView(renderView, new Color(0.45f, 0.55f, 0.6f, 1.0f));

        evalContext.Reset();
        evalContext.RequestedResolution = resolution;

        if (textureOutput == null)
        {
            return false;
        }

        textureOutput.InvalidateGraph();
        var outputTexture = textureOutput.GetValue(evalContext);
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