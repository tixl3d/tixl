using T3.Core.DataTypes;
using T3.Core.Video;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The remote-video bus is the render-node half of boygroup input replication: the master announces a
/// source per input, and only an input whose key matches is driven by it. Exercises the registry, the
/// per-frame pump and the release path without a GPU or a real transport.
/// </summary>
[Collection("RemoteVideoInputBus")]
public class RemoteVideoInputBusTests
{
    [Fact]
    public void RemoteSourceDrivesOnlyTheMatchingInputAndIsReleased()
    {
        var provider = new FakeProvider();
        var matching = new FakeInput("test-package/op-a");
        var other = new FakeInput("test-package/op-b");

        RemoteVideoInputRegistry.Register(provider);
        RemoteVideoInputBus.RegisterInput(matching);
        RemoteVideoInputBus.RegisterInput(other);

        try
        {
            RemoteVideoInputBus.SetRemoteSources([new RemoteVideoSource(matching.RelayKey, "MACHINE (camera)", "starting")]);
            RemoteVideoInputBus.Pump();

            Assert.True(matching.RemoteActive);
            Assert.False(other.RemoteActive);
            Assert.False(string.IsNullOrEmpty(matching.LastStatus));
            Assert.Equal(0, matching.LastFrameCounter);
            Assert.NotNull(provider.Receiver);

            RemoteVideoInputBus.SetRemoteSources([]);
            RemoteVideoInputBus.Pump();

            Assert.False(matching.RemoteActive);
            Assert.True(matching.Ended);
            Assert.Equal(0, RemoteVideoInputBus.RemoteSourceCount);
        }
        finally
        {
            RemoteVideoInputBus.UnregisterInput(matching);
            RemoteVideoInputBus.UnregisterInput(other);
            RemoteVideoInputBus.ClearRemoteSources();
            RemoteVideoInputRegistry.Unregister(provider);
        }
    }

    private sealed class FakeInput(string key) : IRemoteVideoInput
    {
        public string RelayKey => key;
        public string RelayDisplayName => "Fake";
        public Texture2D? RelayTexture => null;
        public string RelayStatus => "fake status";

        public bool RemoteActive { get; private set; }
        public bool Ended { get; private set; }
        public string LastStatus { get; private set; } = "";
        public int LastFrameCounter { get; private set; } = -1;

        public void ApplyRemoteFrame(Texture2D? texture, int frameCounter, string status)
        {
            RemoteActive = true;
            Ended = false;
            LastStatus = status;
            LastFrameCounter = frameCounter;
        }

        public void EndRemoteFrames()
        {
            RemoteActive = false;
            Ended = true;
        }
    }

    private sealed class FakeProvider : IRemoteVideoInputProvider
    {
        public string Kind => RemoteVideoInputBus.PreferredTransportKind;
        public bool IsAvailable => true;
        public string? UnavailableMessage => null;
        public FakeReceiver? Receiver { get; private set; }

        public IRemoteVideoInputReceiver? CreateReceiver(string sourceName)
        {
            Receiver = new FakeReceiver();
            return Receiver;
        }
    }

    private sealed class FakeReceiver : IRemoteVideoInputReceiver
    {
        public bool IsConnected => false;
        public string? StatusMessage => "waiting to connect";
        public Texture2D? Update() => null;
        public void Dispose()
        {
        }
    }
}
