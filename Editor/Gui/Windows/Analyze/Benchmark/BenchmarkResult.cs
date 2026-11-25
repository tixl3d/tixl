#nullable enable
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SDL;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.IO;
using T3.Core.Settings;
using T3.Editor.App;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Windows.Analyze.Benchmark;

/// <summary>One run of the suite on one machine, saved as JSON so runs from other machines can be compared.</summary>
internal sealed class BenchmarkRun
{
    public int FormatVersion = 1;
    public int SuiteVersion = BenchmarkSuite.Version;
    public string Label = string.Empty;

    /** Free text from whoever started the run, e.g. what is uncommitted on top of the commit. */
    public string Note = string.Empty;

    public DateTime StartedUtc;
    public BenchmarkSystemInfo System = new();
    public List<BenchmarkTestResult> Tests = [];

    [JsonIgnore]
    public string FilePath = string.Empty;

    public bool TryGetTest(string key, out BenchmarkTestResult result)
    {
        foreach (var test in Tests)
        {
            if (test.Key != key)
                continue;

            result = test;
            return true;
        }

        result = null!;
        return false;
    }

    public static string FolderPath => Path.Combine(FileLocations.SettingsDirectory, "Benchmarks");

    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var fileName = $"{StartedUtc.ToLocalTime():yyyyMMdd-HHmmss}-{Label}.json";
            FilePath = Path.Combine(FolderPath, fileName);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Saving the benchmark result failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Newest first. Files that can't be read are skipped with a warning.</summary>
    public static List<BenchmarkRun> LoadAll()
    {
        var runs = new List<BenchmarkRun>();
        if (!Directory.Exists(FolderPath))
            return runs;

        foreach (var path in Directory.GetFiles(FolderPath, "*.json"))
        {
            try
            {
                var run = JsonConvert.DeserializeObject<BenchmarkRun>(File.ReadAllText(path));
                if (run == null)
                    continue;

                run.FilePath = path;
                runs.Add(run);
            }
            catch (Exception e)
            {
                Log.Warning($"Skipping benchmark result {Path.GetFileName(path)}: {e.Message}");
            }
        }

        runs.Sort((a, b) => b.StartedUtc.CompareTo(a.StartedUtc));
        return runs;
    }

    /// <summary>A short name for legends: label and date.</summary>
    public string DisplayName => $"{Label} {StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}

internal sealed class BenchmarkSystemInfo
{
    public string Adapter = string.Empty;
    public string Driver = string.Empty;
    public string Backend = string.Empty;
    public string Os = string.Empty;
    public string TixlVersion = string.Empty;

    /** The git commit the editor was built from (short hash); uncommitted changes are not visible here. */
    public string Commit = string.Empty;

    public string BuildConfiguration = string.Empty;

    /** True when the Vulkan validation layer or the D3D11 debug layer was on; both add CPU cost to every call. */
    public bool ValidationLayer;

    /** "AC", "Battery" or "Unknown". Laptops on battery usually run slower. */
    public string PowerSource = string.Empty;

    [JsonIgnore]
    public bool IsDebugBuild => BuildConfiguration.Equals("Debug", StringComparison.OrdinalIgnoreCase);

    public static BenchmarkSystemInfo Capture()
    {
        var backend = ResourceManager.Device.Backend;
        var usesVulkan = ProgramWindows.UseVulkanBackend;
        return new BenchmarkSystemInfo
                   {
                       Adapter = backend.AdapterName,
                       Driver = backend.DriverDescription,
                       Backend = backend.BackendDescription,
                       Os = $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}",
                       TixlVersion = Program.VersionText,
                       Commit = ReadCommit(),
                       BuildConfiguration = typeof(BenchmarkSystemInfo).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                                            ?? "Unknown",
                       ValidationLayer = usesVulkan
                                             ? Environment.GetEnvironmentVariable("TIXL_VULKAN_VALIDATION") == "1"
                                             : CoreSettings.Config.EnableDirectXDebug,
                       PowerSource = ReadPowerSource(),
                   };
    }

    private static string ReadCommit()
    {
        var version = typeof(BenchmarkSystemInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plusIndex = version?.IndexOf('+') ?? -1;
        if (version == null || plusIndex < 0)
            return string.Empty;

        var hash = version[(plusIndex + 1)..];
        return hash.Length > 10 ? hash[..10] : hash;
    }

    private static unsafe string ReadPowerSource()
    {
        return SDL3.SDL_GetPowerInfo(null, null) switch
                   {
                       SDL_PowerState.SDL_POWERSTATE_ON_BATTERY => "Battery",
                       SDL_PowerState.SDL_POWERSTATE_NO_BATTERY
                           or SDL_PowerState.SDL_POWERSTATE_CHARGING
                           or SDL_PowerState.SDL_POWERSTATE_CHARGED => "AC",
                       _ => "Unknown",
                   };
    }
}

internal sealed class BenchmarkTestResult
{
    public string Key = string.Empty;

    /** Hash of the test's composition and every operator it uses; see <see cref="BenchmarkFingerprint"/>. */
    public string Fingerprint = string.Empty;

    public string StopReason = string.Empty;
    public List<BenchmarkStep> Steps = [];

