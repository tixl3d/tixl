using T3.Core.DataTypes;
using T3.Core.Video;
using Xunit;

namespace Core.Tests;

[Collection("RemoteVideoInputBus")]
public class PeerCameraTests
{
    [Theory]
    [InlineData("BisonCam, NB Pro @ ARTON-LINUX", "BisonCam, NB Pro", "ARTON-LINUX")]
    [InlineData("Cam @ A @ B", "Cam @ A", "B")]
    public void ReferenceRoundTrips(string reference, string camera, string machine)
    {
        Assert.True(PeerCameraDirectory.TryParse(reference, out var parsedCamera, out var parsedMachine));
        Assert.Equal(camera, parsedCamera);
        Assert.Equal(machine, parsedMachine);
        Assert.Equal(reference, PeerCameraDirectory.Format(camera, machine));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Integrated Webcam")]
    [InlineData(" @ MACHINE")]
    [InlineData("Camera @ ")]
    public void PlainDeviceNamesAreNotReferences(string? value)
    {
        Assert.False(PeerCameraDirectory.TryParse(value, out _, out _));
    }

    [Fact]
    public void DirectoryListsAnnouncedCamerasInMachineOrderAndForgetsMachines()
    {
        PeerCameraDirectory.Clear();
        try
        {
            PeerCameraDirectory.SetMachine("PC-B", ["Cam 2"]);
            PeerCameraDirectory.SetMachine("PC-A", ["Cam 1", "Cam 3"]);

            var references = new List<string>();
            PeerCameraDirectory.AppendReferences(references);
            Assert.Equal(["Cam 1 @ PC-A", "Cam 3 @ PC-A", "Cam 2 @ PC-B"], references);

            var version = PeerCameraDirectory.Version;
            PeerCameraDirectory.SetMachine("PC-A", ["Cam 1", "Cam 3"]);
            Assert.Equal(version, PeerCameraDirectory.Version);

            PeerCameraDirectory.RemoveMachine("PC-B");
            Assert.False(PeerCameraDirectory.IsMachineKnown("PC-B"));
            Assert.True(PeerCameraDirectory.IsMachineKnown("PC-A"));
        }
        finally
        {
            PeerCameraDirectory.Clear();
        }
    }

    [Fact]
    public void WantsLapseWhenTheInputStopsRefreshingThem()
    {
        PeerCameraBus.SetWant("input-a", "PC-A", "Cam 1");
        PeerCameraBus.SetWant("input-b", "PC-B", "Cam 2");
        try
        {
            var wants = new List<PeerCameraBus.Want>();
            PeerCameraBus.CollectWants(wants);
            Assert.Contains(new PeerCameraBus.Want("input-a", "PC-A", "Cam 1"), wants);
            Assert.Contains(new PeerCameraBus.Want("input-b", "PC-B", "Cam 2"), wants);

            PeerCameraBus.ClearWant("input-a");
            wants.Clear();
            PeerCameraBus.CollectWants(wants);
            Assert.DoesNotContain(wants, w => w.Key == "input-a");
        }
        finally
        {
            PeerCameraBus.ClearWant("input-a");
            PeerCameraBus.ClearWant("input-b");
        }
    }

    [Fact]
    public void PeerSourceDrivesAnInputAndTheMastersSourceWinsOverIt()
    {
        var provider = new FakeProvider();
        var input = new FakeInput("test-package/camera-op");
        RemoteVideoInputRegistry.Register(provider);
        RemoteVideoInputBus.RegisterInput(input);

        try
        {
            RemoteVideoInputBus.SetPeerSources([new RemoteVideoSource(input.RelayKey, "PC-A (cam)", "starting")]);
            RemoteVideoInputBus.Pump();
            Assert.True(input.RemoteActive);
            Assert.Equal(0, RemoteVideoInputBus.RemoteSourceCount);

            RemoteVideoInputBus.SetRemoteSources([new RemoteVideoSource(input.RelayKey, "MASTER (cam)", "starting")]);
            RemoteVideoInputBus.ClearRemoteSources();
            RemoteVideoInputBus.Pump();
            Assert.True(input.RemoteActive);

            RemoteVideoInputBus.SetPeerSources([]);
            Assert.False(input.RemoteActive);
            Assert.True(input.Ended);
        }
        finally
        {
            RemoteVideoInputBus.UnregisterInput(input);
            RemoteVideoInputBus.SetPeerSources([]);
            RemoteVideoInputBus.ClearRemoteSources();
            RemoteVideoInputRegistry.Unregister(provider);
        }
    }

    private sealed class FakeInput(string key) : IRemoteVideoInput
    {
        public string RelayKey => key;
        public string RelayDisplayName => "Fake";
        public Texture2D? RelayTexture => null;
        public string RelayStatus => "";
        public bool RemoteActive { get; private set; }
        public bool Ended { get; private set; }

        public void ApplyRemoteFrame(Texture2D? texture, int frameCounter, string status)
        {
            RemoteActive = true;
            Ended = false;
        }

        public void EndRemoteFrames()
        {
            RemoteActive = false;
            Ended = true;
        }
    }

    private sealed class FakeProvider : IRemoteVideoInputProvider
    {
        public string Kind => "OMT";
        public bool IsAvailable => true;
        public string? UnavailableMessage => null;
        public IRemoteVideoInputReceiver? CreateReceiver(string sourceName) => new FakeReceiver();
    }

    private sealed class FakeReceiver : IRemoteVideoInputReceiver
    {
        public Texture2D? Update() => null;
        public bool IsConnected => false;
        public string? StatusMessage => null;

        public void Dispose()
        {
        }
    }
}
