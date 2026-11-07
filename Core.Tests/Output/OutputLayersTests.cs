using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class OutputLayersTests
{
    [Fact]
    public void WithoutOrder_PatchesAreBehindSurfaces()
    {
        var (setup, output, patch, wall, floor) = CreateSetup();
        var layers = new List<OutputLayers.Layer>();

        OutputLayers.Collect(setup, output, layers);

        Assert.Equal([patch.Id, wall.Id, floor.Id], Ids(layers));
    }

    [Fact]
    public void Move_WritesTheWholeOrder_AndCollectFollowsIt()
    {
        var (setup, output, patch, wall, floor) = CreateSetup();
        var layers = new List<OutputLayers.Layer>();
        OutputLayers.Collect(setup, output, layers);

        // The patch to the front: a projector-camera render over the surfaces.
        OutputLayers.Move(output, layers, 0, 2);
        OutputLayers.Collect(setup, output, layers);

        Assert.Equal([wall.Id, floor.Id, patch.Id], Ids(layers));
        Assert.Equal([wall.Id, floor.Id, patch.Id], output.LayerOrder);
    }

    [Fact]
    public void LayersMissingFromTheOrder_KeepTheirOldPlace()
    {
        var (setup, output, patch, wall, floor) = CreateSetup();
        output.LayerOrder.AddRange([floor.Id, wall.Id]);
        var added = new OutputDefinition.Patch { Quad = OutputDefinition.FullCanvasQuad() };
        output.Patches.Add(added);
        var layers = new List<OutputLayers.Layer>();

        OutputLayers.Collect(setup, output, layers);

        // Unordered patches go to the back, in list order; the ordered surfaces follow as ordered.
        Assert.Equal([patch.Id, added.Id, floor.Id, wall.Id], Ids(layers));
    }

    [Fact]
    public void StaleIds_AreSkipped()
    {
        var (setup, output, patch, wall, floor) = CreateSetup();
        output.LayerOrder.AddRange([Guid.NewGuid(), floor.Id, patch.Id, wall.Id]);
        wall.OutputMappings.Clear();
        var layers = new List<OutputLayers.Layer>();

        OutputLayers.Collect(setup, output, layers);

        Assert.Equal([floor.Id, patch.Id], Ids(layers));
    }

    [Fact]
    public void LayerFields_RoundTrip()
    {
        var (setup, output, patch, wall, _) = CreateSetup();
        output.LayerOrder.AddRange([wall.Id, patch.Id]);
        patch.BlendMode = LayerBlendModes.Multiply;
        patch.LayerColor = new Vector4(1, 0.5f, 0.25f, 0.75f);
        wall.OutputMappings[0].BlendMode = LayerBlendModes.Screen;

        var restored = Setup.ReadFromJson(JObject.Parse(setup.ToJsonString()));
        var restoredOutput = restored.Outputs[0];

        Assert.Equal([wall.Id, patch.Id], restoredOutput.LayerOrder);
        Assert.Equal(LayerBlendModes.Multiply, restoredOutput.Patches[0].BlendMode);
        Assert.Equal(new Vector4(1, 0.5f, 0.25f, 0.75f), restoredOutput.Patches[0].LayerColor);
        Assert.Equal(LayerBlendModes.Screen, restored.FindSurface(wall.Id)!.OutputMappings[0].BlendMode);
        Assert.Equal(Vector4.One, restored.FindSurface(wall.Id)!.OutputMappings[0].LayerColor);
    }

    private static (Setup, OutputDefinition, OutputDefinition.Patch, Surface, Surface) CreateSetup()
    {
        var setup = new Setup();
        var output = new OutputDefinition { Name = "Projector" };
        var patch = new OutputDefinition.Patch { Quad = OutputDefinition.FullCanvasQuad() };
        output.Patches.Add(patch);
        setup.Outputs.Add(output);

        var wall = new Surface { Name = "Wall" };
        wall.OutputMappings.Add(Surface.OutputMapping.CreateFilling(output.Id));
        var floor = new Surface { Name = "Floor" };
        floor.OutputMappings.Add(Surface.OutputMapping.CreateFilling(output.Id));
        var unmapped = new Surface { Name = "Elsewhere" };
        setup.Surfaces.AddRange([wall, floor, unmapped]);
        return (setup, output, patch, wall, floor);
    }

    private static List<Guid> Ids(List<OutputLayers.Layer> layers)
    {
        var ids = new List<Guid>();
        foreach (var layer in layers)
            ids.Add(layer.Id);

        return ids;
    }
}