    /// <summary>
    /// The plotted curve: per load, the mean of the passes, and the lowest and highest pass. Sorted by load.
    /// </summary>
    public void GetCurve(bool useCpuTime, List<CurvePoint> points)
    {
        points.Clear();
        foreach (var step in Steps)
        {
            var value = useCpuTime ? step.CpuMs : step.StepMs;
            var index = -1;
            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                if (points[pointIndex].Load != step.Load)
                    continue;

                index = pointIndex;
                break;
            }

            if (index < 0)
            {
                points.Add(new CurvePoint(step.Load, value, value, value, 1));
                continue;
            }

            var p = points[index];
            var count = p.PassCount + 1;
            points[index] = new CurvePoint(p.Load,
                                           (p.MeanMs * p.PassCount + value) / count,
                                           Math.Min(p.MinMs, value),
                                           Math.Max(p.MaxMs, value),
                                           count);
        }

        points.Sort((a, b) => a.Load.CompareTo(b.Load));
    }

    /// <summary>The duration at <paramref name="load"/>, interpolated between steps on log-log axes.</summary>
    public static bool TryInterpolate(List<CurvePoint> points, int load, out double ms)
    {
        ms = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.Load == load)
            {
                ms = point.MeanMs;
                return true;
            }

            if (point.Load < load || index == 0)
                continue;

            var previous = points[index - 1];
            if (previous.MeanMs <= 0 || point.MeanMs <= 0)
                return false;

            var t = (Math.Log(load) - Math.Log(previous.Load)) / (Math.Log(point.Load) - Math.Log(previous.Load));
            ms = Math.Exp(Math.Log(previous.MeanMs) + t * (Math.Log(point.MeanMs) - Math.Log(previous.MeanMs)));
            return true;
        }

        return false;
    }
}

internal readonly record struct CurvePoint(int Load, double MeanMs, double MinMs, double MaxMs, int PassCount)
{
    /** Passes more than 10 % and 0.05 ms apart: heat, clock changes or other load, so the value is less certain. */
    public bool IsUnsteady => PassCount > 1 && MaxMs - MinMs > Math.Max(0.1 * MinMs, MinUnsteadyDifferenceMs);

    /** Below this, differences are timer and scheduling noise rather than a changed machine state. */
    private const double MinUnsteadyDifferenceMs = 0.05;
}

internal sealed class BenchmarkStep
{
    public int Load;

    /** Even passes go from small to large, odd ones from large to small. */
    public int Pass;

    /** Wall-clock time from the start of the evaluation until the GPU finished the test's work. Median of the measured frames. */
    public double StepMs;

    public double StepP90Ms;
    public double CpuMs;
    public double CpuP90Ms;

    /** From GPU timestamps around the same work; NaN where the backend gave no usable timestamps. */
    public double GpuMs = double.NaN;
}

/// <summary>
/// Tests are built from real operators, so editing an operator changes a test without touching the suite. The
/// fingerprint hashes the test's composition and every symbol below it: each symbol as it is saved, plus the
/// source code of its operator class. Source rather than compiled code, so Debug and Release builds of the same
/// operators match.
/// </summary>
internal static class BenchmarkFingerprint
{
    public static string Compute(Symbol root)
    {
        var symbols = new SortedDictionary<Guid, Symbol>();
        Collect(root, symbols);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var symbol in symbols.Values)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(SerializeSymbol(symbol)));
            if (!TryAppendSource(hash, symbol))
                AppendCode(hash, symbol.InstanceType);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }

    private static void Collect(Symbol symbol, SortedDictionary<Guid, Symbol> symbols)
    {
        if (!symbols.TryAdd(symbol.Id, symbol))
            return;

        foreach (var child in symbol.Children.Values)
        {
            Collect(child.Symbol, symbols);
        }
    }

    private static string SerializeSymbol(Symbol symbol)
    {
        using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
        using var jsonWriter = new JsonTextWriter(stringWriter);
        SymbolJson.WriteSymbol(symbol, jsonWriter);
        jsonWriter.Flush();
        return stringWriter.ToString();
    }

    private static bool TryAppendSource(IncrementalHash hash, Symbol symbol)
    {
        if (symbol.SymbolPackage is not EditorSymbolPackage package
            || !package.TryGetSourceCodePath(symbol, out var path)
            || string.IsNullOrEmpty(path)
            || !File.Exists(path))
            return false;

        try
        {
            // Line endings differ between checkouts and platforms; the code doesn't.
            hash.AppendData(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n")));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Fallback for packages that ship without sources; differs between Debug and Release builds.</summary>
    private static void AppendCode(IncrementalHash hash, Type? type)
    {
        if (type == null)
            return;

        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                      | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in type.GetMethods(declared).OrderBy(m => m.ToString(), StringComparer.Ordinal))
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il != null)
                hash.AppendData(il);
        }

        foreach (var constructor in type.GetConstructors(declared).OrderBy(c => c.ToString(), StringComparer.Ordinal))
        {
            var il = constructor.GetMethodBody()?.GetILAsByteArray();
            if (il != null)
                hash.AppendData(il);
        }

        foreach (var nested in type.GetNestedTypes(declared).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            AppendCode(hash, nested);
        }
    }
}
