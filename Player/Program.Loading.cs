using System.IO;
using System.Linq;
using System.Threading;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Compilation;
using T3.Core.DataTypes.Vector;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Settings;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Player;

internal static partial class Program
{
    private static void LoadOperators()
    {
        var searchDirectory = Path.Combine(FileLocations.StartFolder, FileLocations.OperatorsSubFolder);
        Log.Info($"Loading operators from \"{searchDirectory}\"...");

        var assemblies = Directory.GetDirectories(searchDirectory, "*", SearchOption.TopDirectoryOnly)
                                  .Select(packageDir =>
                                          {
                                              var releaseInfoPath = Path.Combine(packageDir, ReleaseInfo.FileName);
                                              var assetsOnlyPath = !File.Exists(releaseInfoPath);
                                              if (assetsOnlyPath)
                                                  return null;
                                              
                                              var assemblyInformation = new AssemblyInformation(packageDir);
                                              Log.Debug($"Searching for dlls in {packageDir}...");
                                              return assemblyInformation; 
                                          })
                                  .Where(x => x != null)
                                  .ToArray();
        
        Log.Debug($"Finished loading {assemblies.Length} operator assemblies. Loading symbols...");
        var packageLoadInfo = assemblies
                             //.AsParallel()
                             .Select(assemblyInfo =>
                                     {
                                         var symbolPackage = new PlayerSymbolPackage(assemblyInfo);
                                         symbolPackage.LoadSymbols(false, out var newSymbolsWithFiles, out _);
                                         return new PackageLoadInfo(symbolPackage, newSymbolsWithFiles);
                                     })
                             .ToArray();

        packageLoadInfo
           .AsParallel()
           .ForAll(packageInfo => SymbolPackage.ApplySymbolChildren(packageInfo.NewlyLoadedSymbols));
    }
    
    private static void PreloadShadersAndResources(double durationSecs,
                                                   Int2 resolution,
                                                   Playback playback,
                                                   DeviceContext deviceContext,
                                                   EvaluationContext context,
                                                   Slot<Texture2D> textureOutput,
                                                   SwapChain swapChain,
                                                   RenderTargetView renderView)
    {
        var previousSpeed = playback.PlaybackSpeed;
        var originalTime = playback.TimeInSecs;
        var wasWindowVisible = _renderForm?.Visible ?? true;
        var audio = CompositionSettings.Current.Audio;
        var previousSoundtrackMute = audio.SoundtrackMute;
        var previousGlobalMute = CoreSettings.Config.AppMute;
        var hideDisplayDuringPreload = true;
        var muteAudioDuringPreload = true;
        const double subFrameWarmOffsetInSecs = 1.0 / 60.0;

        if (muteAudioDuringPreload)
        {
            audio.SoundtrackMute = true;
            AudioEngine.SetSoundtrackMute(true);
            AudioEngine.SetGlobalMute(true);
        }

        if (hideDisplayDuringPreload && _renderForm != null)
        {
            _renderForm.Visible = false;
        }

        playback.PlaybackSpeed = 0;
        var reportedTextureInitFailure = false;

        try
        {
            for (double timeInSecs = 0; timeInSecs < durationSecs; timeInSecs += 2.0)
            {
                var barsAtSample = playback.BarsFromSeconds(timeInSecs);
                Log.Info($"Pre-evaluate at: {timeInSecs:0.00}s / {barsAtSample:0.00} bars");

                var frameWasDrawn = PreloadSampleAtTime(timeInSecs);
                if (!frameWasDrawn && !reportedTextureInitFailure)
                {
                    Log.Error("Failed to initialize texture during preload");
                    reportedTextureInitFailure = true;
                }

                var warmupTimeInSecs = timeInSecs + subFrameWarmOffsetInSecs;
                if (warmupTimeInSecs < durationSecs)
                {
                    PreloadSampleAtTime(warmupTimeInSecs);
                }

                Thread.Sleep(20);
                if (hideDisplayDuringPreload)
                {
                    // Ensure GPU work gets submitted even when preload frames are not presented.
                    deviceContext.Flush();
                }
                else
                {
                    swapChain.Present(1, PresentFlags.None);
                }
            }
        }
        finally
        {
            playback.PlaybackSpeed = previousSpeed;
            playback.TimeInSecs = originalTime;

            if (muteAudioDuringPreload)
            {
                AudioEngine.SetGlobalMute(previousGlobalMute);
                audio.SoundtrackMute = previousSoundtrackMute;
                AudioEngine.SetSoundtrackMute(previousSoundtrackMute);
            }

            if (hideDisplayDuringPreload && _renderForm != null)
            {
                _renderForm.Visible = wasWindowVisible;
            }
        }

        bool PreloadSampleAtTime(double sampleTimeInSecs)
        {
            playback.TimeInSecs = sampleTimeInSecs;
            playback.Update();

            if (_soundtrackHandle != null)
            {
                AudioEngine.UseSoundtrackClip(_soundtrackHandle, playback.TimeInSecs);
            }

            AudioEngine.CompleteFrame(playback, Playback.LastFrameDuration);

            DirtyFlag.IncrementGlobalTicks();
            DirtyFlag.GlobalInvalidationTick++;

            return EvaluateAndDrawOutput(context, resolution, textureOutput, deviceContext, renderView);
        }
    }
}