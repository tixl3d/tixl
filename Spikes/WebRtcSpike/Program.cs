using System.Diagnostics;
using System.Text;
using DataChannelDotnet;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Data;
using DataChannelDotnet.Impl;

namespace T3.Spikes.WebRtcSpike;

/// <summary>
/// Checks that libdatachannel (through DataChannelDotnet) can carry what a collab session needs:
/// a reliable ordered channel, an unreliable unordered channel, and a negotiated H.264 video track.
///
///   WebRtcSpike loopback                  two peers in this process
///   WebRtcSpike host [stunUrl]            prints an offer; paste it into "join" on the other machine
///   WebRtcSpike join [stunUrl]            reads the offer, prints an answer for "host"
///
/// Signaling is copy and paste so the cross-network test needs no server.
/// </summary>
internal static class Program
{
    private const int PingCount = 300;

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "loopback";
        var stun = args.Length > 1 ? args[1] : null;

        try
        {
            return mode switch
                       {
                           "loopback" => RunLoopback(),
                           "negotiated" => RunNegotiated(),
                           "host" => RunHost(stun),
                           "join" => RunJoin(stun),
                           _ => Usage(),
                       };
        }
        catch (Exception e)
        {
            Console.WriteLine($"FAILED: {e}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("usage: WebRtcSpike loopback | negotiated | host [stunUrl] | join [stunUrl]");
        return 2;
    }

    // ---------------------------------------------------------------------------------------
    // Explicit negotiation, as the collab transport drives it
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The sequence the editor uses: auto-negotiation off, one offer that already contains the data
    /// channel and the video track, answer after the remote description. Also proves that a frame
    /// larger than one SCTP message survives the chunked data-channel stream.
    /// </summary>
    private static int RunNegotiated()
    {
        using var host = new RtcPeerConnection(new RtcPeerConfiguration { DisableAutoNegotiation = true, EnableIceTcp = true });
        using var client = new RtcPeerConnection(new RtcPeerConfiguration { DisableAutoNegotiation = true, EnableIceTcp = true });

        // Signaling stays in-process: this is only about the order of the libdatachannel calls.
        host.OnLocalDescriptionSafe += (_, description) =>
                                           {
                                               Console.WriteLine($"host: local {description.Type}");
                                               if (description.Type == RtcDescriptionType.Offer)
                                               {
                                                   client.SetRemoteDescription(description);
                                                   client.SetLocalDescription(RtcDescriptionType.Answer);
                                               }
                                           };
        client.OnLocalDescriptionSafe += (_, description) =>
                                             {
                                                 Console.WriteLine($"client: local {description.Type}");
                                                 if (description.Type == RtcDescriptionType.Answer)
                                                     host.SetRemoteDescription(description);
                                             };
        client.OnCandidateSafe += (_, candidate) => host.AddRemoteCandidate(candidate);
        host.OnCandidateSafe += (_, candidate) => client.AddRemoteCandidate(candidate);

        var trackSeen = new ManualResetEventSlim();
        var rtpReceived = 0;
        client.OnTrack += (_, track) =>
                              {
                                  Console.WriteLine($"client: track '{track.Mid}' {track.Direction}");
                                  track.OnBinaryReceivedSafe += (_, _) => Interlocked.Increment(ref rtpReceived);
                                  trackSeen.Set();
                              };

        var channel = host.CreateDataChannel(new RtcCreateDataChannelArgs
                                                  {
                                                      Label = "collab",
                                                      Protocol = RtcDataChannelProtocol.Binary,
                                                  });
        var track = host.CreateTrack(new RtcCreateTrackArgs
                                         {
                                             Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                                             Codec = rtcCodec.RTC_CODEC_H264,
                                             PayloadType = 96,
                                             Ssrc = 7,
                                             Mid = "video",
                                             Name = "video",
                                             Msid = "tixl",
                                             TrackId = "video",
                                         });
        track.AddH264Packetizer(new RtcPacketizerInitArgs
                                    {
                                        Ssrc = 7,
                                        Cname = "spike",
                                        PayloadType = 96,
                                        Clockrate = 90_000,
                                        MaxFragmentSize = 1200,
                                        NalUnitSeparator = rtcNalUnitSeparator.RTC_NAL_SEPARATOR_LONG_START_SEQUENCE,
                                    });

        var opened = new ManualResetEventSlim();
        var echoed = 0;
        IRtcDataChannel? remoteChannel = null;
        client.OnDataChannel += (_, remote) =>
                                    {
                                        remoteChannel = remote;
                                        remote.OnOpen += _ => opened.Set();
                                        Console.WriteLine($"client: received channel '{remote.Label}'");
                                    };
        channel.OnOpen += _ => opened.Set();

        host.SetLocalDescription(RtcDescriptionType.Offer);

        if (!opened.Wait(TimeSpan.FromSeconds(15)))
        {
            Console.WriteLine($"FAILED: channel did not open (host {host.ConnectionState}, client {client.ConnectionState}, client signaling {client.SignalingState})");
            return 1;
        }

        Console.WriteLine("data channel open");

        // Round-trip a 300 KiB blob: larger than a single SCTP message on every stack.
        const int blobSize = 300 * 1024;
        var blob = new byte[blobSize];
        Random.Shared.NextBytes(blob);
        var received = new byte[blobSize];
        var receivedCount = 0;
        var complete = new ManualResetEventSlim();
        remoteChannel!.OnBinaryReceivedSafe += (_, message) =>
                                                    {
                                                        var count = message.Data.Length;
                                                        if (receivedCount + count > blobSize)
                                                        {
                                                            Console.WriteLine($"FAILED: received too much ({receivedCount + count})");
                                                            return;
                                                        }

                                                        message.Data.CopyTo(received.AsSpan(receivedCount));
                                                        receivedCount += count;
                                                        Interlocked.Increment(ref echoed);
                                                        if (receivedCount == blobSize)
                                                            complete.Set();
                                                    };

        var offset = 0;
        while (offset < blobSize)
        {
            var count = Math.Min(48 * 1024, blobSize - offset);
            channel.Send(blob.AsSpan(offset, count));
            offset += count;
        }

        var blobOk = complete.Wait(TimeSpan.FromSeconds(10)) && received.AsSpan().SequenceEqual(blob);
        Console.WriteLine(blobOk ? "300 KiB blob reassembled: yes" : $"300 KiB blob reassembled: NO ({receivedCount} bytes)");

        // A fake Annex-B access unit; the packetizer must produce RTP without a decoder.
        track.Timestamp = 0;
        track.Write([0, 0, 0, 1, 0x67, 0x42, 0x00, 0x0A, 0x8C, 0x8D, 0x40,
                     0, 0, 0, 1, 0x68, 0xCE, 0x3C, 0x80,
                     0, 0, 0, 1, 0x65, 0x88, 0x84, 0x00]);

        var trackOk = trackSeen.Wait(TimeSpan.FromSeconds(5));
        Thread.Sleep(500);
        Console.WriteLine(trackOk ? "video track negotiated: yes" : "video track negotiated: NO");
        Console.WriteLine(rtpReceived > 0 ? $"video RTP packets received: {rtpReceived}" : "video RTP packets received: NONE");

        return blobOk && trackOk && rtpReceived > 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------
    // Loopback
    // ---------------------------------------------------------------------------------------

    private static int RunLoopback()
    {
        using var host = new RtcPeerConnection(Configure(null));
        using var client = new RtcPeerConnection(Configure(null));

        host.OnCandidateSafe += (_, candidate) => client.AddRemoteCandidate(candidate);
        client.OnCandidateSafe += (_, candidate) => host.AddRemoteCandidate(candidate);
        host.OnLocalDescriptionSafe += (_, description) => client.SetRemoteDescription(description);
        client.OnLocalDescriptionSafe += (_, description) => host.SetRemoteDescription(description);

        var clientTrackSeen = new ManualResetEventSlim();
        client.OnTrack += (_, track) =>
                              {
                                  Console.WriteLine($"client: track '{track.Mid}' direction {track.Direction}");
                                  clientTrackSeen.Set();
                              };

        var echo = new EchoSide();
        client.OnDataChannel += (_, channel) => echo.Attach(channel);

        var pinger = new Pinger();
        var reliable = host.CreateDataChannel(new RtcCreateDataChannelArgs { Label = "reliable", Protocol = RtcDataChannelProtocol.Binary });
        var fast = host.CreateDataChannel(new RtcCreateDataChannelArgs
                                              {
                                                  Label = "fast",
                                                  Protocol = RtcDataChannelProtocol.Binary,
                                                  Unordered = true,
                                                  Unreliable = true,
                                                  MaxRetransmits = 0,
                                              });
        pinger.Attach(reliable);
        pinger.Attach(fast);

        var track = host.CreateTrack(new RtcCreateTrackArgs
                                         {
                                             Direction = rtcDirection.RTC_DIRECTION_SENDONLY,
                                             Codec = rtcCodec.RTC_CODEC_H264,
                                             PayloadType = 96,
                                             Ssrc = 42,
                                             Mid = "video",
                                             Name = "video",
                                             Msid = "stream",
                                             TrackId = "video",
                                         });
        Console.WriteLine($"host: created {track.Mid} track ({track.Direction})");

        if (!pinger.WaitUntilOpen(TimeSpan.FromSeconds(15)))
        {
            Console.WriteLine($"FAILED: channels did not open (host {host.ConnectionState}, client {client.ConnectionState})");
            return 1;
        }

        Console.WriteLine($"connected. candidate pair: {DescribePair(host)}");
        pinger.Measure(reliable, PingCount);
        pinger.Measure(fast, PingCount);

        Console.WriteLine(clientTrackSeen.Wait(TimeSpan.FromSeconds(5))
                              ? "video track negotiated: yes"
                              : "video track negotiated: NO (client never saw the track)");
        return 0;
    }

    // ---------------------------------------------------------------------------------------
    // Cross-machine
    // ---------------------------------------------------------------------------------------

    private static int RunHost(string? stun)
    {
        using var host = new RtcPeerConnection(Configure(stun));
        var pinger = new Pinger();
        var reliable = host.CreateDataChannel(new RtcCreateDataChannelArgs { Label = "reliable", Protocol = RtcDataChannelProtocol.Binary });
        var fast = host.CreateDataChannel(new RtcCreateDataChannelArgs
                                              {
                                                  Label = "fast",
                                                  Protocol = RtcDataChannelProtocol.Binary,
                                                  Unordered = true,
                                                  Unreliable = true,
                                                  MaxRetransmits = 0,
                                              });
        pinger.Attach(reliable);
        pinger.Attach(fast);

        WaitForGathering(host);
        Console.WriteLine("Send this offer to the other machine (one line):");
        Console.WriteLine(Encode(host.LocalDescription, host.LocalDescriptionType));
        Console.WriteLine();
        Console.WriteLine("Paste the answer here:");

        var answer = Decode(Console.ReadLine() ?? "");
        host.SetRemoteDescription(answer);

        if (!pinger.WaitUntilOpen(TimeSpan.FromSeconds(30)))
        {
            Console.WriteLine($"FAILED: channels did not open (state {host.ConnectionState}, ice {host.IceState})");
            return 1;
        }

        Console.WriteLine($"connected. candidate pair: {DescribePair(host)}");
        pinger.Measure(reliable, PingCount);
        pinger.Measure(fast, PingCount);
        return 0;
    }

    private static int RunJoin(string? stun)
    {
        using var client = new RtcPeerConnection(Configure(stun));
        var echo = new EchoSide();
        client.OnDataChannel += (_, channel) => echo.Attach(channel);

        Console.WriteLine("Paste the offer here:");
        var offer = Decode(Console.ReadLine() ?? "");
        client.SetRemoteDescription(offer);

        WaitForGathering(client);
        Console.WriteLine("Send this answer back (one line):");
        Console.WriteLine(Encode(client.LocalDescription, client.LocalDescriptionType));
        Console.WriteLine();
        Console.WriteLine("Echoing until you press Enter.");
        Console.ReadLine();
        return 0;
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static RtcPeerConfiguration Configure(string? stun)
    {
        var configuration = new RtcPeerConfiguration();
        if (!string.IsNullOrWhiteSpace(stun))
            configuration.IceServers = [stun];

        return configuration;
    }

    private static void WaitForGathering(RtcPeerConnection peer)
    {
        var timeout = Stopwatch.StartNew();
        while (peer.GatheringState != rtcGatheringState.RTC_GATHERING_COMPLETE)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException("ICE gathering did not complete");

            Thread.Sleep(20);
        }
    }

    private static string DescribePair(RtcPeerConnection peer)
    {
        return peer.TryGetSelectedCandidatePair(out var pair)
                   ? $"local [{pair.LocalCandidate}] remote [{pair.RemoteCandidate}]"
                   : "unknown";
    }

    private static string Encode(string? sdp, RtcDescriptionType type)
    {
        var text = $"{type}\n{sdp}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    private static RtcDescription Decode(string blob)
    {
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(blob.Trim()));
        var split = text.IndexOf('\n');
        var type = Enum.Parse<RtcDescriptionType>(text[..split]);
        return new RtcDescription { Sdp = text[(split + 1)..], Type = type };
    }

    /// <summary>The responding end: sends every binary message straight back.</summary>
    private sealed class EchoSide
    {
        public void Attach(IRtcDataChannel channel)
        {
            if (channel is not RtcDataChannel impl)
                return;

            Console.WriteLine($"echo: channel '{impl.Label}' attached");
            impl.OnBinaryReceivedSafe += (source, message) => source.Send(message.Data);
        }
    }

    /// <summary>The measuring end: sends a timestamp, waits for it to come back, records the round trip.</summary>
    private sealed class Pinger
    {
        public void Attach(IRtcDataChannel channel)
        {
            if (channel is not RtcDataChannel impl)
                return;

            _channelCount++;
            impl.OnOpen += _ => Interlocked.Increment(ref _openCount);
            impl.OnBinaryReceivedSafe += (source, message) =>
                                             {
                                                 if (message.Data.Length < 8)
                                                     return;

                                                 var sent = BitConverter.ToInt64(message.Data);
                                                 var rttMs = (Stopwatch.GetTimestamp() - sent) * 1000.0 / Stopwatch.Frequency;
                                                 lock (_gate)
                                                 {
                                                     _samples.Add(rttMs);
                                                 }

                                                 _reply.Set();
                                             };
        }

        public bool WaitUntilOpen(TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (_openCount < _channelCount)
            {
                if (clock.Elapsed > timeout)
                    return false;

                Thread.Sleep(10);
            }

            return true;
        }

        public void Measure(IRtcDataChannel channel, int count)
        {
            lock (_gate)
            {
                _samples.Clear();
            }

            var lost = 0;
            Span<byte> payload = stackalloc byte[64];
            for (var i = 0; i < count; i++)
            {
                _reply.Reset();
                BitConverter.TryWriteBytes(payload, Stopwatch.GetTimestamp());
                channel.Send(payload);
                if (!_reply.Wait(TimeSpan.FromMilliseconds(500)))
                    lost++;

                Thread.Sleep(5);
            }

            double[] sorted;
            lock (_gate)
            {
                sorted = _samples.OrderBy(v => v).ToArray();
            }

            if (sorted.Length == 0)
            {
                Console.WriteLine($"{channel.Label,-9} no replies, lost {lost}/{count}");
                return;
            }

            Console.WriteLine($"{channel.Label,-9} rtt ms: min {sorted[0]:0.00}  median {sorted[sorted.Length / 2]:0.00}  "
                              + $"p99 {sorted[(int)(sorted.Length * 0.99)]:0.00}  max {sorted[^1]:0.00}  lost {lost}/{count}");
        }

        private readonly object _gate = new();
        private readonly List<double> _samples = [];
        private readonly ManualResetEventSlim _reply = new();
        private int _channelCount;
        private int _openCount;
    }
}
