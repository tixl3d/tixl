using SharpDX.Direct3D11;
using Utilities = T3.Core.Utils.Utilities;
using ArtNet.Packets;
using ArtNet.Sockets;
using System.Diagnostics;

namespace Lib.io.dmx.obsolete;

[Guid("faa3e182-96e6-45e7-b037-fb2acd88825b")]
internal sealed class ArtnetPixelOutput : Instance<ArtnetPixelOutput>, IStatusProvider
{
    [Output(Guid = "28e5a0a6-6dee-4771-b3ea-5c95862b34f8")]
    public readonly Slot<Command> Result = new();

    private readonly Stopwatch _artNetSendStopwatch = new Stopwatch();
    private long _lastArtNetSendTime;

    private string _currentIpAddressString; // Store the current IP address
    private bool _isReconnecting = false; // Add a flag to prevent continuous reconnection attempts
    private bool _connected;
    private IPAddress _newIpAddress;
    private ArtNetSocket _sender;
    private string _lastErrorMessage;
    private BufferWithViews _bufferWithViewsCpuAccess = new();

    public ArtnetPixelOutput()
    {
        Result.UpdateAction += Update;
        _artNetSendStopwatch.Start();
    }

    private void Update(EvaluationContext context)
    {
        var artNetSendRateHz = ArtNetSendRate.GetValue(context);
        var updateContinuously = true;
        var ipAddressString = IpAddress.GetValue(context).Trim();
        var reconnect = Reconnect.GetValue(context);

        // Check if the IP address has changed
        if (ipAddressString != _currentIpAddressString)
        {
            _currentIpAddressString = ipAddressString;
            reconnect = true; // Force a reconnect when the IP changes
        }


        // Handle Reconnection outside of try-catch to ensure it happens before data processing
        if (reconnect && !_isReconnecting) // Only try to reconnect if requested and not already reconnecting
        {
            _isReconnecting = true; // Set the flag to prevent concurrent reconnection attempts
            Reconnect.SetTypedInputValue(false); // Reset the reconnect trigger

            if (TryGetValidAddress(ipAddressString, out var error, out _newIpAddress))
            {
                if (_connected)
                {
                    CloseArtNet(); // Close existing connection before attempting a new one
                }

                _connected = TryConnectArtnet(_newIpAddress);

                if (!_connected)
                {
                    _lastErrorMessage = $"Failed to connect to {ipAddressString}: {error}"; // Store the error message
                    Log.Error(_lastErrorMessage, this);
                }
            }
            else
            {
                _lastErrorMessage = error;
                Log.Error(_lastErrorMessage, this);
                _connected = false;
            }

            _isReconnecting = false; // Reset the flag after reconnection attempt
        }


        try
        {
            var pointBuffer = Points.GetValue(context);

            if (pointBuffer == null)
            {
                Log.Warning("Point buffer is null. Skipping Art-Net send.", this);
                Result.DirtyFlag.Trigger = DirtyFlagTrigger.None;
                return;
            }

            var d3DDevice = ResourceManager.Device;
            var immediateContext = d3DDevice.ImmediateContext;

            if (updateContinuously
                || _bufferWithViewsCpuAccess == null
                || _bufferWithViewsCpuAccess.Buffer == null
                || _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes != pointBuffer.Buffer.Description.SizeInBytes
                || _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride != pointBuffer.Buffer.Description.StructureByteStride
               )
            {
                try
                {
                    if (_bufferWithViewsCpuAccess != null)
                        Utilities.Dispose(ref _bufferWithViewsCpuAccess.Buffer);

                    _bufferWithViewsCpuAccess ??= new BufferWithViews();

                    if (_bufferWithViewsCpuAccess.Buffer == null ||
                        _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes != pointBuffer.Buffer.Description.SizeInBytes)
                    {
                        _bufferWithViewsCpuAccess.Buffer?.Dispose();
                        var bufferDesc = new BufferDescription
                        {
                            Usage = ResourceUsage.Default,
                            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                            SizeInBytes = pointBuffer.Buffer.Description.SizeInBytes,
                            OptionFlags = ResourceOptionFlags.BufferStructured,
                            StructureByteStride = pointBuffer.Buffer.Description.StructureByteStride,
                            CpuAccessFlags = CpuAccessFlags.Read
                        };
                        _bufferWithViewsCpuAccess.Buffer = new Buffer(ResourceManager.Device, bufferDesc);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Failed to setup structured buffer " + e.Message, this);
                    return;
                }

                ResourceManager.CreateStructuredBufferSrv(_bufferWithViewsCpuAccess.Buffer, ref _bufferWithViewsCpuAccess.Srv);
                immediateContext.CopyResource(pointBuffer.Buffer, _bufferWithViewsCpuAccess.Buffer);
            }

            var sourceDataBox = immediateContext.MapSubresource(_bufferWithViewsCpuAccess.Buffer, 0, MapMode.Read, MapFlags.None, out var sourceStream);

            using (sourceStream)
            {
                var elementCount = _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes / _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride;
                var points = sourceStream.ReadRange<Point>(elementCount);

                var universes = new Dictionary<int, List<byte>>();

                foreach (var point in points)
                {
                    var factorR = (point.Color.X - 0f) / (1f - 0f);
                    var vR = (byte)Math.Round(factorR * 255.0f);
                    var factorG = (point.Color.Y - 0.0f) / (1.0f - 0.0f);
                    var vG = (byte)Math.Round(factorG * 255.0f);
                    var factorB = (point.Color.Z - 0.0f) / (1.0f - 0.0f);
                    var vB = (byte)Math.Round(factorB * 255.0f);

                    var startUniverse = (int)point.F2;

                    if (!universes.ContainsKey(startUniverse))
                    {
                        universes[startUniverse] = new List<byte>();
                    }

                    universes[startUniverse].Add(vR);
                    universes[startUniverse].Add(vG);
                    universes[startUniverse].Add(vB);
                }

                var shouldSend = false;
                if (artNetSendRateHz > 0)
                {
                    var currentTime = _artNetSendStopwatch.ElapsedMilliseconds;
                    var elapsedMs = currentTime - _lastArtNetSendTime;
                    var targetSendIntervalMs = 1000.0 / artNetSendRateHz;

                    if (elapsedMs >= targetSendIntervalMs)
                    {
                        shouldSend = true;
                        _lastArtNetSendTime = currentTime;
                    }
                }
                else
                {
                    shouldSend = true;
                }

                if (shouldSend && _connected && _sender != null) // Only send if connected
                {
                    SendArtNetData(universes);
                }
                else
                {
                    Result.DirtyFlag.Trigger = DirtyFlagTrigger.None;
                }

                Result.Value = new Command();
            }

            immediateContext.UnmapSubresource(_bufferWithViewsCpuAccess.Buffer, 0);
            Result.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        }
        catch (Exception e)
        {
             Log.Error($"Error during data processing: {e.Message}", this);
            // Consider setting a status message here to indicate an error
        }
        finally
        {
            // Removed HandleReconnection from here. It is handled before data processing
        }
    }

    private void SendArtNetData(Dictionary<int, List<byte>> universes)
    {
        if (_sender == null)
        {
            Log.Warning("Art-Net sender is not initialized. Skipping data send.", this);
            return; // Exit if sender is not initialized
        }

        foreach (var universe in universes)
        {
            var universeIndex = universe.Key;
            var dmxData = universe.Value;

            for (var i = 0; i < dmxData.Count; i += 512)
            {
                var currentChunkSize = Math.Min(512, dmxData.Count - i);
                var chunk = dmxData.GetRange(i, currentChunkSize).ToArray();

                if (chunk.Length < 512)
                {
                    var paddedChunk = new byte[512];
                    Array.Copy(chunk, paddedChunk, chunk.Length);
                    chunk = paddedChunk;
                }

                var dmxPacket = new ArtNetDmxPacket
                {
                    DmxData = chunk,
                    Universe = (short)universeIndex
                };

                try
                {
                    _sender.Send(dmxPacket);
                }
                catch (Exception sendEx)
                {
                    Log.Error($"Failed to send Art-Net packet: {sendEx.Message}", this);
                    _connected = false;
                    // Optionally, attempt a reconnect here or set a flag to trigger a reconnect
                    break; // Exit the loop to prevent further sending attempts
                }


                universeIndex++;

                if (universeIndex > 32767)
                {
                    Log.Warning("Art-Net universe limit reached, wrapping back to universe 0.", this);
                    universeIndex = 0;
                }
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            CloseArtNet();
            _artNetSendStopwatch.Stop();
            Console.WriteLine(@"dispose");
        }
    }

    private void CloseArtNet()
    {
        if (_connected && _sender != null)
        {
            try
            {
                _sender.Close();
                _sender.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Error while closing Art-Net socket: {e.Message}", this);
            }
            finally
            {
                _sender = null;
                _connected = false;
            }
        }
    }

    private static bool TryGetValidAddress(string ipAddressString, out string error, out IPAddress ipAddress)
    {
        if (IPAddress.TryParse(ipAddressString, out ipAddress))
        {
            error = null;
            return true;
        }

        error = $"Failed to parse ip: {ipAddressString}";
        return false;
    }

    private bool TryConnectArtnet(IPAddress ipAddress)
    {
        if (ipAddress == null)
        {
            _lastErrorMessage = "IP Address is null, cannot connect.";
            Log.Error(_lastErrorMessage, this);
            return false;
        }

        try
        {
            _sender = new ArtNetSocket();
            _sender.EnableBroadcast = true;
            _sender.Open(ipAddress, IPAddress.Parse("255.255.255.0"));
            _currentIpAddressString = ipAddress.ToString();
            return true;
        }
        catch (Exception e)
        {
            _lastErrorMessage = $"Failed to connect to {ipAddress}: {e.Message}";
            Log.Error(_lastErrorMessage, this);
            return false;
        }
    }


    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;


    [Input(Guid = "4598c09c-f463-426f-ab1b-c9be10c0ff75")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();

    [Input(Guid = "508ebec1-cd2b-49bf-b77d-787fa3a8c138")]
    public readonly InputSlot<string> IpAddress = new InputSlot<string>();

    [Input(Guid = "db583506-fd7a-4109-9f37-489c27f237b8")]
    public readonly InputSlot<bool> Reconnect = new InputSlot<bool>();

    [Input(Guid = "d2e1f0c3-a4b5-4c6d-8e7f-9a0b1c2d3e4f")]
    public readonly InputSlot<float> ArtNetSendRate = new InputSlot<float>(44.0f);
}
