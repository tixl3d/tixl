using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using t3.streamdiffusion.Onnx;
using T3.Core.Logging;

namespace t3.streamdiffusion.V2;

/// <summary>
/// StreamDiffusion V2: the causal Wan2.1 video DiT, run as a pipelined stream-batch. Every call consumes one input frame
/// and advances all denoising steps at once, each batch element holding a different latent frame at a different noise level.
/// Keys/values of past frames live in a GPU-resident cache owned here; the exported DiT is stateless.
/// </summary>
internal sealed class StreamDiffusionV2Pipeline : IDisposable
{
    public const string DiTFileName = "dit_step.onnx";

    private const int EncoderWindowFrames = 8;
    private const int DefaultDecoderWindowLatents = 3;
    private const int FastDecoderWindowLatents = 1;
    private const int FramesPerLatent = 4;
    private const int LatentChannels = 16;
    private const int TextLength = 512;
    private const int TextFeatureCount = 4096;
    private const int FirstStreamFrame = 2;
    private const int MaxFramePosition = 1000;

    private static readonly int[] DenoisingTimesteps = { 700, 500, 400, 200 };

    private static readonly string[] RequiredFiles =
    {
        DiTFileName, "dit_manifest.json", "cross_kv.onnx", "kv_zeros.onnx", "text_encoder.onnx",
        "vae_encoder.onnx", "vae_decoder.onnx", "tokenizer.json",
    };

    public bool IsInitialized => _dit != null && _tokenizer != null;
    public string ProviderLabel => _dit?.ProviderLabel ?? "CPU";
    public string? InitializationError => _initializationError;

    /// <summary>True while a changed prompt is being encoded in the background; the stream keeps the previous prompt until it lands.</summary>
    public bool IsPromptPending => _promptPending;

    /// <summary>Increments whenever the cross-attention K/V changes. The operator includes this in its stream fingerprint so a
    /// background prompt swap forces one new frame with the new prompt.</summary>
    public int PromptVersion => _promptVersion;

    /// <summary>
    /// Decode only the newest latent instead of a three-latent window. Cuts the decode stage to about 10 ms on
    /// TensorRT (from 11-26 ms) and about 28 ms on CUDA (from 70-105 ms) at the cost of less temporal smoothing;
    /// changing it restarts the stream.
    /// </summary>
    public volatile bool FastDecodeEnabled;

    public bool VerboseLogging
    {
        get => _verboseLogging;
        set => _verboseLogging = value;
    }

    /// <summary>Per-stage wall time of the most recent stream step (milliseconds).</summary>
    public double LastEncodeMs { get; private set; }
    public double LastDenoiseMs { get; private set; }
    public double LastDecodeMs { get; private set; }

    /// <summary>
    /// The noise scale the motion-aware controller actually used for the newest frame: it starts at the
    /// Strength input and is lowered on fast motion (which would tear) and raised towards it on still
    /// scenes (which would freeze). Telemetry; see <see cref="ResetStream"/>.
    /// </summary>
    public float EffectiveNoiseScale => _noiseScale;

    /// <summary>
    /// Drops the rolling KV cache, sink tokens and the decode ring; the next <see cref="StreamStep"/>
    /// re-primes the stream from the incoming frame. Use on scene cuts or when the output drifts.
    /// </summary>
    public void ResetStream()
    {
        _needsPrime = true;
    }

