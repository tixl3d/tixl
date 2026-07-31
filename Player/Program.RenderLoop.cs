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
            // Clip TimeRange.Start is in bars; convert to seconds for the end-of-clip comparison.
            var clipStartSecs = _playback.SecondsFromBars(_soundtrackHandle.Clip.TimeRange.Start);
            if (timeInSecs >= _soundtrackHandle.Clip.LengthInSeconds + clipStartSecs)
            {
                if (_resolvedOptions.Loop)
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
        deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, resolution.Width, resolution.Height, 0.0f, 1.0f));
        deviceContext.OutputMerger.SetTargets(renderView);

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
        deviceContext.ClearRenderTargetView(renderView, new Color(0.45f, 0.55f, 0.6f, 1.0f));
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