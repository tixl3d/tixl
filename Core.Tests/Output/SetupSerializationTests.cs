using System;
using System.Collections.Generic;
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

        var surface = Assert.Single(restored.Surfaces);
        Assert.Equal(setup.Surfaces[0].Id, surface.Id);
        Assert.Equal(new Vector2(5, 3), surface.SizeInMeters);
        Assert.Equal(400, surface.PixelsPerMeter);

        var mapping = Assert.Single(surface.OutputMappings);
        Assert.Equal(setup.Outputs[1].Id, mapping.OutputId);
        Assert.Equal(Surface.OutputMapping.Modes.CornerPin, mapping.Mode);
        Assert.Equal(new Vector2(210, 95), mapping.Quad[0]);
        Assert.Equal(new Vector2(215, 905), mapping.Quad[3]);

        Assert.NotNull(surface.Reference);
        Assert.Equal(setup.ReferenceImages[0].Id, surface.Reference!.ImageId);
        var annotation = Assert.Single(surface.Reference.Annotations);
        Assert.Equal(4.5f, annotation.LengthInMeters);
        Assert.Equal("mortar-3", annotation.Name);
        Assert.True(annotation.ShowArrows);

        Assert.NotNull(surface.Placement);
        Assert.Equal(new Vector3(0.5f, 0, 1), surface.Placement!.Pose.Position);
        Assert.Equal(new Vector2(0.5f, 0), surface.Placement.Pivot);

        Assert.Equal(2, restored.Outputs.Count);
        Assert.Equal(OutputDefinition.Kinds.Default, restored.Outputs[0].Kind);
        var projector = restored.Outputs[1];
        Assert.Equal(OutputDefinition.Kinds.Projector, projector.Kind);
        Assert.Equal(new Int2(1920, 1200), projector.CanvasResolution);
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
    public void MinimalJson_LoadsWithDefaults()
    {
        var setup = Setup.ReadFromJson(JObject.Parse("""{ "Version": 1, "Name": "sparse" }"""));

        Assert.NotNull(setup);
        Assert.Equal("sparse", setup!.Name);
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

        Assert.NotNull(setup);
        var surface = Assert.Single(setup!.Surfaces);
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
        Assert.Null(restored.Surfaces[0].Reference);
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

    private static Setup RoundTrip(Setup setup)
    {
        var restored = Setup.ReadFromJson(JObject.Parse(setup.ToJsonString()));
        Assert.NotNull(restored);
        return restored!;
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
                                                   new Vector2(210, 95),
                                                   new Vector2(1660, 120),
                                                   new Vector2(1655, 940),
                                                   new Vector2(215, 905),
                                               ],
                                           }
                                   ],
                                   Reference = new Surface.ReferenceBinding
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
                                                           new LineAnnotation
                                                               {
                                                                   P1 = new Vector2(500, 400),
                                                                   P2 = new Vector2(1200, 410),
                                                                   LengthInMeters = 4.5f,
                                                                   Name = "mortar-3",
                                                                   ShowArrows = true,
                                                               }
                                                       ],
                                                   },
                                   Placement = new Surface.StagePlacement
                                                   {
                                                       Pose = new Pose(new Vector3(0.5f, 0, 1), Quaternion.Identity),
                                                       Pivot = new Vector2(0.5f, 0),
                                                   },
                               });
        setup.Props.Add(new Prop { Position = new Vector3(1.5f, 0, 1.2f), HeightInMeters = 1.70f });
        return setup;
    }
}
