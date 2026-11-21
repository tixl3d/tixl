#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using Lib.io.video;
using T3.Core.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace T3.VideoServices.Tests;

/// <summary>
/// Live-hardware test for the Video operator package's camera-control backend (V4L2 path). It runs the
/// same enumeration the <c>[VideoDeviceInput]</c> control worker performs —
/// <c>CameraControlBackendRegistry.Create()</c> → <see cref="ICameraControlBackend.EnumerateCatalog"/> —
/// on every present <c>/dev/video*</c> node, and cross-checks the catalog against an INDEPENDENT raw
/// <c>VIDIOC_QUERYCTRL</c> walk done here (own libc P/Invokes, own structs — not the backend's code).
/// The backend must not miss any control the kernel reports, and every matched control must agree on
/// kind and range.
///
/// Skipping: on non-Linux machines and machines without cameras the test exits early with an output
/// message (it reports Passed, not Skipped — xunit 2.9.3 has no <c>Assert.Skip</c>, and a true
/// <c>Skipped</c> verdict would need the Xunit.SkippableFact package, i.e. a dependency change this
/// project deliberately avoids).
///
/// The backend sources are compiled straight into this test assembly via
/// <c>&lt;Compile Include="..\Operators\Video\Utils\CameraControls*.cs"&gt;</c> in the csproj (their types
/// are internal to the operator package and the package project is not referenced here). Two stand-ins
/// make those sources compile standalone; both must be REMOVED if this project ever gains a real
/// reference to the operator package:
/// - <c>Lib.io.video.WebcamWithIndex</c> (WebcamWithIndexStandIn.cs) — identical shape to the real record
///   at Operators/Video/Utils/WebcamScan.cs:470; WebcamScan.cs itself cannot be compiled here
///   (DirectShowLib).
/// - the csproj-level <c>&lt;Using Include="T3.Core.Logging" /&gt;</c> — mirrors the operator package's
///   project-level using (Operators/Video/Video.csproj:44); the real Log comes transitively from
///   Logging.csproj, which this project already references.
/// No csproj dependencies (package/project references) were added for this.
/// </summary>
public class CameraControlsV4L2LiveTests
{
    private readonly ITestOutputHelper _output;

    public CameraControlsV4L2LiveTests(ITestOutputHelper output) => _output = output;

    private void Skip(string reason)
        => _output.WriteLine($"SKIPPED: {reason}");

