using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Editor.IntegrationTests;

[Collection("Editor")]
public sealed class DmxLifecycleTests(EditorFixture editor)
{
    [Theory]
    [InlineData("ArtnetOutput", 6454)]
    [InlineData("SacnOutput", 5568)]
    [InlineData("SacnInput", 5568)]
    public void DeleteActiveOperator_ReleasesPort_AndAllowsRecreation(string symbolName, int port)
    {
        editor.OpenPlayground();
        var client = editor.Client;
        Assert.True(CanBind(port), $"Close other DMX applications using UDP port {port} before running this test.");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Guid? child = client.AddOp(symbolName);
            try
            {
                client.SetInput(child.Value, "LocalIpAddress", JToken.FromObject("127.0.0.1"));
                if (symbolName == "SacnInput")
                {
                    client.SetInput(child.Value, "Active", JToken.FromObject(true));
                }
                else
                {
                    client.SetInput(child.Value, "TargetIpAddress", JToken.FromObject("127.0.0.1"));
                    client.SetInput(child.Value, "SendUnicast", JToken.FromObject(true));
                    client.SetInput(child.Value, "SendTrigger", JToken.FromObject(true));
                    client.SetInput(child.Value, symbolName == "SacnOutput" ? "DiscoverSources" : "PrintArtnetPoll",
                                    JToken.FromObject(true));
                }

                client.Call("getOutput", new { childId = child.Value, update = true }).Require("evaluate DMX operator");
                Assert.True(SpinWait.SpinUntil(() => !CanBind(port), TimeSpan.FromSeconds(5)),
                            $"{symbolName} did not bind UDP port {port}.");

                client.DeleteOp(child.Value);
                child = null;
                Assert.True(SpinWait.SpinUntil(() => CanBind(port), TimeSpan.FromSeconds(5)),
                            $"Deleting {symbolName} did not release UDP port {port}.");
            }
            finally
            {
                if (child.HasValue)
                    client.DeleteOp(child.Value);
            }
        }
    }

    private static bool CanBind(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ExclusiveAddressUse = true;
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            return false;
        }
    }
}
