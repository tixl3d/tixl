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
        config.Bind(new DeviceBinding { OutputId = outputId, DisplayName = @"\\.\DISPLAY2", DisplayIndex = 1 });

        var restored = MachineConfig.ReadFromJson(JObject.Parse(config.ToJsonString()));

        var binding = Assert.Single(restored.Bindings);
        Assert.Equal(outputId, binding.OutputId);
        Assert.Equal(@"\\.\DISPLAY2", binding.DisplayName);
        Assert.Equal(1, binding.DisplayIndex);
        Assert.True(binding.Fullscreen);
    }

    [Fact]
    public void Bind_ReplacesExistingBindingForSameOutput()
    {
        var outputId = Guid.NewGuid();
        var config = new MachineConfig();
        config.Bind(new DeviceBinding { OutputId = outputId, DisplayIndex = 0 });
        config.Bind(new DeviceBinding { OutputId = outputId, DisplayIndex = 2 });

        var binding = Assert.Single(config.Bindings);
        Assert.Equal(2, binding.DisplayIndex);

        config.Unbind(outputId);
        Assert.Empty(config.Bindings);
        Assert.Null(config.TryGetBinding(outputId));
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
