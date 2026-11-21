using t3.streamdiffusion.Onnx;
using t3.streamdiffusion.V2;
using Xunit;

namespace StreamDiffusion.Tests;

/// <summary>
/// End-to-end run of the exported StreamDiffusion V2 models. Passes silently when the export folder
/// (TIXL_SDV2_MODELS) is absent, so it stays safe for CI.
/// </summary>
public class StreamDiffusionV2IntegrationTests
{
    private static readonly string ModelDir =
        Environment.GetEnvironmentVariable("TIXL_SDV2_MODELS") ?? @"D:\DEVDEVDEV\models\streamdiffusionv2\v2_256";

    /// <summary>TIXL_SDV2_PROVIDER=cpu|directml|cuda|tensorrt; TIXL_SDV2_CPU=1 is kept as a shorthand for cpu.</summary>
    private static ExecutionProvider Provider
    {
        get
        {
            if (Environment.GetEnvironmentVariable("TIXL_SDV2_CPU") == "1")
                return ExecutionProvider.Cpu;

            return (Environment.GetEnvironmentVariable("TIXL_SDV2_PROVIDER") ?? "cuda").ToLowerInvariant() switch
            {
                "cpu" => ExecutionProvider.Cpu,
                "directml" or "dml" => ExecutionProvider.DirectMl,
                "tensorrt" or "trt" => ExecutionProvider.TensorRt,
                _ => ExecutionProvider.Cuda,
            };
        }
    }

    private static bool Stress => Environment.GetEnvironmentVariable("TIXL_SDV2_STRESS") == "1";

    /// <summary>
    /// The provider chain falls back honestly when a GPU provider cannot initialize, so the resulting label is
    /// not environment-independent. Streaming machines pin the expected provider via TIXL_SDV2_REQUIRE_PROVIDER=1;
    /// a default run only reports what the models landed on.
    /// </summary>
    private static void AssertProvider(StreamDiffusionV2Pipeline pipeline)
    {
        if (Environment.GetEnvironmentVariable("TIXL_SDV2_REQUIRE_PROVIDER") == "1")
        {
            var expected = Provider switch
            {
                ExecutionProvider.Cuda => "CUDA",
                ExecutionProvider.TensorRt => "TensorRT",
                ExecutionProvider.DirectMl => "DirectML",
                _ => "CPU",
            };
            Assert.Equal(expected, pipeline.ProviderLabel);
        }
        else
        {
            Console.WriteLine($"V2 provider: {pipeline.ProviderLabel}");
        }
    }

