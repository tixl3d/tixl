using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class SetupSerializationTests
{
    [Fact]
    public void FullSetup_RoundTripsAllFields()
    {
        var setup = CreateStudioSetup();
        var restored = RoundTrip(setup);

        Assert.Equal(setup.Id, restored.Id);
        Assert.Equal("studio", restored.Name);

        var image = Assert.Single(restored.ReferenceImages);
        Assert.Equal(setup.ReferenceImages[0].Id, image.Id);
        Assert.Equal(ReferenceImage.Kinds.Photo, image.Kind);
        Assert.Equal("images/refs/corner.jpg", image.FilePath);
        Assert.Equal(4032, image.Width);
        Assert.Equal(new Vector2(100, 50), image.CropMin);
        Assert.Equal(new Vector2(3900, 2900), image.CropMax);

        var surface = Assert.Single(restored.Surfaces);
        Assert.Equal(setup.Surfaces[0].Id, surface.Id);
        Assert.Equal(new Vector2(5, 3), surface.SizeInMeters);
        Assert.Equal(400, surface.PixelsPerMeter);

        var mapping = Assert.Single(surface.OutputMappings);
        Assert.Equal(setup.Outputs[1].Id, mapping.OutputId);
        Assert.Equal(MappingModes.CornerPin, mapping.Mode);
        Assert.Equal(new Vector2(0.11f, 0.08f), mapping.Quad[0]);
        Assert.Equal(new Vector2(0.112f, 0.755f), mapping.Quad[3]);

        Assert.NotNull(surface.Trace);
        Assert.Equal(setup.ReferenceImages[0].Id, surface.Trace!.ImageId);
        var annotation = Assert.Single(surface.Trace.Annotations);
        Assert.Equal(4.5f, annotation.LengthInMeters);
        Assert.Equal("mortar-3", annotation.Name);
        Assert.True(annotation.ShowArrows);

        Assert.NotNull(surface.Placement);
        Assert.Equal(new Vector3(0.5f, 0, 1), surface.Placement!.Pose.Position);
        Assert.Equal(new Vector2(0.4f, 1), surface.Anchor);
        Assert.NotNull(surface.BoardPlacement);
        Assert.Equal(new Vector2(1.5f, 0), surface.BoardPlacement!.Position);

        Assert.Equal(2, restored.Outputs.Count);
        Assert.Equal(OutputDefinition.Kinds.Default, restored.Outputs[0].Kind);
        var projector = restored.Outputs[1];
        Assert.Equal(OutputDefinition.Kinds.Projector, projector.Kind);
        Assert.Equal(new Int2(1920, 1200), projector.CanvasResolution);
        Assert.NotNull(projector.BoardPlacement);
        Assert.Equal(800f, projector.BoardPlacement!.CardScale);
        var patch = Assert.Single(projector.Patches);
        Assert.Equal(setup.Outputs[1].Patches[0].Id, patch.Id);
        Assert.Equal(setup.Outputs[1].Patches[0].SliceId, patch.SliceId);
        Assert.Equal("left half", patch.Name);
        Assert.Equal(new Vector2(0.5f, 0), patch.Quad[1]);
        Assert.Equal(new Vector2(0, 1), patch.Quad[3]);
        Assert.NotNull(projector.Camera);
        Assert.NotNull(projector.Camera!.Pose);
        Assert.NotNull(projector.Camera.Lens);
        Assert.Equal(0.66f, projector.Camera.Lens!.Value.FieldOfViewY, 4);
        var calibrationPoint = Assert.Single(projector.Camera.CalibrationPoints);
        Assert.Equal(new Vector3(1, 2, 0), calibrationPoint.StagePosition);

        var prop = Assert.Single(restored.Props);
        Assert.Equal(1.70f, prop.HeightInMeters);
    }

    [Fact]
    public void FullSetup_RoundTrip_NeedsNoRepair()
    {
        var restored = RoundTrip(CreateStudioSetup());
        var before = restored.ToJsonString();

        Assert.False(SetupRepair.Repair(restored));
        Assert.Equal(before, restored.ToJsonString());
    }

    [Fact]
    public void Duplicate_PreservesEntityGuids_ButGetsNewSetupId()
    {
        var setup = CreateStudioSetup();
        var duplicate = setup.Duplicate("venue");

        Assert.NotEqual(setup.Id, duplicate.Id);
        Assert.Equal("venue", duplicate.Name);

        // The venue-swap contract: every op-bindable entity keeps its GUID
        Assert.Equal(setup.Surfaces[0].Id, duplicate.Surfaces[0].Id);
        Assert.Equal(setup.Outputs[0].Id, duplicate.Outputs[0].Id);
        Assert.Equal(setup.Outputs[1].Id, duplicate.Outputs[1].Id);
        Assert.Equal(setup.ReferenceImages[0].Id, duplicate.ReferenceImages[0].Id);
    }

    [Fact]
    public void SurfaceWithoutAnchor_ReadsBottomCentre()
    {
        var setup = Setup.ReadFromJson(JObject.Parse("""{ "Version": 1, "Name": "s", "Surfaces": [ { "Name": "wall", "SizeInMeters": [4, 2] } ] }"""));

        var surface = Assert.Single(setup.Surfaces);
        Assert.Equal(Surface.DefaultAnchor, surface.Anchor);
        Assert.Equal(new Vector2(2, 0), surface.AnchorInMeters);
    }

    [Fact]
    public void MinimalJson_LoadsWithDefaults()
    {
        var setup = Setup.ReadFromJson(JObject.Parse("""{ "Version": 1, "Name": "sparse" }"""));

        Assert.Equal("sparse", setup.Name);
        Assert.Empty(setup.Surfaces);
        Assert.Empty(setup.Outputs);
    }

    [Fact]
    public void UnknownFieldsAndNewerVersion_AreTolerated()
    {
        var setup = Setup.ReadFromJson(JObject.Parse("""
            {
              "Version": 99,
              "Name": "future",
              "SomeFutureFeature": { "nested": true },
              "Surfaces": [
                { "Name": "wall", "SizeInMeters": [2, 1], "FutureField": 42 },
                "garbage-entry"
              ]
            }
            """));

        var surface = Assert.Single(setup.Surfaces);
        Assert.Equal("wall", surface.Name);
        Assert.Equal(new Vector2(2, 1), surface.SizeInMeters);
    }

    [Fact]
    public void LadderIsMonotone_L1SurfaceHasNoOptionalSections()
    {
        var setup = Setup.CreateDefault();
        setup.Surfaces.Add(new Surface { Name = "poster-left", SizeInMeters = new Vector2(0.6f, 0.9f) });

        var json = JObject.Parse(setup.ToJsonString());
        var surfaceJson = (JObject)json["Surfaces"]![0]!;

        // L1 surfaces don't write the L2/L3 upgrade fields at all
        Assert.Null(surfaceJson["Reference"]);
        Assert.Null(surfaceJson["Placement"]);

        var restored = RoundTrip(setup);
        Assert.Null(restored.Surfaces[0].Trace);
        Assert.Null(restored.Surfaces[0].Placement);
    }

    [Fact]
    public void CreateDefault_ContainsDefaultOutput()
    {
        var setup = Setup.CreateDefault();
        var output = Assert.Single(setup.Outputs);
        Assert.Equal(OutputDefinition.Kinds.Default, output.Kind);
        Assert.Equal("Default", output.Name);
    }

    [Fact]
    public void TryLoadFromFile_TakesNameFromFileName()
    {
        using var folder = new TemporaryFolder();
        var copyPath = Path.Combine(folder.Path, "Hall_old" + Setup.FileSuffix);
        Assert.True(Setup.CreateDefault("Hall").TrySaveToFile(copyPath));

        Assert.True(Setup.TryLoadFromFile(copyPath, out var loaded, out _));

        Assert.Equal("Hall_old", loaded.Name);
    }

    [Fact]
    public void TrySaveToFile_ReplacesFileAndLeavesNoTemporaryFile()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "Hall" + Setup.FileSuffix);
        Assert.True(Setup.CreateDefault("Hall").TrySaveToFile(path));

        var edited = Setup.CreateDefault("Hall");
        edited.Surfaces.Add(new Surface { Name = "Wall" });
        Assert.True(edited.TrySaveToFile(path));

        Assert.True(Setup.TryLoadFromFile(path, out var loaded, out _));
        Assert.Single(loaded.Surfaces);
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void TryLoad_WithUnreadableActiveSetup_FallsBackAndLeavesItsFileAlone()
    {
        using var folder = new TemporaryFolder();
        const string brokenContent = "{ not json";
        var brokenPath = Path.Combine(folder.Path, "Hall" + Setup.FileSuffix);
        File.WriteAllText(brokenPath, brokenContent);
        Assert.True(Setup.CreateDefault("Club").TrySaveToFile(Path.Combine(folder.Path, "Club" + Setup.FileSuffix)));
        Assert.True(new MachineConfig { ActiveSetupName = "Hall" }.TrySaveToFile(Path.Combine(folder.Path, MachineConfig.FileName)));

        Assert.True(SetupFiles.TryLoad(folder.Path, out var setup, out _, out _));

        Assert.Equal("Club", setup!.Name);
        Assert.Equal(brokenContent, File.ReadAllText(brokenPath));
    }

    [Fact]
    public void MachineConfigLoad_WhenUnreadable_KeepsACopyOfTheFile()
    {
        using var folder = new TemporaryFolder();
        const string brokenContent = "{ not json";
        var path = Path.Combine(folder.Path, MachineConfig.FileName);
        File.WriteAllText(path, brokenContent);

        Assert.False(MachineConfig.TryLoadFromFile(path, out var config));

        Assert.Empty(config.Bindings);
        Assert.Equal(brokenContent, File.ReadAllText(path + ".unreadable"));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TemporaryFolder() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void ParkedMappings_RoundTripWithTheirAlignment()
    {
        var setup = Setup.CreateDefault();
        var output = setup.Outputs[0];
        var pointId = Guid.NewGuid();
        var parked = new Surface.OutputMapping
                         {
                             OutputId = output.Id,
                             Quad = [new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.1f), new Vector2(0.8f, 0.9f), new Vector2(0.2f, 0.8f)],
                         };
        parked.PointAims[pointId] = new Surface.OutputMapping.PointAim(new Vector2(0.4f, 0.5f), true);
        var wall = new Surface { Name = "wall" };
        wall.ParkedMappings.Add(parked);
        setup.Surfaces.Add(wall);

        var restored = Assert.Single(RoundTrip(setup).Surfaces);
        Assert.Empty(restored.OutputMappings);
        var restoredParked = Assert.Single(restored.ParkedMappings);
        Assert.Equal(output.Id, restoredParked.OutputId);
        Assert.Equal(new Vector2(0.9f, 0.1f), restoredParked.Quad[1]);
        Assert.True(restoredParked.PointAims[pointId].IsAimed);
    }

    private static Setup RoundTrip(Setup setup)
    {
        return Setup.ReadFromJson(JObject.Parse(setup.ToJsonString()));
    }

    private static Setup CreateStudioSetup()
    {
        var image = new ReferenceImage
                        {
                            Name = "north-corner",
                            Kind = ReferenceImage.Kinds.Photo,
                            FilePath = "images/refs/corner.jpg",
                            Width = 4032,
                            Height = 3024,
                            CropMin = new Vector2(100, 50),
                            CropMax = new Vector2(3900, 2900),
                        };

        var projector = new OutputDefinition
                            {
                                Name = "P1",
                                Kind = OutputDefinition.Kinds.Projector,
                                CanvasResolution = new Int2(1920, 1200),
                                Camera = new OutputDefinition.ProjectorCamera
                                             {
                                                 Pose = new Pose(new Vector3(1.8f, 1.6f, 3.5f), Quaternion.Identity),
                                                 Lens = Projection.CreatePerspective(0.66f, new Vector2(0, 0.4f)),
                                                 CalibrationPoints = [new CalibrationPoint { StagePosition = new Vector3(1, 2, 0), OutputPixel = new Vector2(600, 300) }],
                                                 ResidualPx = 0.4f,
                                             },
                            };
        projector.BoardPlacement = new BoardPlacement { Position = new Vector2(7, 0), CardScale = 800 };
        projector.Patches.Add(new OutputDefinition.Patch
                                  {
                                      Name = "left half",
                                      SliceId = Guid.NewGuid(),
                                      Quad = [Vector2.Zero, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, 1)],
                                  });

        var setup = Setup.CreateDefault("studio");
        setup.ReferenceImages.Add(image);
        setup.Outputs.Add(projector);
        setup.Surfaces.Add(new Surface
                               {
                                   Name = "wall",
                                   SizeInMeters = new Vector2(5, 3),
                                   OutputMappings =
                                   [
                                       new Surface.OutputMapping
                                           {
                                               OutputId = projector.Id,
                                               Quad =
                                               [
                                                   new Vector2(0.11f, 0.08f),
                                                   new Vector2(0.865f, 0.1f),
                                                   new Vector2(0.86f, 0.78f),
                                                   new Vector2(0.112f, 0.755f),
                                               ],
                                           }
                                   ],
                                   Trace = new Surface.TraceBinding
                                                   {
                                                       ImageId = image.Id,
                                                       Quad =
                                                       [
                                                           new Vector2(430, 300),
                                                           new Vector2(1450, 180),
                                                           new Vector2(1450, 900),
                                                           new Vector2(430, 830),
                                                       ],
                                                       Annotations =
                                                       [
                                                           new Annotation
                                                               {
                                                                   P1 = new Vector2(500, 400),
                                                                   P2 = new Vector2(1200, 410),
                                                                   LengthInMeters = 4.5f,
                                                                   Name = "mortar-3",
                                                                   ShowArrows = true,
                                                               }
                                                       ],
                                                   },
                                   Anchor = new Vector2(0.4f, 1),
                                   BoardPlacement = new BoardPlacement { Position = new Vector2(1.5f, 0) },
                                   Placement = new Surface.StagePlacement
                                                   {
                                                       Pose = new Pose(new Vector3(0.5f, 0, 1), Quaternion.Identity),
                                                   },
                               });
        setup.Props.Add(new Prop { Position = new Vector3(1.5f, 0, 1.2f), HeightInMeters = 1.70f });
        return setup;
    }
}
