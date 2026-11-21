using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;
using VolumeNoiseTool = T3.Core.DataTypes.Volume.Tools.VolumeNoise;

namespace Lib.Tests.Volume;

/// <summary>
/// The volume chains of the example graphs under Operators/Examples/Symbols/Lib/volume, rebuilt with the same numbers. The
/// graphs need an editor; these keep their chains producing the surfaces the examples are laid out around.
/// </summary>
public class VolumeExampleScenesTests
{
    [Fact]
    public void EveryPrimitiveHasASurfaceOfItsSize()
    {
        foreach (var shape in new[]
                              {
                                  VolumePrimitives.Shape.Sphere, VolumePrimitives.Shape.Box, VolumePrimitives.Shape.RoundedBox, VolumePrimitives.Shape.Torus,
                                  VolumePrimitives.Shape.Capsule, VolumePrimitives.Shape.Cylinder, VolumePrimitives.Shape.Cone, VolumePrimitives.Shape.Octahedron,
                              })
        {
            var (min, max) = Bounds(VolumeToMesh.Extract(VolumePrimitives.Create(shape, new Vector3(1f, 1.1f, 1f), 0.03f, Vector3.Zero, 3)));
            Assert.InRange(max.X - min.X, 0.9f, 2.2f);
            Assert.True(max.Y - min.Y > 0.9f, $"{shape} is {max - min}");
        }
    }

    [Fact]
    public void CsgBodyIsDrilledThrough()
    {
        // VolumeCsgExample: rounded box AND sphere, minus three bars.
        var body = VolumeCsg.Combine(Primitive(VolumePrimitives.Shape.RoundedBox, new Vector3(1.6f)),
                                     Primitive(VolumePrimitives.Shape.Sphere, new Vector3(2.1f)), VolumeCsg.Operation.Intersection);
        var drill = VolumeCsg.Combine(VolumeCsg.Combine(Primitive(VolumePrimitives.Shape.Cylinder, new Vector3(0.75f, 2.6f, 0.75f)),
                                                        Primitive(VolumePrimitives.Shape.Box, new Vector3(2.6f, 0.6f, 0.6f)), VolumeCsg.Operation.Union),
                                      Primitive(VolumePrimitives.Shape.Box, new Vector3(0.6f, 0.6f, 2.6f)), VolumeCsg.Operation.Union);
        var result = VolumeCsg.Combine(body, drill, VolumeCsg.Operation.Difference);

        var sampler = new VdbSampler(result);
        Assert.True(sampler.SampleWorld(new Vector3(0, 0, 0)) > 0f, "the centre is drilled out");
        Assert.True(sampler.SampleWorld(new Vector3(0.6f, 0.6f, 0.6f)) < 0f, "the body between the holes is solid");
        Assert.True(sampler.SampleWorld(new Vector3(0.95f, 0.95f, 0.95f)) > 0f, "the sphere cuts the box's corners off");
    }