    public static string ValidateModelDirectory(string? modelDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory))
            return "No StreamDiffusion V2 export found. Download the ready-to-run export in Settings > AI Models " +
                   "(or the checkpoint + export scripts for other sizes); the operator picks it up from the model store automatically.";

        if (!Directory.Exists(modelDirectory))
            return $"Model directory not found: {modelDirectory}";

        var missing = new List<string>();
        foreach (var file in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(modelDirectory, file)))
                missing.Add(file);
        }

        return missing.Count == 0 ? string.Empty : $"Missing in '{modelDirectory}': {string.Join(", ", missing)}";
    }

    public bool TryGetFixedOutputSize(out int width, out int height)
    {
        width = _width;
        height = _height;
        return IsInitialized;
    }

    public void ConfigureStream(int steps)
    {
        lock (_lock)
        {
            var clamped = Math.Clamp(steps, 1, DenoisingTimesteps.Length);
            if (clamped == _stepCount)
                return;

            _stepCount = clamped;
            _needsPrime = true;
        }
    }

    public bool Initialize(string modelDirectory, int deviceId, ExecutionProvider provider)
    {
        lock (_lock)
        {
            _initializationError = null;
            _disposed = false;
            _promptGeneration++;
            _pendingPrompt = null;
            _promptPending = false;
            CancelPromptJob();
            _disposeCts = new CancellationTokenSource();
            _failedPrompt = null;
            _promptFailureCount = 0;
            _promptRetryAfterTicks = 0;
            _promptEmbeddings.Clear();
            while (_promptCacheOrder.TryDequeue(out _)) { }

            // Re-initialization (the TensorRT -> CUDA fallback below) must release the previous sessions and
            // GPU-resident caches first; on a fresh instance both calls are no-ops.
            DisposeStreamState();
            DisposeSessions();
            _provider = provider;
            _fallbackAttempted = false;
            _needsPrime = true;

            var error = ValidateModelDirectory(modelDirectory);
            if (error.Length > 0)
            {
                _initializationError = error;
                Log.Error($"[StreamDiffusion] {error}");
                return false;
            }

            if (provider == ExecutionProvider.DirectMl)
            {
                _initializationError = "StreamDiffusion V2 does not support DirectML. Use CUDA or TensorRT.";
                Log.Error("[StreamDiffusion] V2 does not support DirectML: ONNX Runtime's DML provider fails the DiT's 5D " +
                          "reshapes with an internal error. Use CUDA or TensorRT.");
                return false;
            }

            if (provider == ExecutionProvider.Cpu)
            {
                // The DiT is far too large for CPU and the cache I/O is built around GPU memory
                Log.Warning("[StreamDiffusion] V2 is designed for CUDA or TensorRT, but CPU was selected. " +
                            "Expect very slow streaming; switch ExecutionProvider to CUDA.");
            }

            try
            {
                _modelDirectory = modelDirectory;
                _deviceId = deviceId;
                LoadManifest(Path.Combine(modelDirectory, "dit_manifest.json"));
                _tokenizer = Umt5Tokenizer.Load(Path.Combine(modelDirectory, "tokenizer.json"));

                // Explicit profile so one TensorRT engine covers every Steps value (the step count is the batch size)
                var ditProfile = CreateDitTensorRtProfile();
                _dit = OnnxModelSession.Create(Path.Combine(modelDirectory, DiTFileName), deviceId, "V2 DiT", provider, ditProfile);
                _crossKv = OnnxModelSession.Create(Path.Combine(modelDirectory, "cross_kv.onnx"), deviceId, "V2 CrossKV", provider);
                _kvZeros = OnnxModelSession.Create(Path.Combine(modelDirectory, "kv_zeros.onnx"), deviceId, "V2 KvZeros", provider);
                _vaeEncoder = OnnxModelSession.Create(Path.Combine(modelDirectory, "vae_encoder.onnx"), deviceId, "V2 VaeEncoder", provider);
                _vaeDecoder = OnnxModelSession.Create(Path.Combine(modelDirectory, "vae_decoder.onnx"), deviceId, "V2 VaeDecoder", provider,
                                                      CreateDecoderTensorRtProfile(provider));
                if (_dit == null || _crossKv == null || _kvZeros == null || _vaeEncoder == null || _vaeDecoder == null)
                {
                    DisposeSessions();
                    return false;
                }

                _device = _dit.ProviderLabel is "CUDA" or "TensorRT"
                              ? new OrtMemoryInfo("Cuda", OrtAllocatorType.ArenaAllocator, deviceId, OrtMemType.Default)
                              : OrtMemoryInfo.DefaultInstance;

                // The decoder's engine is the one that cannot be built later: the DiT engine (about 5 GB) and the
                // KV caches are resident by then. Building it here, while the card is still mostly empty, is what
                // keeps TensorRT usable on 16 GB GPUs. If it still cannot be built, the decoder moves to CUDA
                // while the DiT stays on TensorRT - the decoder runs through plain Session.Run, so mixed
                // providers are safe and the expensive path keeps its engine.
                if (provider == ExecutionProvider.TensorRt
                    && !WarmUpDecoderEngine()
                    && _vaeDecoder?.ProviderLabel == "TensorRT")
                {
                    Log.Warning("[StreamDiffusion] V2 VaeDecoder: retrying the decoder on CUDA; the DiT stays on TensorRT.");
                    _vaeDecoder.Dispose();
                    _vaeDecoder = OnnxModelSession.Create(Path.Combine(modelDirectory, "vae_decoder.onnx"), deviceId,
                                                          "V2 VaeDecoder", ExecutionProvider.Cuda);
                    if (_vaeDecoder == null)
                    {
                        DisposeSessions();
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _initializationError = ex.Message;
                Log.Error($"[StreamDiffusion] V2 initialization failed: {ex.Message}");
                DisposeSessions();
                return false;
            }
        }
    }

    /// <summary>
    /// Streams one input frame through the causal DiT. The output size is baked into the export,
    /// so there are no width/height arguments; the caller learns it from <see cref="TryGetFixedOutputSize"/>.
    /// </summary>
    public byte[]? StreamStep(string prompt, byte[] rgbaInput, int inputWidth, int inputHeight,
                              float strength, int resizeMode, int seed, bool isBgra, out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (!IsInitialized)
            {
                // A provider fallback that itself failed must say why; otherwise this reads like the pipeline
                // was never set up and hides the real reason (the status line shows the same text).
                error = _initializationError ?? "Pipeline not initialized";
                return null;
            }

            // A decode-window change alters the decoder input shape, so the stream re-primes
            var fastDecode = FastDecodeEnabled;
            if (fastDecode != _fastDecodeActive)
            {
                _fastDecodeActive = fastDecode;
                _decoderWindowLatents = fastDecode ? FastDecoderWindowLatents : DefaultDecoderWindowLatents;
                _needsPrime = true;
            }

            var stage = "resize";
            try
            {
                var resized = RgbaResize.Apply(rgbaInput, inputWidth, inputHeight, _width, _height, resizeMode);
                var frameChange = PushFrame(resized, isBgra);

                if (!string.Equals(prompt, _preparedPrompt, StringComparison.Ordinal))
                {
                    stage = "prompt";
                    EnsurePrompt(prompt);
                }

                // A failed first prompt encode latches a retry backoff and leaves the stream without
                // cross-attention K/V. Priming without them would throw a null reference on every backoff
                // frame (and run the VAE + cache allocation for nothing); report the honest state instead
                // and keep the previous output.
                if (_crossOutputs == null)
                {
                    error = "No prompt embeddings: the prompt encode failed (see log)";
                    return null;
                }

                if (seed != _lastSeed)
                {
                    _lastSeed = seed;
                    _needsPrime = true;
                }

                var initialNoiseScale = Math.Clamp(strength, 0.3f, 1f);
                if (_needsPrime || _newestFrame >= MaxFramePosition)
                {
                    stage = "prime";
                    Prime(seed, initialNoiseScale);
                }

                var (noiseScale, timestep) = MotionNoiseController.Update(frameChange, _noiseScale, initialNoiseScale);
                _noiseScale = noiseScale;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                stage = "encode";
                var clean = EncodeLatestFrame();
                LastEncodeMs = stopwatch.Elapsed.TotalMilliseconds;

                stopwatch.Restart();
                stage = "denoise";
                var finished = Step(clean, noiseScale, timestep);
                LastDenoiseMs = stopwatch.Elapsed.TotalMilliseconds;

                // Until the pipeline is full there is no denoised frame yet; the encoded input stands in so the output never goes blank
                stopwatch.Restart();
                stage = "decode";
                var rgba = DecodeToRgba(finished ?? clean, finished != null);
                LastDecodeMs = stopwatch.Elapsed.TotalMilliseconds;

                if (VerboseLogging)
                    Log.Debug($"[StreamDiffusion] V2 encode {LastEncodeMs:F0} ms, denoise {LastDenoiseMs:F0} ms, decode {LastDecodeMs:F0} ms");

                return rgba;
            }
            catch (Exception ex)
            {
                Log.Error($"[StreamDiffusion] V2 {stage} failed: {ex}");

                // A TensorRT engine is built on the first run of each graph, and that build can fail on a card
                // that is full (the 16 GB laptop cards cannot hold the decoder builder next to the DiT engine).
                // Without this the stream would report the same error every frame and stay wedged; one automatic
                // re-initialization on CUDA recovers it and is reported only once.
                if (_provider == ExecutionProvider.TensorRt
                    && !_fallbackAttempted
                    && IsTensorRtEngineBuildFailure(ex)
                    && _modelDirectory != null)
                {
                    _fallbackAttempted = true;
                    Log.Warning("[StreamDiffusion] TensorRT could not build an engine (out of GPU memory or an " +
                                "unsupported shape); switching this model to CUDA. Change ExecutionProvider or free " +
                                "GPU memory to try TensorRT again.");
                    if (Initialize(_modelDirectory, _deviceId, ExecutionProvider.Cuda))
                    {
                        error = $"V2 {stage}: TensorRT engine build failed, switched to CUDA - streaming resumes on the next frame";
                        return null;
                    }
                }

                error = $"V2 {stage} error: {ex.Message}";
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _promptGeneration++;
            _pendingPrompt = null;
            _promptPending = false;
            CancelPromptJob();
            _disposeCts.Cancel();
            _promptEmbeddings.Clear();
            DisposeStreamState();
            DisposeSessions();
            _textEncoderReleaseTimer?.Dispose();
            _textEncoderReleaseTimer = null;

            // A running encode holds the gate and releases the encoder itself when it sees _disposed
            if (_promptEncodeGate.Wait(0))
            {
                ReleaseTextEncoder();
                _promptEncodeGate.Release();
            }
        }
    }

    private void LoadManifest(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        _latentHeight = root.GetProperty("latentHeight").GetInt32();
        _latentWidth = root.GetProperty("latentWidth").GetInt32();
        _tokensPerFrame = root.GetProperty("tokensPerFrame").GetInt32();
        _cacheFrames = root.GetProperty("cacheFrames").GetInt32();
        _sinkFrames = root.GetProperty("sinkFrames").GetInt32();
        _layers = root.GetProperty("layers").GetInt32();
        _heads = root.GetProperty("heads").GetInt32();
        _headDim = root.GetProperty("headDim").GetInt32();
        _width = _latentWidth * 8;
        _height = _latentHeight * 8;
        _latentLength = LatentChannels * _latentHeight * _latentWidth;
        _frameLength = 3 * _width * _height;
    }

    /// <summary>
    /// The DiT's batch axis is the step count (1-4). TensorRT would otherwise build a multi-GB engine for the exact
    /// traced batch and rebuild it whenever <c>Steps</c> changes; this profile covers the whole range with one engine.
    /// </summary>
    private OnnxModelSession.TensorRtProfile CreateDitTensorRtProfile()
    {
        var cacheTokens = _cacheFrames * _tokensPerFrame;

        string Shapes(int batch)
        {
            return $"latent:{batch}x{LatentChannels}x{_latentHeight}x{_latentWidth}," +
                   $"timestep:{batch},sigma:{batch},frame_pos:{batch}," +
                   $"k_cache:{_layers}x{batch}x{cacheTokens}x{_heads}x{_headDim}," +
                   $"v_cache:{_layers}x{batch}x{cacheTokens}x{_heads}x{_headDim}," +
                   $"cache_mask:{batch}x{cacheTokens},slot:{batch}," +
                   $"cross_k:{_layers}x1x{TextLength}x{_heads}x{_headDim}," +
                   $"cross_v:{_layers}x1x{TextLength}x{_heads}x{_headDim}";
        }

        return new OnnxModelSession.TensorRtProfile(
            Shapes(1), Shapes(Math.Min(2, DenoisingTimesteps.Length)), Shapes(DenoisingTimesteps.Length));
    }

    /// <summary>
    /// The decoder's second axis is the number of latents in its window: 1 with <c>FastDecode</c>, 3 by default.
    /// The export keeps that axis dynamic, so without a profile the TensorRT EP builds a separate engine for each
    /// window at run time - after the DiT engine and the KV caches already occupy the card, which is where engine
    /// builds fail on a 16 GB GPU. One explicit 1-3 profile is built during initialization instead.
    /// </summary>
    private OnnxModelSession.TensorRtProfile? CreateDecoderTensorRtProfile(ExecutionProvider provider)
    {
        if (provider != ExecutionProvider.TensorRt)
            return null;

        // The export names the decoder input "latents" (see tools/streamdiffusionv2/export-vae.py)
        string Shapes(int window) => $"latents:1x{window}x{LatentChannels}x{_latentHeight}x{_latentWidth}";
        return new OnnxModelSession.TensorRtProfile(
            Shapes(1), Shapes(DefaultDecoderWindowLatents), Shapes(DefaultDecoderWindowLatents));
    }

    /// <summary>
    /// Runs the decoder once with a zero latent so its TensorRT engine is built while the card is still mostly
    /// empty. The engine is cached next to the model (trt_cache), so later starts load it instead of rebuilding.
    /// Returns false when the engine could not be built, so the caller can put the decoder on CUDA right away
    /// rather than retrying the build later with the DiT engine and KV caches resident.
    /// </summary>
    private bool WarmUpDecoderEngine()
    {
        if (_vaeDecoder == null || _latentLength <= 0 || _vaeDecoder.ProviderLabel != "TensorRT")
            return true;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dummy = new float[_latentLength];
            var tensor = new DenseTensor<float>(dummy, new[] { 1, 1, LatentChannels, _latentHeight, _latentWidth });
            using var outputs = _vaeDecoder.Session.Run(new[] { NamedOnnxValue.CreateFromTensor(_vaeDecoder.PrimaryInputName, tensor) });
            Log.Info($"[StreamDiffusion] V2 VaeDecoder: TensorRT engine ready after {stopwatch.ElapsedMilliseconds} ms " +
                     "(built during initialization, cached in trt_cache)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[StreamDiffusion] V2 VaeDecoder: TensorRT engine warm-up failed " +
                        $"({ex.Message.Split('\n')[0].Trim()})");
            return false;
        }
    }

    /// <summary>Appends a frame to the encoder window and returns the RMS change against the previous frame in [-1, 1] units.</summary>
    private float PushFrame(byte[] rgba, bool isBgra)
    {
        if (_frames[0].Length != _frameLength)
        {
            for (var index = 0; index < EncoderWindowFrames; index++)
                _frames[index] = new float[_frameLength];
        }

        var frame = _frames[_frameWriteIndex];
        var previous = _frameCount > 0 ? _frames[(_frameWriteIndex + EncoderWindowFrames - 1) % EncoderWindowFrames] : null;
        var plane = _width * _height;
        var redOffset = isBgra ? 2 : 0;
        var blueOffset = isBgra ? 0 : 2;
        for (var pixel = 0; pixel < plane; pixel++)
        {
            frame[pixel] = rgba[pixel * 4 + redOffset] / 255f;
            frame[plane + pixel] = rgba[pixel * 4 + 1] / 255f;
            frame[2 * plane + pixel] = rgba[pixel * 4 + blueOffset] / 255f;
        }

        var change = 0f;
        if (previous != null)
        {
            // A sparse sample is plenty to tell a cut or fast motion from a still scene
            var squared = 0.0;
            var samples = 0;
            for (var index = 0; index < _frameLength; index += 61)
            {
                var difference = (frame[index] - previous[index]) * 2f;
                squared += difference * difference;
                samples++;
            }

            change = (float)Math.Sqrt(squared / Math.Max(1, samples));
        }
        else
        {
            // Seed the whole window so the first encode sees a static scene instead of black frames
            for (var index = 0; index < EncoderWindowFrames; index++)
            {
                if (index != _frameWriteIndex)
                    Array.Copy(frame, _frames[index], _frameLength);
            }
        }

        _frameWriteIndex = (_frameWriteIndex + 1) % EncoderWindowFrames;
        _frameCount++;
        return change;
    }

    private float[] EncodeLatestFrame()
    {
        // After a push the write index points at the oldest frame, so reading from it yields oldest to newest
        if (_encoderInput.Length != EncoderWindowFrames * _frameLength)
            _encoderInput = new float[EncoderWindowFrames * _frameLength];

        var input = _encoderInput;
        for (var slot = 0; slot < EncoderWindowFrames; slot++)
        {
            var source = _frames[(_frameWriteIndex + slot) % EncoderWindowFrames];
            Array.Copy(source, 0, input, slot * _frameLength, _frameLength);
        }

        var tensor = new DenseTensor<float>(input, new[] { 1, EncoderWindowFrames, 3, _height, _width });
        using var outputs = _vaeEncoder!.Session.Run(new[] { NamedOnnxValue.CreateFromTensor(_vaeEncoder.PrimaryInputName, tensor) });

        // Only the newest latent is needed; copying the whole window output costs 12x the data at 512
        var latentTensor = outputs.First().AsTensor<float>();
        ReadOnlySpan<float> latents = latentTensor is DenseTensor<float> denseLatents
                                          ? denseLatents.Buffer.Span
                                          : latentTensor.ToArray();
        var clean = new float[_latentLength];
        latents.Slice(latents.Length - _latentLength, _latentLength).CopyTo(clean);
        return clean;
    }

    private byte[] DecodeToRgba(float[] latent, bool isDenoised)
    {
        int windowCount;
        if (isDenoised)
        {
            if (_decodeCount < _decodeRing.Length)
                _decodeCount++;

            for (var index = 0; index < _decodeRing.Length - 1; index++)
                _decodeRing[index] = _decodeRing[index + 1];

            _decodeRing[^1] = latent;
            windowCount = _decodeCount;
        }
        else
        {
            windowCount = 1;
        }

        var input = new float[windowCount * _latentLength];
        for (var index = 0; index < windowCount; index++)
        {
            var source = isDenoised ? _decodeRing[_decodeRing.Length - windowCount + index] : latent;
            Array.Copy(source, 0, input, index * _latentLength, _latentLength);
        }

        var tensor = new DenseTensor<float>(input, new[] { 1, windowCount, LatentChannels, _latentHeight, _latentWidth });
        using var outputs = _vaeDecoder!.Session.Run(new[] { NamedOnnxValue.CreateFromTensor(_vaeDecoder.PrimaryInputName, tensor) });

        // The decoder emits FramesPerLatent frames per latent, but only the newest frame is displayed. Copying
        // the full window (up to 38 MB at 512) every frame was both slow and a growing GC load.
        var frameTensor = outputs.First().AsTensor<float>();
        ReadOnlySpan<float> frames = frameTensor is DenseTensor<float> denseFrames
                                         ? denseFrames.Buffer.Span
                                         : frameTensor.ToArray();
        var plane = _width * _height;
        var lastFrameOffset = (windowCount * FramesPerLatent - 1) * 3 * plane;
        var rgba = new byte[plane * 4];
        for (var pixel = 0; pixel < plane; pixel++)
        {
            rgba[pixel * 4 + 0] = ToByte(frames[lastFrameOffset + pixel]);
            rgba[pixel * 4 + 1] = ToByte(frames[lastFrameOffset + plane + pixel]);
            rgba[pixel * 4 + 2] = ToByte(frames[lastFrameOffset + 2 * plane + pixel]);
            rgba[pixel * 4 + 3] = 255;
        }

        return rgba;
    }

    private static byte ToByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    /// <summary>
    /// The TensorRT EP reports an engine that could not be built (builder workspace OOM on a full card, or a
    /// shape its parser rejects) as "failed to create engine"; that is the one failure worth retrying on CUDA.
    /// </summary>
    private static bool IsTensorRtEngineBuildFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("failed to create engine", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Switches to a new prompt. The first prompt is encoded synchronously because the DiT cannot run without
    /// cross-attention K/V; later prompts are encoded on a background task while the stream keeps using the
    /// previous one, so a prompt change never stalls live playback. Encoded prompts are cached.
    /// </summary>
    private void EnsurePrompt(string prompt)
    {
        if (_promptEmbeddings.TryGetValue(prompt, out var cached))
        {
            CancelPendingPrompt();
            ApplyPrompt(prompt, cached);
            return;
        }

        if (_crossOutputs == null)
        {
            // The synchronous first prompt shares the background path's backoff: a failed 11 GB
            // text-encoder load must not be retried on every stream step while the failure stands.
            if (string.Equals(prompt, _failedPrompt, StringComparison.Ordinal)
                && Environment.TickCount64 < _promptRetryAfterTicks)
                return;

            CancelPendingPrompt();
            try
            {
                ApplyPrompt(prompt, LoadPromptEmbeddings(prompt));
            }
            catch (Exception)
            {
                _failedPrompt = prompt;
                _promptRetryAfterTicks = Environment.TickCount64 + 5_000L;
                throw;
            }

            return;
        }

        // A preparation for this prompt is already running; keep the current prompt until it arrives
        if (string.Equals(prompt, _pendingPrompt, StringComparison.Ordinal))
            return;

        // A prompt that already failed must not reload the 11 GB encoder every frame.
        if (string.Equals(prompt, _failedPrompt, StringComparison.Ordinal)
            && Environment.TickCount64 < _promptRetryAfterTicks)
            return;

        CancelPendingPrompt();
        _pendingPrompt = prompt;
        _promptPending = true;
        var generation = ++_promptGeneration;
        var modelDirectory = _modelDirectory!;
        var job = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _promptCts = job;
        _ = Task.Run(() => PreparePromptInBackground(prompt, modelDirectory, generation, job));
    }

    /// <summary>Discards an in-flight background encode so it cannot apply over a newer prompt.</summary>
    private void CancelPendingPrompt()
    {
        _promptGeneration++;
        _pendingPrompt = null;
        _promptPending = false;
        CancelPromptJob();
    }

    private void CancelPromptJob()
    {
        var job = Interlocked.Exchange(ref _promptCts, null);
        if (job == null)
            return;

        try
        {
            job.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job reached its own finally between the read and the cancel.
        }
    }

    private async Task PreparePromptInBackground(string prompt, string modelDirectory, int generation, CancellationTokenSource job)
    {
        var token = job.Token;
        var acquired = false;
        try
        {
            if (token.IsCancellationRequested)
                return;

            // A newer prompt cancels a job that is still waiting for the encoder; only one 11 GB load runs at a time.
            await _promptEncodeGate.WaitAsync(token).ConfigureAwait(false);
            acquired = true;

            if (token.IsCancellationRequested)
                return;

            var encodeStopwatch = System.Diagnostics.Stopwatch.StartNew();
            Log.Info($"[StreamDiffusion] V2 encoding prompt \"{prompt}\"");
            var embeddings = LoadPromptEmbeddings(prompt, modelDirectory, acquireGate: false, token);
            var encodeSeconds = encodeStopwatch.Elapsed.TotalSeconds;

            // Release the encode gate BEFORE taking the stream lock. StreamStep holds the stream lock and can wait
            // on this gate (the synchronous first-prompt path after a provider re-initialization), so holding the
            // gate while waiting for the lock would deadlock the worker and the job against each other.
            if (acquired)
            {
                acquired = false;
                _promptEncodeGate.Release();
            }

            // The stream lock serializes the swap with StreamStep; the expensive encode above runs without it
            lock (_lock)
            {
                if (_disposed || generation != _promptGeneration || _crossOutputs == null)
                    return;

                ApplyPrompt(prompt, embeddings);
                _pendingPrompt = null;
                _promptPending = false;
                _failedPrompt = null;
                _promptFailureCount = 0;
                Log.Info($"[StreamDiffusion] V2 prompt ready after {encodeSeconds:F1} s: \"{prompt}\"");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer prompt or by Dispose; the successor owns the state now.
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (generation == _promptGeneration)
                {
                    _pendingPrompt = null;
                    _promptPending = false;

                    // Back off instead of retrying the same failing 11 GB load every frame.
                    _failedPrompt = prompt;
                    _promptFailureCount++;
                    var delayMs = Math.Min(60_000L, 5_000L << Math.Min(4, _promptFailureCount - 1));
                    _promptRetryAfterTicks = Environment.TickCount64 + delayMs;
                }
            }

            if (!token.IsCancellationRequested)
                Log.Warning($"[StreamDiffusion] V2 prompt encode failed: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _promptEncodeGate.Release();

            if (ReferenceEquals(_promptCts, job))
                _promptCts = null;

            job.Dispose();
        }
    }

    private float[] LoadPromptEmbeddings(string prompt, string? modelDirectory = null, bool acquireGate = true, CancellationToken token = default)
    {
        if (_promptEmbeddings.TryGetValue(prompt, out var cached))
            return cached;

        // One 11 GB encoder at a time: a second prompt change must not load a second copy alongside the first
        if (acquireGate)
            _promptEncodeGate.Wait();

        try
        {
            token.ThrowIfCancellationRequested();

            if (_promptEmbeddings.TryGetValue(prompt, out cached))
                return cached;

            var directory = modelDirectory ?? _modelDirectory!;

            // A known prompt (a fixed set list, a revisited look) skips the 11 GB encoder load entirely
            var fromDisk = TryLoadCachedEmbeddings(prompt, directory);
            if (fromDisk != null)
            {
                RememberPrompt(prompt, fromDisk);
                return fromDisk;
            }

            var ids = _tokenizer!.Encode(prompt, TextLength);
            var length = Math.Min(TextLength, Math.Max(16, (ids.Length + 15) / 16 * 16));

            var idTensor = new DenseTensor<long>(new[] { 1, length });
            var maskTensor = new DenseTensor<long>(new[] { 1, length });
            for (var index = 0; index < ids.Length; index++)
            {
                idTensor[0, index] = ids[index];
                maskTensor[0, index] = 1;
            }

            token.ThrowIfCancellationRequested();
            var textEncoder = AcquireTextEncoder(directory);
            using var encoded = textEncoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("ids", idTensor),
                NamedOnnxValue.CreateFromTensor("mask", maskTensor),
            });
            var embeddings = encoded.First().AsTensor<float>().ToArray();

            // Rows beyond the prompt are zero in the reference, and the DiT still attends over all 512 rows
            var padded = new float[TextLength * TextFeatureCount];
            Array.Copy(embeddings, padded, Math.Min(embeddings.Length, ids.Length * TextFeatureCount));

            SaveCachedEmbeddings(prompt, padded, directory);
            RememberPrompt(prompt, padded);
            return padded;
        }
        finally
        {
            // Called with the encode gate held: a pipeline disposed mid-encode left its encoder for this point
            if (_disposed)
            {
                ReleaseTextEncoder();
            }
            else
            {
                ScheduleTextEncoderRelease();
            }

            if (acquireGate)
                _promptEncodeGate.Release();
        }
    }

    /** The 8-bit export when present, else the bf16 original. */
    private static string GetTextEncoderPath(string directory)
    {
        var quantized = Path.Combine(directory, QuantizedTextEncoderFileName);
        return File.Exists(quantized) ? quantized : Path.Combine(directory, "text_encoder.onnx");
    }

    /**
     * Returns the loaded encoder, loading it on first use. Must be called with the encode gate held. It stays loaded
     * for <see cref="TextEncoderKeepAlive"/> after the last encode, so editing a prompt does not pay the load each time.
     */
    private InferenceSession AcquireTextEncoder(string directory)
    {
        var path = GetTextEncoderPath(directory);
        if (_textEncoder != null && string.Equals(_textEncoderPath, path, StringComparison.OrdinalIgnoreCase))
            return _textEncoder;

        ReleaseTextEncoder();

        // The bf16 original must not be optimized: constant folding would expand its weights to fp32 (twice the memory).
        // The 8-bit export has no such casts left and needs the optimizer for its int8 kernels.
        var quantized = path.EndsWith(QuantizedTextEncoderFileName, StringComparison.OrdinalIgnoreCase);
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = quantized ? GraphOptimizationLevel.ORT_ENABLE_ALL : GraphOptimizationLevel.ORT_DISABLE_ALL,
            // Prompt changes encode in the background; leave cores for the render thread instead of saturating the CPU
            IntraOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2),
        };
        _textEncoder = new InferenceSession(path, options);
        _textEncoderPath = path;
        return _textEncoder;
    }

    private void ReleaseTextEncoder()
    {
        _textEncoder?.Dispose();
        _textEncoder = null;
        _textEncoderPath = null;
    }

    private void ScheduleTextEncoderRelease()
    {
        _textEncoderReleaseTimer ??= new Timer(_ => ReleaseIdleTextEncoder(), null, Timeout.Infinite, Timeout.Infinite);
        _textEncoderReleaseTimer.Change(TextEncoderKeepAlive, Timeout.InfiniteTimeSpan);
    }

    private void ReleaseIdleTextEncoder()
    {
        try
        {
            // An encode in progress keeps it; that encode reschedules the release when it finishes
            if (!_promptEncodeGate.Wait(0))
                return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            ReleaseTextEncoder();
        }
        finally
        {
            _promptEncodeGate.Release();
        }
    }

    /// <summary>Keeps the most recent embeddings in memory with a small LRU-ish cap.</summary>
    private void RememberPrompt(string prompt, float[] embeddings)
    {
        _promptEmbeddings[prompt] = embeddings;
        _promptCacheOrder.Enqueue(prompt);
        while (_promptCacheOrder.Count > MaxCachedPrompts && _promptCacheOrder.TryDequeue(out var expired))
            _promptEmbeddings.TryRemove(expired, out _);
    }

    /// <summary>
    /// Prompt embeddings survive restarts: a fixed live set can start streaming without loading the 11 GB text
    /// encoder at all. Files are keyed by a hash of the prompt and the encoder file, and only hold the embeddings.
    /// </summary>
    private static string PromptCacheDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
                root = Path.GetTempPath();
            return Path.Combine(root, "TiXL", "sdv2_prompt_cache");
        }
    }

    private static string GetPromptCachePath(string prompt, string modelDirectory)
    {
        var stamp = string.Empty;
        try
        {
            var info = new FileInfo(GetTextEncoderPath(modelDirectory));
            if (info.Exists)
                stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            // No stamp: the hash still keeps different prompts apart
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{PromptCacheVersion}|{stamp}|{prompt}")));
        return Path.Combine(PromptCacheDirectory, key + ".bin");
    }

    private static float[]? TryLoadCachedEmbeddings(string prompt, string modelDirectory)
    {
        try
        {
            var path = GetPromptCachePath(prompt, modelDirectory);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != PromptCacheHeaderBytes + TextLength * TextFeatureCount * 4)
                return null;

            if (BitConverter.ToInt32(bytes, 0) != PromptCacheVersion
                || BitConverter.ToInt32(bytes, 4) != TextLength
                || BitConverter.ToInt32(bytes, 8) != TextFeatureCount)
                return null;

            var embeddings = new float[TextLength * TextFeatureCount];
            System.Buffer.BlockCopy(bytes, PromptCacheHeaderBytes, embeddings, 0, embeddings.Length * 4);
            return embeddings;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCachedEmbeddings(string prompt, float[] embeddings, string modelDirectory)
    {
        try
        {
            var directory = PromptCacheDirectory;
            Directory.CreateDirectory(directory);
            var path = GetPromptCachePath(prompt, modelDirectory);

            var bytes = new byte[PromptCacheHeaderBytes + embeddings.Length * 4];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), PromptCacheVersion);
            BitConverter.TryWriteBytes(bytes.AsSpan(4), TextLength);
            BitConverter.TryWriteBytes(bytes.AsSpan(8), TextFeatureCount);
            System.Buffer.BlockCopy(embeddings, 0, bytes, PromptCacheHeaderBytes, embeddings.Length * 4);

            var temporary = $"{path}.{Environment.ProcessId}.tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);

            var files = new DirectoryInfo(directory).GetFiles("*.bin");
            if (files.Length > MaxDiskCachedPrompts)
            {
                foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxDiskCachedPrompts))
                    file.Delete();
            }

            foreach (var file in new DirectoryInfo(directory).GetFiles("*.tmp"))
            {
                if (DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromHours(1))
                    file.Delete();
            }
        }
        catch
        {
            // A cache failure must never break the stream
        }
    }

    /// <summary>Uploads the prompt embeddings and replaces the cross-attention K/V. Must be called while holding <see cref="_lock"/>.</summary>
    private void ApplyPrompt(string prompt, float[] padded)
    {
        using var embeddingValue = OrtValue.CreateTensorValueFromMemory(padded, new long[] { 1, TextLength, TextFeatureCount });
        using var binding = _crossKv!.CreateIoBinder();
        binding.BindInput("prompt_embeds", embeddingValue);
        binding.BindOutputToDevice("cross_k", _device!);
        binding.BindOutputToDevice("cross_v", _device!);
        _crossKv.RunBound(binding);
        binding.SynchronizeBoundOutputs();

        var previous = _crossOutputs;
        _crossOutputs = binding.GetOutputValues();
        previous?.Dispose();
        _preparedPrompt = prompt;
        _promptVersion++;
    }

    private void Prime(int seed, float initialNoiseScale)
    {
        DisposeStreamState();

        _batch = Math.Clamp(_stepCount, 1, DenoisingTimesteps.Length);
        _random = seed >= 0 ? new Random(seed) : new Random();
        _hidden = new float[_batch][];
        _caches = new KvCacheState[_batch];
        for (var index = 0; index < _batch; index++)
        {
            _hidden[index] = new float[_latentLength];
            _caches[index] = new KvCacheState(_cacheFrames);
        }

        _decodeRing = new float[_decoderWindowLatents][];
        _decodeCount = 0;

        var shape = new long[] { _layers, _batch, _cacheFrames * _tokensPerFrame, _heads, _headDim };
        var keys = AllocateZeroedCache(shape);
        var values = AllocateZeroedCache(shape);
        _cacheOwners.Add(keys);
        _cacheOwners.Add(values);
        _cacheKeys = keys[0];
        _cacheValues = values[0];

        float[] clean;
        try
        {
            clean = EncodeLatestFrame();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"VaeEncoder: {ex.Message}", ex);
        }

        var startTimestep = (int)(1000f * initialNoiseScale) - 100;
        var noise = new float[_latentLength];
        var current = new float[_latentLength];

        // The first frames are denoised alone with every batch element running the same step, so the caches end up identical
        // across the batch before pipelined streaming starts
        for (var frame = 0; frame < FirstStreamFrame; frame++)
        {
            FillGaussian(noise);
            for (var index = 0; index < _latentLength; index++)
                current[index] = noise[index] * initialNoiseScale + clean[index] * (1f - initialNoiseScale);

            var slot = KvSlotPolicy.SlotForFrame(frame, _cacheFrames, _sinkFrames);
            for (var step = 0; step < _batch; step++)
            {
                var timestep = step == 0 ? startTimestep : DenoisingTimesteps[step];
                var timesteps = new float[_batch];
                var sigmas = new float[_batch];
                var framePositions = new long[_batch];
                var slots = new long[_batch];
                var latents = new float[_batch * _latentLength];
                for (var element = 0; element < _batch; element++)
                {
                    timesteps[element] = timestep;
                    sigmas[element] = _schedule.SigmaAt(timestep);
                    framePositions[element] = frame;
                    slots[element] = slot;
                    Array.Copy(current, 0, latents, element * _latentLength, _latentLength);
                }

                float[] prediction;
                try
                {
                    prediction = RunDit(latents, timesteps, sigmas, framePositions, slots);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Dit: {ex.Message}", ex);
                }

                for (var element = 0; element < _batch; element++)
                    _caches[element].MarkWritten(slot, frame);

                if (step < _batch - 1)
                {
                    FillGaussian(noise);
                    _schedule.AddNoise(prediction.AsSpan(0, _latentLength), noise, DenoisingTimesteps[step + 1], current);
                }
            }
        }

        _newestFrame = FirstStreamFrame - 1;
        _noiseScale = initialNoiseScale;
        _needsPrime = false;
    }

    /// <summary>Runs one pipelined step. Returns the finished latent of the oldest batch element, or null while the pipeline fills.</summary>
    private float[]? Step(float[] clean, float noiseScale, int newestTimestep)
    {
        var noise = new float[_latentLength];
        FillGaussian(noise);
        var newest = _hidden[0];
        for (var index = 0; index < _latentLength; index++)
            newest[index] = noise[index] * noiseScale + clean[index] * (1f - noiseScale);

        _newestFrame++;

        var timesteps = new float[_batch];
        var sigmas = new float[_batch];
        var framePositions = new long[_batch];
        var slots = new long[_batch];
        var latents = new float[_batch * _latentLength];
        var active = new bool[_batch];
        for (var element = 0; element < _batch; element++)
        {
            var timestep = element == 0 ? newestTimestep : DenoisingTimesteps[element];
            var frame = _newestFrame - element;
            active[element] = frame >= FirstStreamFrame;

            timesteps[element] = timestep;
            sigmas[element] = _schedule.SigmaAt(timestep);
            framePositions[element] = Math.Max(frame, 0);

            // Idle elements write into an empty slot so they cannot disturb the primed frames
            slots[element] = active[element]
                                 ? KvSlotPolicy.SlotForFrame(frame, _cacheFrames, _sinkFrames)
                                 : Math.Max(_caches[element].FindEmptySlot(), 0);
            Array.Copy(_hidden[element], 0, latents, element * _latentLength, _latentLength);
        }

        var prediction = RunDit(latents, timesteps, sigmas, framePositions, slots);

        for (var element = 0; element < _batch; element++)
        {
            if (active[element])
                _caches[element].MarkWritten((int)slots[element], _newestFrame - element);
        }

        float[]? finished = null;
        var lastElement = _batch - 1;
        if (active[lastElement])
        {
            finished = new float[_latentLength];
            Array.Copy(prediction, lastElement * _latentLength, finished, 0, _latentLength);
        }

        for (var element = lastElement - 1; element >= 0; element--)
        {
            FillGaussian(noise);
            _schedule.AddNoise(prediction.AsSpan(element * _latentLength, _latentLength), noise,
                               DenoisingTimesteps[element + 1], _hidden[element + 1]);
        }

        return finished;
    }

    /// <summary>Runs the DiT for the whole batch, swaps in the updated caches and returns the denoised latents.</summary>
    private float[] RunDit(float[] latents, float[] timesteps, float[] sigmas, long[] framePositions, long[] slots)
    {
        var cacheTokens = _cacheFrames * _tokensPerFrame;
        var mask = new float[_batch * cacheTokens];
        for (var element = 0; element < _batch; element++)
            _caches[element].FillMask(mask.AsSpan(element * cacheTokens, cacheTokens), _tokensPerFrame, (int)slots[element]);

        var batchShape = new long[] { _batch };
        using var latentValue = OrtValue.CreateTensorValueFromMemory(latents, new long[] { _batch, LatentChannels, _latentHeight, _latentWidth });
        using var timestepValue = OrtValue.CreateTensorValueFromMemory(timesteps, batchShape);
        using var sigmaValue = OrtValue.CreateTensorValueFromMemory(sigmas, batchShape);
        using var positionValue = OrtValue.CreateTensorValueFromMemory(framePositions, batchShape);
        using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, new long[] { _batch, cacheTokens });
        using var slotValue = OrtValue.CreateTensorValueFromMemory(slots, batchShape);

        using var binding = _dit!.CreateIoBinder();
        binding.BindInput("latent", latentValue);
        binding.BindInput("timestep", timestepValue);
        binding.BindInput("sigma", sigmaValue);
        binding.BindInput("frame_pos", positionValue);
        binding.BindInput("k_cache", _cacheKeys!);
        binding.BindInput("v_cache", _cacheValues!);
        binding.BindInput("cache_mask", maskValue);
        binding.BindInput("slot", slotValue);
        binding.BindInput("cross_k", _crossOutputs![0]);
        binding.BindInput("cross_v", _crossOutputs![1]);
        binding.BindOutputToDevice("x0", OrtMemoryInfo.DefaultInstance);
        binding.BindOutputToDevice("k_cache_out", _device!);
        binding.BindOutputToDevice("v_cache_out", _device!);
        _dit.RunBound(binding);
        binding.SynchronizeBoundOutputs();

        var outputs = binding.GetOutputValues();
        var prediction = outputs[0].GetTensorDataAsSpan<float>().ToArray();

        // The cache outputs stay on the device and become the next call's inputs; everything older can go
        foreach (var owner in _cacheOwners)
            owner.Dispose();

        _cacheOwners.Clear();
        _cacheOwners.Add(outputs);
        _cacheKeys = outputs[1];
        _cacheValues = outputs[2];
        return prediction;
    }

    private IDisposableReadOnlyCollection<OrtValue> AllocateZeroedCache(long[] shape)
    {
        using var shapeValue = OrtValue.CreateTensorValueFromMemory(shape, new long[] { shape.Length });
        using var binding = _kvZeros!.CreateIoBinder();
        binding.BindInput("shape", shapeValue);
        binding.BindOutputToDevice("zeros", _device!);
        _kvZeros.RunBound(binding);
        binding.SynchronizeBoundOutputs();
        return binding.GetOutputValues();
    }

    private void FillGaussian(float[] destination)
    {
        for (var index = 0; index < destination.Length; index += 2)
        {
            var u1 = 1.0 - _random.NextDouble();
            var u2 = _random.NextDouble();
            var radius = Math.Sqrt(-2.0 * Math.Log(u1));
            destination[index] = (float)(radius * Math.Cos(2.0 * Math.PI * u2));
            if (index + 1 < destination.Length)
                destination[index + 1] = (float)(radius * Math.Sin(2.0 * Math.PI * u2));
        }
    }

    private void DisposeStreamState()
    {
        foreach (var owner in _cacheOwners)
            owner.Dispose();

        _cacheOwners.Clear();
        _cacheKeys = null;
        _cacheValues = null;
        _needsPrime = true;
    }

    private void DisposeSessions()
    {
        _crossOutputs?.Dispose();
        _crossOutputs = null;
        _dit?.Dispose();
        _crossKv?.Dispose();
        _kvZeros?.Dispose();
        _vaeEncoder?.Dispose();
        _vaeDecoder?.Dispose();
        _dit = null;
        _crossKv = null;
        _kvZeros = null;
        _vaeEncoder = null;
        _vaeDecoder = null;
        _preparedPrompt = null;

        // The CUDA memory info owns a native handle; DefaultInstance is process-wide and must stay alive
        if (!ReferenceEquals(_device, OrtMemoryInfo.DefaultInstance))
            (_device as IDisposable)?.Dispose();

        _device = null;
    }

    private readonly object _lock = new();
    private volatile bool _verboseLogging;
    private volatile bool _disposed;
    private volatile bool _promptPending;
    private volatile int _promptVersion;
    private const int MaxCachedPrompts = 4;
    private const int MaxDiskCachedPrompts = 32;
    private const int PromptCacheVersion = 1;
    private const int PromptCacheHeaderBytes = 16;
    private readonly ConcurrentDictionary<string, float[]> _promptEmbeddings = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _promptCacheOrder = new();
    private readonly SemaphoreSlim _promptEncodeGate = new(1, 1);
    private const string QuantizedTextEncoderFileName = "text_encoder_q8.onnx";
    private static readonly TimeSpan TextEncoderKeepAlive = TimeSpan.FromMinutes(2);
    private InferenceSession? _textEncoder;
    private string? _textEncoderPath;
    private Timer? _textEncoderReleaseTimer;
    private CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _promptCts;
    private string? _pendingPrompt;
    private string? _failedPrompt;
    private int _promptFailureCount;
    private long _promptRetryAfterTicks;
    private int _promptGeneration;
    private readonly FlowMatchSchedule _schedule = new();
    private readonly float[][] _frames = { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(),
                                           Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>() };
    private readonly List<IDisposableReadOnlyCollection<OrtValue>> _cacheOwners = new();

    private OnnxModelSession? _dit;
    private OnnxModelSession? _crossKv;
    private OnnxModelSession? _kvZeros;
    private OnnxModelSession? _vaeEncoder;
    private OnnxModelSession? _vaeDecoder;
    private Umt5Tokenizer? _tokenizer;
    private OrtMemoryInfo? _device;
    private string? _modelDirectory;
    private int _deviceId;
    private ExecutionProvider _provider = ExecutionProvider.Cuda;
    private bool _fallbackAttempted;

    private int _latentHeight;
    private int _latentWidth;
    private int _tokensPerFrame;
    private int _cacheFrames;
    private int _sinkFrames;
    private int _layers;
    private int _heads;
    private int _headDim;
    private int _width;
    private int _height;
    private int _latentLength;
    private int _frameLength;

    private int _stepCount = DenoisingTimesteps.Length;
    private int _batch;
    private bool _needsPrime = true;
    private int _lastSeed = int.MinValue;
    private int _newestFrame;
    private float _noiseScale = MotionNoiseController.DefaultNoiseScale;
    private Random _random = new();

    private float[] _encoderInput = Array.Empty<float>();
    private int _frameWriteIndex;
    private int _frameCount;
    private float[][] _hidden = Array.Empty<float[]>();
    private KvCacheState[] _caches = Array.Empty<KvCacheState>();
    private float[][] _decodeRing = new float[DefaultDecoderWindowLatents][];
    private int _decoderWindowLatents = DefaultDecoderWindowLatents;
    private bool _fastDecodeActive;
    private int _decodeCount;

    private string? _preparedPrompt;
    private string? _initializationError;
    private IDisposableReadOnlyCollection<OrtValue>? _crossOutputs;
    private OrtValue? _cacheKeys;
    private OrtValue? _cacheValues;
}