    [Fact]
    public void DirectMlIsRejectedWithAReason()
    {
        if (Provider != ExecutionProvider.DirectMl || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        using var pipeline = new StreamDiffusionV2Pipeline();
        Assert.False(pipeline.Initialize(ModelDir, 0, ExecutionProvider.DirectMl));
        Assert.Contains("DirectML", pipeline.InitializationError);
    }

    [Fact]
    public void StreamsFramesThroughTheWholePipeline()
    {
        if (Provider == ExecutionProvider.DirectMl || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        var provider = Provider;
        var writer = new T3.Core.Logging.ConsoleWriter();
        T3.Core.Logging.Log.AddWriter(writer);
        using var pipeline = new StreamDiffusionV2Pipeline();
        Assert.True(pipeline.Initialize(ModelDir, 0, provider));
        AssertProvider(pipeline);
        Assert.True(pipeline.TryGetFixedOutputSize(out var width, out var height));
        pipeline.ConfigureStream(int.TryParse(Environment.GetEnvironmentVariable("TIXL_SDV2_STEPS"), out var steps) ? steps : 2);

        // FastDecode decodes one latent instead of a three-latent window; the operator's FastDecode input sets
        // the same flag. Env-gated so the check can measure both decode windows on one machine.
        pipeline.FastDecodeEnabled = Environment.GetEnvironmentVariable("TIXL_SDV2_FASTDECODE") == "1";
        var frameCount = int.TryParse(Environment.GetEnvironmentVariable("TIXL_SDV2_FRAMES"), out var frames) ? frames : 8;
        Console.WriteLine($"V2 models {ModelDir}, {width}x{height}, {steps} steps, {frameCount} frames, fastDecode={pipeline.FastDecodeEnabled}");

        byte[]? last = null;
        var sawFallback = false;
        var successfulFrames = 0;
        var successfulAfterFallback = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var input = MakeFrame(width, height, frame);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.StreamStep("a watercolor painting of a fox in a forest", input, width, height,
                                             0.8f, 0, 1234, false, out var error);
            if (result != null)
                last = result;
            Console.WriteLine($"V2 frame {frame}: {stopwatch.ElapsedMilliseconds} ms (encode {pipeline.LastEncodeMs:F0}, denoise {pipeline.LastDenoiseMs:F0}, decode {pipeline.LastDecodeMs:F0})");

            // A TensorRT engine build can fail on a full card; the pipeline then reports it once and continues
            // on CUDA. That frame has no output by design, so only later frames are held to the strict check.
            if (error != null && error.Contains("switched to CUDA", StringComparison.Ordinal))
            {
                sawFallback = true;
                Console.WriteLine($"V2 provider fallback: {error}");
                continue;
            }

            Assert.True(error == null, error ?? string.Empty);
            Assert.NotNull(result);
            Assert.Equal(width * height * 4, result!.Length);
            successfulFrames++;
            if (sawFallback)
                successfulAfterFallback++;
        }

        // A provider fallback must still leave a working stream behind it.
        Assert.True(successfulFrames > 0, "no frame completed");
        Assert.NotNull(last);
        Assert.True(pipeline.IsInitialized, "the pipeline was left uninitialized");
        Assert.True(last!.Length == width * height * 4, "the last frame has the wrong size");
        if (sawFallback)
        {
            Assert.Equal("CUDA", pipeline.ProviderLabel);
            Assert.True(successfulAfterFallback > 0, "no frame completed after the TensorRT fallback");
        }

        var outputPath = Environment.GetEnvironmentVariable("TIXL_SDV2_OUT");
        if (!string.IsNullOrEmpty(outputPath))
            File.WriteAllBytes(outputPath, last!);

        var min = 255;
        var max = 0;
        for (var index = 0; index < last!.Length; index += 4)
        {
            min = Math.Min(min, last[index]);
            max = Math.Max(max, last[index]);
        }

        Assert.True(max - min > 8, $"Output is flat (range {min}-{max})");
    }

    /// <summary>Concurrent callers must be serialized by the pipeline lock, not corrupt the stream state.</summary>
    [Fact]
    public void ConcurrentStreamStepsAreSerialized()
    {
        if (!Stress || Provider == ExecutionProvider.DirectMl
            || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        using var pipeline = new StreamDiffusionV2Pipeline();
        Assert.True(pipeline.Initialize(ModelDir, 0, Provider));
        Assert.True(pipeline.TryGetFixedOutputSize(out var width, out var height));
        pipeline.ConfigureStream(1);

        var input = MakeFrame(width, height, 0);
        var errors = new System.Collections.Concurrent.ConcurrentBag<string?>();
        var lengths = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 4, _ =>
        {
            var result = pipeline.StreamStep("a watercolor painting of a fox in a forest", input, width, height,
                                             0.8f, 0, 1234, false, out var error);
            errors.Add(error);
            if (result != null)
                lengths.Add(result.Length);
        });

        Assert.All(errors, error => Assert.Null(error));
        Assert.Equal(4, lengths.Count);
        Assert.All(lengths, length => Assert.Equal(width * height * 4, length));
    }

    /// <summary>Disposing and rebuilding the sessions must release them; a leak shows up as an initialization failure.</summary>
    [Fact]
    public void ReinitializingReleasesSessions()
    {
        if (!Stress || Provider == ExecutionProvider.DirectMl
            || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        for (var iteration = 0; iteration < 3; iteration++)
        {
            using var pipeline = new StreamDiffusionV2Pipeline();
            Assert.True(pipeline.Initialize(ModelDir, 0, Provider), $"initialization failed on iteration {iteration}");
            AssertProvider(pipeline);
            Assert.True(pipeline.TryGetFixedOutputSize(out var width, out var height));
            pipeline.ConfigureStream(1);

            var frame = MakeFrame(width, height, iteration);
            var result = pipeline.StreamStep("a watercolor painting of a fox in a forest", frame, width, height,
                                             0.8f, 0, 1234, false, out var error);
            Assert.Null(error);
            Assert.NotNull(result);
            Console.WriteLine($"Reinit {iteration}: {pipeline.ProviderLabel}, {Environment.WorkingSet / (1024 * 1024)} MB managed working set");
        }
    }

    /// <summary>Long stream soak. Watch the GPU memory from outside; the assertion only catches managed growth.</summary>
    [Fact]
    public void RepeatedStreamStepsStayStable()
    {
        if (!Stress || Provider == ExecutionProvider.DirectMl
            || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        using var pipeline = new StreamDiffusionV2Pipeline();
        Assert.True(pipeline.Initialize(ModelDir, 0, Provider));
        Assert.True(pipeline.TryGetFixedOutputSize(out var width, out var height));
        pipeline.ConfigureStream(1);

        var frameCount = int.TryParse(Environment.GetEnvironmentVariable("TIXL_SDV2_SOAK_FRAMES"), out var frames) ? frames : 30;
        var startWorkingSet = Environment.WorkingSet;
        var startManaged = GC.GetTotalMemory(forceFullCollection: true);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var input = MakeFrame(width, height, frame);
            var result = pipeline.StreamStep("a watercolor painting of a fox in a forest", input, width, height,
                                             0.8f, 0, 1234, false, out var error);
            Assert.Null(error);
            Assert.NotNull(result);

            if (frame % 10 == 9)
                Console.WriteLine($"Soak {frame + 1}/{frameCount}: {Environment.WorkingSet / (1024 * 1024)} MB working set, " +
                                  $"{GC.GetTotalMemory(false) / (1024 * 1024)} MB managed");
        }

        var endManaged = GC.GetTotalMemory(forceFullCollection: true);
        var growth = endManaged - startManaged;
        Console.WriteLine($"Soak managed growth: {growth / (1024 * 1024)} MB over {frameCount} frames " +
                          $"({startWorkingSet / (1024 * 1024)} MB -> {Environment.WorkingSet / (1024 * 1024)} MB working set)");
        Assert.True(growth < 64L * 1024 * 1024, $"Managed heap grew by {growth / (1024 * 1024)} MB");
    }

    /// <summary>
    /// A prompt change must not block the running stream: the old prompt keeps producing frames while the new
    /// embeddings encode in the background, then the swap lands. The first prompt is disk-cached, so this test
    /// stays fast across runs.
    /// </summary>
    [Fact]
    public void PromptChangeDoesNotBlockTheStream()
    {
        if (!Stress || Provider == ExecutionProvider.DirectMl
            || StreamDiffusionV2Pipeline.ValidateModelDirectory(ModelDir).Length > 0)
            return;

        using var pipeline = new StreamDiffusionV2Pipeline();
        Assert.True(pipeline.Initialize(ModelDir, 0, Provider));
        Assert.True(pipeline.TryGetFixedOutputSize(out var width, out var height));
        pipeline.ConfigureStream(1);

        var frame = MakeFrame(width, height, 0);
        const string firstPrompt = "a watercolor painting of a fox in a forest";
        const string secondPrompt = "a neon cyberpunk city at night";
        var firstFrame = pipeline.StreamStep(firstPrompt, frame, width, height, 0.8f, 0, 1234, false, out var error);
        Assert.NotNull(firstFrame);
        Assert.Null(error);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var duringEncode = pipeline.StreamStep(secondPrompt, frame, width, height, 0.8f, 0, 1234, false, out error);
        stopwatch.Stop();
        Assert.Null(error);
        Assert.NotNull(duringEncode);
        Assert.True(stopwatch.ElapsedMilliseconds < 15000,
                    $"a prompt change blocked the stream for {stopwatch.ElapsedMilliseconds} ms");

        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (pipeline.IsPromptPending && DateTime.UtcNow < deadline)
            System.Threading.Thread.Sleep(250);

        Assert.False(pipeline.IsPromptPending, "background prompt encoding did not finish");
        var afterSwap = pipeline.StreamStep(secondPrompt, frame, width, height, 0.8f, 0, 1234, false, out error);
        Assert.Null(error);
        Assert.NotNull(afterSwap);
        Console.WriteLine($"Prompt swap: {stopwatch.ElapsedMilliseconds} ms to keep streaming");
    }

    private static byte[] MakeFrame(int width, int height, int frame)
    {
        var rgba = new byte[width * height * 4];
        var squareX = 20 + frame * 6;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var inside = x >= squareX && x < squareX + 64 && y >= height / 3 && y < height / 3 + 64;
                rgba[offset + 0] = inside ? (byte)230 : (byte)(x * 255 / width);
                rgba[offset + 1] = inside ? (byte)60 : (byte)(y * 255 / height);
                rgba[offset + 2] = inside ? (byte)40 : (byte)128;
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
    }
}