    [Fact]
    public void NoiseChainKeepsAClosedBlob()
    {
        // VolumeNoiseExample: displace, grow, smooth.
        var sphere = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1.8f), 0.035f, Vector3.Zero, 8);
        var displaced = VolumeNoiseTool.Apply(sphere, new VdbNoise(7), 1.6f, 5f, Vector3.Zero, 4, VdbNoiseType.Perlin, VdbNoiseFractalType.Fbm,
                                              VolumeNoiseTool.Mode.Displace);
        var grown = VolumeMorphology.Apply(displaced, VolumeMorphology.Operation.Dilate, 0.15f);
        var smooth = VolumeFilter.Apply(grown, VolumeFilter.FilterType.Gaussian, 2, 0, 2);

        var (displacedMin, displacedMax) = Bounds(VolumeToMesh.Extract(displaced));
        var (grownMin, grownMax) = Bounds(VolumeToMesh.Extract(grown));
        Assert.True(displacedMax.X - displacedMin.X > 1.6f);
        Assert.True(grownMax.X - grownMin.X > displacedMax.X - displacedMin.X + 0.15f, "dilating grows the blob");
        Assert.True(VolumeToMesh.Extract(smooth).FaceCount > 1000);
    }

    [Fact]
    public void SplattedPointsMeshIntoOneCloud()
    {
        // VolumeFromMeshExample, middle: 90 points splatted with radius 0.28 and meshed at density 0.5.
        var random = new Random(4);
        var points = new Vector3[90];
        for (var index = 0; index < points.Length; index++)
            points[index] = new Vector3(0, 0.3f, 0) + (new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()) - new Vector3(0.5f)) * new Vector3(1.4f, 1.2f, 1.4f);

        var (min, max) = Bounds(VolumeToMesh.Extract(PointsToVolume.Splat(points, null, 0.03f, 0.28f, 1f, PointsToVolume.Falloff.Gaussian, 0), 0.5f));
        Assert.InRange(max.X - min.X, 1.2f, 2.2f);
    }

    [Fact]
    public void WaterStaysInsideItsBox()
    {
        // VolumeShakingWaterExample: a noisy slab clipped to the inside of the open box.
        var water = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(2.6f, 1.2f, 2.6f), 0.03f, new Vector3(0, -0.2f, 0), 5);
        var waves = VolumeNoiseTool.Apply(water, new VdbNoise(11), 2.2f, 3f, Vector3.Zero, 3, VdbNoiseType.Perlin, VdbNoiseFractalType.Fbm,
                                          VolumeNoiseTool.Mode.Displace);
        var clip = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(2.7f, 2f, 2.7f), 0.03f, new Vector3(0, 0.15f, 0), 3);
        var (min, max) = Bounds(VolumeToMesh.Extract(VolumeCsg.Combine(waves, clip, VolumeCsg.Operation.Intersection)));

        Assert.True(min.X > -1.36f && max.X < 1.36f, "clipped to the box walls");
        Assert.InRange(max.Y, 0.3f, 0.6f);
    }

    [Fact]
    public void RecordsTheCostOfEveryExampleChain()
    {
        // PhysicsToVolumeExample re-splats and re-meshes the balls on every frame: that is the one volume chain that
        // runs per frame. The others run once per input change on a worker; their cost is how long the result takes to appear.
        var random = new Random(2);
        var balls = new Vector3[60];
        for (var index = 0; index < balls.Length; index++)
            balls[index] = new Vector3(random.NextSingle() - 0.5f, random.NextSingle(), random.NextSingle() - 0.5f) * new Vector3(2.5f, 1.2f, 2.5f);

        for (var frame = 0; frame < 120; frame++)
        {
            for (var index = 0; index < balls.Length; index++)
                balls[index] += new Vector3(MathF.Sin(frame * 0.1f + index), 0, MathF.Cos(frame * 0.1f + index)) * 0.01f;

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var skin = VolumeToMesh.Extract(PointsToVolume.Splat(balls, null, 0.12f, 0.55f, 1f, PointsToVolume.Falloff.Gaussian, 0), 0.35f);
            FrameTimes.Record("PhysicsToVolumeExample per frame: splat + mesh", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.True(skin.FaceCount > 0);
        }

        Once("VolumeNoiseExample once: displace, dilate, smooth, mesh", () =>
                 {
                     var sphere = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1.8f), 0.035f, Vector3.Zero, 8);
                     var displaced = VolumeNoiseTool.Apply(sphere, new VdbNoise(7), 1.6f, 5f, Vector3.Zero, 4, VdbNoiseType.Perlin, VdbNoiseFractalType.Fbm,
                                                           VolumeNoiseTool.Mode.Displace);
                     var grown = VolumeMorphology.Apply(displaced, VolumeMorphology.Operation.Dilate, 0.15f);
                     VolumeToMesh.Extract(VolumeFilter.Apply(grown, VolumeFilter.FilterType.Gaussian, 2, 0, 2));
                 });
        Once("VolumeShakingWaterExample once: water chain + mesh", () =>
                 {
                     var water = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(2.6f, 1.2f, 2.6f), 0.03f, new Vector3(0, -0.2f, 0), 5);
                     var waves = VolumeNoiseTool.Apply(water, new VdbNoise(11), 2.2f, 3f, Vector3.Zero, 3, VdbNoiseType.Perlin, VdbNoiseFractalType.Fbm,
                                                       VolumeNoiseTool.Mode.Displace);
                     var clip = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(2.7f, 2f, 2.7f), 0.03f, new Vector3(0, 0.15f, 0), 3);
                     VolumeToMesh.Extract(VolumeCsg.Combine(waves, clip, VolumeCsg.Operation.Intersection));
                 });
        Once("VolumeCsgExample once: csg chain", () => CsgBodyIsDrilledThrough());
    }

    private static void Once(string name, Action chain)
    {
        // The second run is the one that counts: the first one also compiles the code paths.
        chain();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        chain();
        FrameTimes.Record(name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static VdbGrid<float> Primitive(VolumePrimitives.Shape shape, Vector3 size) => VolumePrimitives.Create(shape, size, 0.03f, Vector3.Zero, 3);

    private static (Vector3 Min, Vector3 Max) Bounds(MeshGeometry geometry)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in geometry.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min, max);
    }
}