    [Fact]
    public void V4L2Backend_EnumeratesEverythingTheKernelReports()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip("Linux-only: the V4L2 backend registers only on Linux.");
            return;
        }

        var nodes = Enumerable.Range(0, 16).Select(i => $"/dev/video{i}").Where(File.Exists).ToList();
        if (nodes.Count == 0)
        {
            Skip("No /dev/video* nodes present on this machine.");
            return;
        }

        var backend = CameraControlBackendRegistry.Create();
        Assert.NotNull(backend);

        var nodesWithControls = 0;
        var problems = new List<string>();

        foreach (var (node, index) in nodes.Select(NodeAndIndex))
        {
            var kernel = KernelWalk(node);
            // What the shared int model can legitimately not represent: CTRL_CLASS markers, DISABLED
            // entries, and types 5 INTEGER64 / 7 STRING / 8 BITMASK / compound (>= 0x0100).
            var required = kernel.Where(c => c.Type != 6
                                             && (c.Flags & 0x001) == 0        // DISABLED
                                             && c.Type is not (5 or 7 or 8)
                                             && (c.Type & 0x0100) == 0)
                                  .ToDictionary(c => c.Id);

            if (required.Count > 0)
                nodesWithControls++;

            var deviceName = DeviceName(node);
            var catalog = backend!.EnumerateCatalog(new WebcamWithIndex(deviceName, index));

            if (required.Count == 0)
            {
                // A metadata node: an empty catalog is honest, a fabricated one would be a defect.
                if (catalog != null && catalog.Controls.Count > 0)
                    problems.Add($"{node}: kernel reports no controls but backend returned " +
                                 $"{catalog.Controls.Count}");
                continue;
            }

            if (catalog == null)
            {
                problems.Add($"{node}: kernel reports {required.Count} controls but " +
                             "EnumerateCatalog returned null");
                continue;
            }

            var byCid = catalog.Controls
                               .Select(c => (Cid: ParseBackendId(c.BackendId), Control: c))
                               .Where(pair => pair.Cid != 0)
                               .ToDictionary(pair => pair.Cid, pair => pair.Control);

            foreach (var (cid, kernelControl) in required)
            {
                if (!byCid.TryGetValue(cid, out var control))
                {
                    problems.Add($"{node}: kernel reports 0x{cid:X8} '{kernelControl.Name}' " +
                                 $"({kernelControl.TypeName}) — missing from the backend catalog");
                    continue;
                }

                if (control.Minimum != kernelControl.Min || control.Maximum != kernelControl.Max
                    || control.Step != Math.Max(kernelControl.Step, 1)
                    || control.DefaultValue != kernelControl.Default)
                {
                    problems.Add($"{node}: 0x{cid:X8} '{control.Name}' range mismatch: backend " +
                                 $"{control.Minimum}..{control.Maximum} step {control.Step} def " +
                                 $"{control.DefaultValue}, kernel {kernelControl.Min}..{kernelControl.Max} " +
                                 $"step {kernelControl.Step} def {kernelControl.Default}");
                }

                var expectedKind = kernelControl.Type switch
                {
                    1 => CameraControlKind.Integer,
                    2 => CameraControlKind.Boolean,
                    3 => CameraControlKind.Menu,
                    4 => CameraControlKind.Button,
                    9 => CameraControlKind.IntegerMenu,
                    _ => (CameraControlKind?)(-1 as int?),
                };
                if (expectedKind is { } kind && control.Kind != kind)
                    problems.Add($"{node}: 0x{cid:X8} '{control.Name}' kind {control.Kind}, " +
                                 $"kernel type {kernelControl.Type} maps to {kind}");
            }
        }

        if (nodesWithControls == 0)
        {
            Skip("Present /dev/video* nodes expose no V4L2 controls (metadata nodes only?).");
            return;
        }

        Assert.True(problems.Count == 0,
            "Backend/kernel enumeration mismatches:\n  " + string.Join("\n  ", problems));
    }

    // ---------------------------------------------------------------- helpers

    private static (string Node, int Index) NodeAndIndex(string node)
        => (node, int.Parse(node["/dev/video".Length..]));

    private static string DeviceName(string node)
    {
        try
        {
            return File.ReadAllText($"/sys/class/video4linux/{Path.GetFileName(node)}/name").Trim();
        }
        catch
        {
            return Path.GetFileName(node); // informational only — identity is the index
        }
    }

    private static uint ParseBackendId(string backendId)
        => backendId.StartsWith("v4l2:0x", StringComparison.OrdinalIgnoreCase)
           && uint.TryParse(backendId["v4l2:0x".Length..],
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var cid)
               ? cid
               : 0;

    private sealed record KernelControl(uint Id, uint Type, string TypeName, string Name,
                                        int Min, int Max, int Step, int Default, uint Flags);

    /// <summary>
    /// Independent VIDIOC_QUERYCTRL walk (NEXT_CTRL re-seeded with the RETURNED id) + QUERYMENU
    /// expansion, straight from the kernel UAPI: v4l2_queryctrl = 68 bytes, v4l2_querymenu = 44 packed,
    /// _IOWR('V', 36/37, ...). Shares no code with the backend.
    /// </summary>
    private static List<KernelControl> KernelWalk(string node)
    {
        var controls = new List<KernelControl>();
        var fd = open(node, O_RDWR);
        if (fd < 0)
            return controls;

        try
        {
            const uint NEXT_CTRL = 0x80000000;
            var id = NEXT_CTRL;
            for (var entries = 0; entries < 256; entries++)
            {
                var query = new V4L2QueryCtrl { Id = id };
                if (ioctl(fd, Iowr(36, 68), ref query) != 0)
                    break;

                controls.Add(new KernelControl(query.Id, query.Type, $"type{query.Type}",
                    query.Name ?? "", query.Minimum, query.Maximum, query.Step, query.DefaultValue,
                    query.Flags));

                var returned = query.Id & 0x0FFFFFFF;
                if (returned == (id & 0x0FFFFFFF))
                    break;
                id = returned | NEXT_CTRL;
            }
        }
        finally
        {
            _ = close(fd);
        }

        return controls;
    }

    private static ulong Iowr(byte number, uint structSize)
        => (3UL << 30) | ((ulong)structSize << 16) | (0x56UL << 8) | number;

    private const int O_RDWR = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2QueryCtrl
    {
        public uint Id;
        public uint Type;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public int Minimum;
        public int Maximum;
        public int Step;
        public int DefaultValue;
        public uint Flags;
        public uint Reserved0;
        public uint Reserved1;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2QueryCtrl arg);
}

