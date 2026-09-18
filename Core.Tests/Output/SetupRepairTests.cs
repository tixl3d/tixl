using System;
using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class SetupRepairTests
{
    [Fact]
    public void FindMappedAncestor_ReturnsSelfOrNearestMappedParent()
    {
        var setup = Setup.CreateDefault();
        var output = setup.Outputs[0];
        var wall = new Surface { Name = "wall", OutputMappings = [new Surface.OutputMapping { OutputId = output.Id, Quad = UnitQuad() }] };
        var region = new Surface { Name = "region", Kind = Surface.Kinds.Layout, ParentId = wall.Id };
        var subRegion = new Surface { Name = "sub", Kind = Surface.Kinds.Layout, ParentId = region.Id };
        var orphan = new Surface { Name = "orphan" };
        setup.Surfaces.AddRange([wall, region, subRegion, orphan]);

        Assert.Same(wall, setup.FindMappedAncestor(wall.Id));
        Assert.Same(wall, setup.FindMappedAncestor(region.Id));
        Assert.Same(wall, setup.FindMappedAncestor(subRegion.Id));
        Assert.Null(setup.FindMappedAncestor(orphan.Id));
        Assert.Null(setup.FindMappedAncestor(Guid.Empty));
        Assert.Null(setup.FindMappedAncestor(Guid.NewGuid()));
    }

    [Fact]
    public void FindMappedAncestor_SurvivesParentCycle()
    {
        var setup = Setup.CreateDefault();
        var a = new Surface { Name = "a", Kind = Surface.Kinds.Layout };
        var b = new Surface { Name = "b", Kind = Surface.Kinds.Layout, ParentId = a.Id };
        a.ParentId = b.Id;
        setup.Surfaces.AddRange([a, b]);

        Assert.Null(setup.FindMappedAncestor(a.Id));
    }

    [Fact]
    public void Repair_GivesPatchesThatShareAnIdNewOnes()
    {
        var setup = Setup.CreateDefault();
        var first = new OutputDefinition { Name = "first" };
        var second = new OutputDefinition { Name = "second" };
        var shared = new OutputDefinition.Patch { Quad = OutputDefinition.FullCanvasQuad() };
        var copy = new OutputDefinition.Patch { Id = shared.Id, Quad = OutputDefinition.FullCanvasQuad() };
        first.Patches.Add(shared);
        second.Patches.Add(copy);
        setup.Outputs.AddRange([first, second]);

        Assert.True(SetupRepair.Repair(setup));
        Assert.NotEqual(shared.Id, copy.Id);
        Assert.False(SetupRepair.Repair(setup));
    }

    [Fact]
    public void Repair_ResetsCorruptedQuadsAndDetachesNestedPhysicalSurfaces()
    {
        var setup = Setup.CreateDefault();
        var output = setup.Outputs[0];
        var parent = new Surface { Name = "parent" };
        var nestedPhysical = new Surface { Name = "nested", ParentId = parent.Id, SizeInMeters = new Vector2(float.NaN, 1) };
        nestedPhysical.OutputMappings.Add(new Surface.OutputMapping
                                              {
                                                  OutputId = output.Id,
                                                  Quad = [new Vector2(500, 0), new Vector2(600, 0), new Vector2(600, 100), new Vector2(500, 100)],
                                              });
        setup.Surfaces.AddRange([parent, nestedPhysical]);

        Assert.True(SetupRepair.Repair(setup));
        Assert.Equal(Guid.Empty, nestedPhysical.ParentId);
        Assert.Equal(new Vector2(1, 1), nestedPhysical.SizeInMeters);
        Assert.Equal(new Vector2(0.2f, 0.2f), nestedPhysical.OutputMappings[0].Quad[0]);

        Assert.False(SetupRepair.Repair(setup));
    }

    private static Vector2[] UnitQuad() => [Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)];
}
