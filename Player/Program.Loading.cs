using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
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
    /// <summary>
    /// Redraws the loading screen, processes window messages and returns false once the user cancelled.
    /// Loading runs on the main thread in steps; callers pump between steps so the window stays responsive.
    /// </summary>
    private static bool PumpLoadingScreen(string status, float progress)
    {
        Application.DoEvents();
        if (_renderForm == null || _renderForm.IsDisposed)
            _loadCancelled = true;

        if (_loadCancelled)
            return false;

        if (_loadingScreen == null)
            return true;

        EnsureBackBufferSize();
        _loadingScreen.Draw(_backBuffer, _backBufferSize.Width, _backBufferSize.Height, status, progress, _lastLogLine?.Text, false);
        _swapChain.Present(1, PresentFlags.None);
        return true;
    }

    private static bool LoadOperators(PlayerLoadReport report)
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
        report.PackageCount = assemblies.Length;

        var packageLoadInfo = new List<PackageLoadInfo>(assemblies.Length);
        for (var index = 0; index < assemblies.Length; index++)
        {
            var assemblyInfo = assemblies[index];
            var packageName = Path.GetFileName(assemblyInfo.Directory);
            var progress = LoadProgressOperatorsStart + (LoadProgressOperatorsEnd - LoadProgressOperatorsStart) * index / assemblies.Length;
            if (!PumpLoadingScreen($"Loading {packageName} ({index + 1}/{assemblies.Length})", progress))
                return false;

            Log.Info($"Loading package {packageName}...");
            var symbolPackage = new PlayerSymbolPackage(assemblyInfo);
            symbolPackage.LoadSymbols(false, out var newSymbolsWithFiles, out _);
            packageLoadInfo.Add(new PackageLoadInfo(symbolPackage, newSymbolsWithFiles));
            report.SymbolCount += symbolPackage.Symbols.Count;
        }

        if (!PumpLoadingScreen("Connecting operators...", LoadProgressOperatorsEnd))
            return false;

        packageLoadInfo
           .AsParallel()
           .ForAll(packageInfo => SymbolPackage.ApplySymbolChildren(packageInfo.NewlyLoadedSymbols));

        return true;
    }

    private static int CountInstances(Instance instance)
    {
        var count = 1;
        foreach (var child in instance.Children.Values)
        {
            count += CountInstances(child);
        }

        return count;
    }

    /// <summary>
    /// Steps through the timeline so every shader compiles and every resource loads before playback starts.
    /// Frames are evaluated but not shown; the loading screen is drawn over them instead.
    /// </summary>
    private static bool PreloadShadersAndResources(double durationSecs,
                                                   Int2 resolution,
                                                   Playback playback,
                                                   DeviceContext deviceContext,
                                                   EvaluationContext context,
                                                   Slot<Texture2D> textureOutput,
                                                   RenderTargetView renderView)
    {
        var previousSpeed = playback.PlaybackSpeed;
        var originalTime = playback.TimeInSecs;
        var audio = CompositionSettings.Current.Audio;
        var previousSoundtrackMute = audio.SoundtrackMute;
        var previousGlobalMute = CoreSettings.Config.AppMute;
        const double subFrameWarmOffsetInSecs = 1.0 / 60.0;
        const double sampleStepInSecs = 2.0;

        audio.SoundtrackMute = true;
        AudioEngine.SetSoundtrackMute(true);
        AudioEngine.SetGlobalMute(true);

        playback.PlaybackSpeed = 0;
        var reportedTextureInitFailure = false;
        var completed = false;

        try
        {
            for (double timeInSecs = 0; timeInSecs < durationSecs; timeInSecs += sampleStepInSecs)
            {
                var progress = LoadProgressPreloadStart + (LoadProgressPreloadEnd - LoadProgressPreloadStart) * (float)(timeInSecs / durationSecs);
                if (!PumpLoadingScreen($"Warming up shaders and resources ({timeInSecs:0}s / {durationSecs:0}s)", progress))
                    return false;

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

                // Ensure GPU work gets submitted even when preload frames are not presented.
                deviceContext.Flush();
            }

            completed = true;
        }
        finally
        {
            playback.PlaybackSpeed = previousSpeed;
            playback.TimeInSecs = originalTime;

            AudioEngine.SetGlobalMute(previousGlobalMute);
            audio.SoundtrackMute = previousSoundtrackMute;
            AudioEngine.SetSoundtrackMute(previousSoundtrackMute);
        }

        return completed;

        bool PreloadSampleAtTime(double sampleTimeInSecs)
        {
            playback.TimeInSecs = sampleTimeInSecs;
            playback.Update();

            // Register every cached clip so all streams stay loaded during preload sampling.
            foreach (var handle in _allSoundtrackHandles)
            {
                AudioEngine.UseSoundtrackClip(handle, playback.TimeInSecs);
            }

            AudioEngine.CompleteFrame(playback, Playback.LastFrameDuration);

            DirtyFlag.IncrementGlobalTicks();
            DirtyFlag.GlobalInvalidationTick++;

            return EvaluateAndDrawOutput(context, resolution, textureOutput, deviceContext, renderView);
        }
    }

    // Progress bar fractions of the loading stages
    private const float LoadProgressOperatorsStart = 0.05f;
    private const float LoadProgressOperatorsEnd = 0.5f;
    private const float LoadProgressInstance = 0.55f;
    private const float LoadProgressPreloadStart = 0.6f;
    private const float LoadProgressPreloadEnd = 1f;

    private static LoadingScreen _loadingScreen;
    private static LastLogLineWriter _lastLogLine;
    private static bool _isLoading;
    private static bool _loadCancelled;
}
