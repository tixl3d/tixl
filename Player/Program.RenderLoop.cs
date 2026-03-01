using System;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator.Slots;

namespace T3.Player;

internal static partial class Program
{
    // todo - share this function with the editor ? is that possible? it could have delegate arguments
    private static void RenderCallback()
    {
        WasapiAudioInput.StartFrame(_playback.Settings);
        _playback.Update();

        //Log.Debug($" render at playback time {_playback.TimeInSecs:0.00}s");
        if (_soundtrackHandle != null)
        {
            AudioEngine.UseSoundtrackClip(_soundtrackHandle, _playback.TimeInSecs);
            if (_playback.TimeInSecs >= _soundtrackHandle.Clip.LengthInSeconds + _soundtrackHandle.Clip.StartTime)
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

        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, _resolution.Width, _resolution.Height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(_renderView);

        _evalContext.Reset();
        _evalContext.RequestedResolution = _resolution;

        if (_textureOutput != null)
        {
            _textureOutput.InvalidateGraph();
            var outputTexture = _textureOutput.GetValue(_evalContext);
            var textureChanged = outputTexture != _outputTexture;

            if (_outputTexture != null || textureChanged)
            {
                _outputTexture = outputTexture;
                _deviceContext.Rasterizer.State = _rasterizerState;
                if (_fullScreenVertexShaderResource?.Value != null)
                    _deviceContext.VertexShader.Set(_fullScreenVertexShaderResource.Value);
                if (_fullScreenPixelShaderResource?.Value != null)
                    _deviceContext.PixelShader.Set(_fullScreenPixelShaderResource.Value);

                if (_outputTextureSrv == null || textureChanged)
                {
                    Log.Debug("Creating new srv...");
                    _outputTextureSrv = new ShaderResourceView(_device, _outputTexture);
                }

                var pixelShader = _deviceContext.PixelShader;
                pixelShader.SetShaderResource(0, _outputTextureSrv);

                _deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                _deviceContext.ClearRenderTargetView(_renderView, new Color(0.45f, 0.55f, 0.6f, 1.0f));
                _deviceContext.Draw(3, 0);
                pixelShader.SetShaderResource(0, null);
            }
        }

        _swapChain.Present(_vsyncInterval, PresentFlags.None);
    }
    
    private class TimelineEndedException : Exception
    {
    }
}