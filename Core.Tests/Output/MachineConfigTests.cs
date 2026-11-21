using System;
using Newtonsoft.Json.Linq;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class MachineConfigTests
{
    [Fact]
    public void Bindings_RoundTrip()
    {
        var outputId = Guid.NewGuid();
        var config = new MachineConfig();
        config.Bind(new PlugBinding { OutputId = outputId, DisplayName = @"\\.\DISPLAY2", DisplayIndex = 1 });

        var restored = MachineConfig.ReadFromJson(JObject.Parse(config.ToJsonString()));

        var binding = Assert.Single(restored.Bindings);
        Assert.Equal(outputId, binding.OutputId);
        Assert.Equal(@"\\.\DISPLAY2", binding.DisplayName);
        Assert.Equal(1, binding.DisplayIndex);
    }

    [Fact]
    public void ActiveSetupName_RoundTrips_AndDefaultsToEmpty()
    {
        var config = new MachineConfig { ActiveSetupName = "Venue B" };

        var restored = MachineConfig.ReadFromJson(JObject.Parse(config.ToJsonString()));
        Assert.Equal("Venue B", restored.ActiveSetupName);

        var withoutName = MachineConfig.ReadFromJson(JObject.Parse(new MachineConfig().ToJsonString()));
        Assert.Equal(string.Empty, withoutName.ActiveSetupName);
    }

    [Fact]
    public void Bind_ReplacesExistingBindingForSameOutput()
    {
        var outputId = Guid.NewGuid();
        var config = new MachineConfig();
        config.Bind(new PlugBinding { OutputId = outputId, DisplayIndex = 0 });
        config.Bind(new PlugBinding { OutputId = outputId, DisplayIndex = 2 });

        var binding = Assert.Single(config.Bindings);
        Assert.Equal(2, binding.DisplayIndex);

        config.Unbind(outputId);
        Assert.Empty(config.Bindings);
        Assert.Null(config.FindBinding(outputId));
    }

    [Fact]
    public void MalformedAndFutureContent_LoadsTolerantly()
    {
        var config = MachineConfig.ReadFromJson(JObject.Parse("""
            {
              "Version": 99,
              "FutureSyncSettings": {},
              "Bindings": [ { "DisplayIndex": 1 }, 17 ]
            }
            """));

        var binding = Assert.Single(config.Bindings);
        Assert.Equal(1, binding.DisplayIndex);
        Assert.Equal(Guid.Empty, binding.OutputId);
    }

    [Fact]
    public void RemoveKindMismatchedBindings_DropsDisplayBindingOnLaserOutput()
    {
        var laserOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "L1", Kind = OutputDefinition.Kinds.Laser };
        var displayOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "P1", Kind = OutputDefinition.Kinds.Projector };
        var laserPlug = Guid.NewGuid();

        var config = new MachineConfig();
        config.Bind(new PlugBinding { OutputId = laserOutput.Id, DisplayIndex = 0 });          // stale: a display on a laser output
        config.Bind(new PlugBinding { OutputId = laserOutput.Id, Kind = PlugBinding.Kinds.Laser, PlugId = laserPlug });
        config.Bind(new PlugBinding { OutputId = displayOutput.Id, DisplayIndex = 0 });        // legitimate

        var removed = config.RemoveKindMismatchedBindings([laserOutput, displayOutput]);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(config.Bindings, b => b.OutputId == laserOutput.Id && !b.IsLaser);
        Assert.Contains(config.Bindings, b => b.OutputId == laserOutput.Id && b.IsLaser && b.PlugId == laserPlug);
        Assert.Contains(config.Bindings, b => b.OutputId == displayOutput.Id && !b.IsLaser);
    }

    [Fact]
    public void RemoveKindMismatchedBindings_DropsLaserBindingOnCanvasOutput()
    {
        var canvasOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "P1", Kind = OutputDefinition.Kinds.Projector };
        var config = new MachineConfig();
        config.Bind(new PlugBinding { OutputId = canvasOutput.Id, Kind = PlugBinding.Kinds.Laser, PlugId = Guid.NewGuid() });

        Assert.Equal(1, config.RemoveKindMismatchedBindings([canvasOutput]));
        Assert.Empty(config.Bindings);
    }

    [Fact]
    public void RemoveKindMismatchedBindings_LeavesMatchingPairsAlone()
    {
        var laserOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "L1", Kind = OutputDefinition.Kinds.Laser };
        var displayOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "P1", Kind = OutputDefinition.Kinds.Projector };
        var streamOutput = new OutputDefinition { Id = Guid.NewGuid(), Name = "P2", Kind = OutputDefinition.Kinds.Projector };
        var config = new MachineConfig();
        config.Bind(new PlugBinding { OutputId = laserOutput.Id, Kind = PlugBinding.Kinds.Laser, PlugId = Guid.NewGuid() });
        config.Bind(new PlugBinding { OutputId = displayOutput.Id, DisplayIndex = 1 });
        config.Bind(new PlugBinding { OutputId = streamOutput.Id, Kind = PlugBinding.Kinds.Stream, PlugId = Guid.NewGuid() });

        Assert.Equal(0, config.RemoveKindMismatchedBindings([laserOutput, displayOutput, streamOutput]));
        Assert.Equal(3, config.Bindings.Count);
    }
}
