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
        Assert.True(binding.IsFullscreen);
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
}
