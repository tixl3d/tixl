// Headless tests for the collab/boygroup/failover network layer. Runs anywhere .NET 10 runs.
//
//   CollabHeadless                 → runs every in-process test, prints PASS/FAIL per test, exit 0/1
//   CollabHeadless failover-node <showId> <priority> <heartbeatMs>
//                                  → failover election participant used by TestFailoverElection
//
// These exercise exactly the code that runs inside the editor: the same sources are compile-included.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using T3.Core.Show;
using T3.Editor.Collab;
using T3.Editor.Show;

namespace T3.CollabHeadless;

internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "failover-node")
            return RunFailoverNode(args[1], int.Parse(args[2]), int.Parse(args[3]));

        Run("framing.roundtrip", TestFramingRoundTrip);
        Run("framing.maxsize", TestFrameSizeLimit);
        Run("json.decode", TestJsonDecode);
        Run("json.token-tolerance", TestJsonTokenTolerance);
        Run("presence.roundtrip", TestPresencePacket);
        Run("auth.mac", TestAuth);
        Run("heartbeat.answers-not-answered", TestHeartbeatAnswersAreNotAnswered);
        Run("collab.host-port-release", TestHostPortReleasedOnFailure);
        Run("deploy.inventory", TestDeployInventoryDiff);
        Run("deploy.transfer", TestDeployFileTransfer);
        Run("deploy.pathsafety", TestDeployPathSafety);
        Run("deploy.mtime-tolerance", TestDeployMtimeTolerance);
        Run("deploy.hash-over-mtime", TestDeployHashOverMtime);
        Run("boygroup.discovery", TestBoygroupDiscovery);
        Run("failover.election", TestFailoverElection);
        Run("failover.no-dual-active", TestFailoverNoDualActive);

        Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : $"{_failures} TEST(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception e)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {e.Message}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    // ---------------------------------------------------------------------------------------
    // Framing
    // ---------------------------------------------------------------------------------------

    private static void TestFramingRoundTrip()
    {
        // Real sockets: forces multi-read paths (a 100 KB payload never arrives in one Read).
        var (a, b) = LoopbackPair();
        try
        {
            var streamA = a.GetStream();
            var streamB = b.GetStream();

            var payloads = new[]
                               {
                                   Array.Empty<byte>(),
                                   Encoding.UTF8.GetBytes("hello"),
                                   new byte[100_000],
                               };
            payloads[2].AsSpan().Fill(0xAB);

            foreach (var (payload, i) in payloads.Select((p, i) => (p, i)))
            {
                var type = i % 2 == 0 ? CollabProtocol.FrameType.Json : CollabProtocol.FrameType.Presence;
                CollabProtocol.WriteFrame(streamA, type, payload);
            }

            for (var i = 0; i < payloads.Length; i++)
            {
                var expectedType = i % 2 == 0 ? CollabProtocol.FrameType.Json : CollabProtocol.FrameType.Presence;
                Check(CollabProtocol.TryReadFrameLength(streamB, out var length), $"frame {i}: stream ended early");
                Check(length == payloads[i].Length + 1, $"frame {i}: length {length} != {payloads[i].Length + 1}");

                var header = new byte[1];
                Check(CollabProtocol.TryReadExact(streamB, header), $"frame {i}: type read failed");
                Check((CollabProtocol.FrameType)header[0] == expectedType, $"frame {i}: wrong type {header[0]}");

                var body = new byte[length - 1];
                Check(CollabProtocol.TryReadExact(streamB, body), $"frame {i}: payload read failed");
                Check(body.AsSpan().SequenceEqual(payloads[i]), $"frame {i}: payload mismatch");
            }
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    private static void TestFrameSizeLimit()
    {
        var tooBig = new byte[CollabProtocol.MaxFrameSize]; // +1 type byte exceeds the cap
        try
        {
            CollabProtocol.WriteFrame(System.IO.Stream.Null, CollabProtocol.FrameType.Json, tooBig);
            throw new Exception("oversized frame was accepted");
        }
        catch (System.IO.IOException)
        {
            // expected
        }
    }

    private static void TestJsonDecode()
    {
        var encoded = CollabProtocol.EncodeJson(new { t = "hello", n = 42, g = Guid.Empty.ToString() });
        Check(CollabProtocol.TryDecodeJson(encoded, out var json), "valid message rejected");
        Check(CollabProtocol.GetString(json, "t") == "hello", "GetString failed");
        Check(CollabProtocol.TryGetInt(json, "n", out var n) && n == 42, "TryGetInt failed");
        Check(CollabProtocol.TryGetGuid(json, "g", out var g) && g == Guid.Empty, "TryGetGuid failed");

        Check(!CollabProtocol.TryDecodeJson("[1,2,3]"u8.ToArray(), out _), "array accepted as message");
        Check(!CollabProtocol.TryDecodeJson("not json"u8.ToArray(), out _), "garbage accepted");
        Check(!CollabProtocol.TryDecodeJson("{\"noType\":1}"u8.ToArray(), out _), "message without t accepted");

        // JSON never starts with '{' collides with relay control lines — assert the discriminator.
        var objStart = Encoding.UTF8.GetBytes("{\"t\":\"x\"}");
        Check(objStart[0] == (byte)'{', "JSON frames must start with '{' (relay control-line convention)");
    }

    // ---------------------------------------------------------------------------------------
    // Presence packet (binary, little-endian)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Values cross the wire both as strings and as typed tokens (Guid, number). A reader that
    /// assumes one kind throws InvalidCastException out of the frame loop - the editor crashed on a
    /// Guid token in a snapshot op. Both accessors must accept either representation.
    /// </summary>
    private static void TestJsonTokenTolerance()
    {
        var guid = Guid.Parse("9081b6b1-0e6d-4a2b-944a-eff62b9f58ea");
        var json = new JObject
                       {
                           ["asGuid"] = guid,
                           ["asString"] = guid.ToString(),
                           ["asNumber"] = 42,
                           ["asNull"] = JValue.CreateNull(),
                           ["asObject"] = new JObject { ["x"] = 1 },
                       };

        Check(CollabProtocol.TryGetGuid(json, "asGuid", out var fromGuid) && fromGuid == guid,
              "TryGetGuid failed on a Guid token");
        Check(CollabProtocol.TryGetGuid(json, "asString", out var fromString) && fromString == guid,
              "TryGetGuid failed on a string token");
        Check(!CollabProtocol.TryGetGuid(json, "asNumber", out _), "TryGetGuid accepted a number");
        Check(!CollabProtocol.TryGetGuid(json, "asObject", out _), "TryGetGuid accepted an object");

        Check(CollabProtocol.GetString(json, "asGuid") == guid.ToString(), "GetString failed on a Guid token");
        Check(CollabProtocol.GetString(json, "asNumber") == "42", "GetString failed on a number");
        Check(CollabProtocol.GetString(json, "asNull", "fallback") == "fallback", "GetString failed on null");
        Check(CollabProtocol.GetString(json, "asObject", "fallback") == "fallback", "GetString failed on an object");
        Check(CollabProtocol.GetString(json, "missing", "fallback") == "fallback", "GetString failed when missing");
    }

    private static void TestPresencePacket()
    {
        var packet = new PresencePacket
                         {
                             PeerId = 7,
                             Flags = PresencePacket.FlagHoveringCanvas,
                             CompositionId = Guid.NewGuid(),
                             CursorOnCanvas = new Vector2(-123.5f, 88.25f),
                             SelectedChildIds = [Guid.NewGuid(), Guid.NewGuid()],
                             ColorRgb = 0x12ab34,
                             WheelSpin = -3,
                             PressedKeys = [17, 88],
                         };

        var buffer = new byte[128];
        var size = packet.Encode(buffer);
        Check(size == 2 + 16 + 8 + 1 + 2 * 16 + 1 + 3 + 1 + 2, $"encoded size {size} unexpected");
        Check(PresencePacket.TryDecode(buffer.AsSpan(0, size), out var decoded), "decode failed");
        Check(decoded.PeerId == 7 && decoded.Flags == packet.Flags, "header fields mismatch");
        Check(decoded.CompositionId == packet.CompositionId, "composition id mismatch");
        Check(MathF.Abs(decoded.CursorOnCanvas.X - packet.CursorOnCanvas.X) < 0.001f, "cursor x drifted");
        Check(decoded.SelectedChildIds.SequenceEqual(packet.SelectedChildIds), "selection mismatch");
        Check(decoded.ColorRgb == packet.ColorRgb, $"cursor color lost ({decoded.ColorRgb:x6})");
        Check(decoded.WheelSpin == -3, "wheel spin lost");
        Check(decoded.PressedKeys.SequenceEqual(packet.PressedKeys), "pressed keys lost");

        // An older peer's packet (no appended bytes) must still decode: color 0, no keys, no wheel.
        Check(PresencePacket.TryDecode(buffer.AsSpan(0, 27 + 2 * 16), out var oldPeer), "short packet rejected");
        Check(oldPeer.ColorRgb == 0 && oldPeer.WheelSpin == 0 && oldPeer.PressedKeys.Length == 0,
              "appended fields decoded from a packet that has none");

        Check(!PresencePacket.TryDecode(buffer.AsSpan(0, 26), out _), "truncated packet accepted");
    }

    // ---------------------------------------------------------------------------------------
    // Auth (PBKDF2 + HMAC challenge/response)
    // ---------------------------------------------------------------------------------------

    private static void TestAuth()
    {
        var salt = CollabAuth.CreateSalt();
        var nonce = CollabAuth.CreateNonce();
        var keyA = CollabAuth.DeriveKey("show-password", salt);
        var keyB = CollabAuth.DeriveKey("show-password", salt);
        Check(keyA.AsSpan().SequenceEqual(keyB), "derivation not deterministic for same salt");

        var other = CollabAuth.DeriveKey("wrong-password", salt);
        Check(!keyA.AsSpan().SequenceEqual(other), "different passwords produced equal keys");

        var mac = CollabAuth.ComputeMac(keyA, nonce);
        Check(CollabAuth.MacsEqual(mac, CollabAuth.ComputeMac(keyB, nonce)), "valid mac rejected");
        Check(!CollabAuth.MacsEqual(mac, CollabAuth.ComputeMac(other, nonce)), "wrong-password mac accepted");
        Check(!CollabAuth.MacsEqual(mac, CollabAuth.ComputeMac(keyA, CollabAuth.CreateNonce())), "mac bound to nonce");

        Check(CollabAuth.TryFromHexString(CollabAuth.ToHexString(mac), out var roundTrip)
              && roundTrip.AsSpan().SequenceEqual(mac), "hex round-trip failed");
    }

    // ---------------------------------------------------------------------------------------
    // Heartbeats
    // ---------------------------------------------------------------------------------------

    private static void TestHeartbeatAnswersAreNotAnswered()
    {
        var port = FreeTcpPort();
        using var host = new CollabHostTransport(port, enableLanDiscovery: false, () => new JObject());
        using var raw = new TcpClient();
        raw.Connect(IPAddress.Loopback, port);
        raw.NoDelay = true;
        var stream = raw.GetStream();

        CollabProtocol.WriteFrame(stream, CollabProtocol.FrameType.Heartbeat, BitConverter.GetBytes(Environment.TickCount64));

        // Behave like a peer that answers every heartbeat it receives: if the host answered those
        // answers too, the pair would ping-pong for as long as the connection lives.
        var heartbeatsReceived = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 1500)
        {
            if (!raw.Client.Poll(50_000, SelectMode.SelectRead))
                continue;

            Check(TryReadRawFrame(stream, out var type, out var payload), "host closed the connection");
            if (type != CollabProtocol.FrameType.Heartbeat)
                continue;

            heartbeatsReceived++;
            CollabProtocol.WriteFrame(stream, CollabProtocol.FrameType.Heartbeat, payload);
        }

        Check(heartbeatsReceived == 1, $"host sent {heartbeatsReceived} heartbeats for one ping");
    }

    /// <summary>
    /// A host transport whose constructor fails after its TCP listener is up must release that
    /// listener. Otherwise hosting again in the same process fails with "address in use" forever.
    /// </summary>
    private static void TestHostPortReleasedOnFailure()
    {
        var port = FreeTcpPort();

        // Occupy only the UDP half, so the constructor gets past the TCP bind and then fails.
        using (var squatter = new UdpClient(port))
        {
            try
            {
                using var host = new CollabHostTransport(port, enableLanDiscovery: false, () => new JObject());
                throw new Exception("host started although the UDP port was occupied");
            }
            catch (SocketException)
            {
                // expected: the UDP bind is taken
            }
        }

        using var restarted = new CollabHostTransport(port, enableLanDiscovery: false, () => new JObject());
    }

    // ---------------------------------------------------------------------------------------
    // Deployer: inventory diff + full chunked transfer
    // ---------------------------------------------------------------------------------------

    private static void TestDeployInventoryDiff()
    {
        var server = new Dictionary<string, BoygroupDeployer.InventoryEntry>(StringComparer.Ordinal)
                         {
                             ["a.txt"] = new() { RelativePath = "a.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["b.bin"] = new() { RelativePath = "b.bin", Size = 500_000, LastWriteTicks = 638_000_000_000_000_000 },
                             ["sub/c.txt"] = new() { RelativePath = "sub/c.txt", Size = 3, LastWriteTicks = 638_000_000_000_000_000 },
                             ["d.txt"] = new() { RelativePath = "d.txt", Size = 100, LastWriteTicks = 638_000_000_000_000_000 },
                         };
        var client = new Dictionary<string, BoygroupDeployer.InventoryEntry>(StringComparer.Ordinal)
                         {
                             // a.txt: identical. b.bin: size differs. sub/c.txt: 30 s older (beyond
                             // the mtime tolerance, i.e. the server copy really is newer). d.txt: server-only.
                             ["a.txt"] = new() { RelativePath = "a.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["b.bin"] = new() { RelativePath = "b.bin", Size = 400_000, LastWriteTicks = 638_000_000_000_000_000 },
                             ["sub/c.txt"] = new() { RelativePath = "sub/c.txt", Size = 3, LastWriteTicks = 638_000_000_000_000_000 - 30_000_000 },
                             ["stale.txt"] = new() { RelativePath = "stale.txt", Size = 1, LastWriteTicks = 1 },
                         };

        var message = BoygroupDeployer.BuildInventoryMessage(server);
        var parsed = BoygroupDeployer.ParseInventory(message);
        Check(parsed.Count == 4 && parsed["b.bin"].Size == 500_000, "inventory message round-trip failed");

        var missing = BoygroupDeployer.ComputeMissingFiles(server, client);
        // b.bin: size differs. sub/c.txt: mtime differs. d.txt: exists only on the server.
        Check(missing.Count == 3, $"expected 3 missing, got {string.Join(',', missing)}");
        Check(missing[0] == "sub/c.txt" && missing[1] == "d.txt" && missing[2] == "b.bin",
              $"smallest-first sort wrong: {string.Join(',', missing)}");

        var extra = BoygroupDeployer.ComputeExtraFiles(server, client);
        Check(extra.Count == 1 && extra[0] == "stale.txt", "extra-file detection failed");
    }

    private static void TestDeployFileTransfer()
    {
        var root = Directory.CreateTempSubdirectory("tixl-deploy-");
        try
        {
            // Empty file (edge case), a text file, and a 500 KB binary blob spanning several chunks.
            var mtime = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var cases = new (string Name, byte[] Content)[]
                            {
                                ("empty.dat", []),
                                ("notes.txt", "höllo ünICODE ✓"u8.ToArray()),
                                ("blob.bin", MakeBlob(500_000)),
                            };
            foreach (var (name, content) in cases)
            {
                var path = Path.Combine(root.FullName, name);
                File.WriteAllBytes(path, content);
                File.SetLastWriteTimeUtc(path, mtime);
            }

            var inventory = BoygroupDeployer.BuildInventory(root.FullName);
            Check(inventory.Count == 3, $"inventory found {inventory.Count} files");

            foreach (var (name, content) in cases)
            {
                var receiver = new BoygroupDeployer.FileReceiver();
                receiver.Begin(root.FullName, new JObject { ["t"] = "fileBegin", ["path"] = name });

                var sender = new BoygroupDeployer.FileSender(root.FullName, name);
                try
                {
                JObject? message;
                while ((message = sender.NextMessage()) != null)
                {
                    var type = message["t"]?.Value<string>();
                    if (type == "fileChunk")
                        receiver.Chunk(message);
                    else if (type == "fileEnd")
                    {
                        var error = receiver.Finish(message);
                        Check(error == null, $"receiver error for {name}: {error}");
                    }
                }

                Check(sender.Completed, $"sender never completed for {name}");
                }
                finally
                {
                    sender.Dispose();
                }

                Check(receiver.Error == null, $"receiver error for {name}: {receiver.Error}");
                var deployed = File.ReadAllBytes(Path.Combine(root.FullName, name));
                Check(deployed.AsSpan().SequenceEqual(content), $"content mismatch for {name}");
                Check(File.GetLastWriteTimeUtc(Path.Combine(root.FullName, name)) == mtime,
                      $"mtime not restored for {name}");
            }
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch { /* temp cleanup is best-effort in tests */ }
        }
    }

    /// <summary>
    /// The boygroup responder answers a scanner probe with an announcement the boygroup consumer
    /// accepts. This locks in the wire contract between LanDiscoveryResponder and
    /// LanDiscoveryScanner — the scanner once filtered for a collab prefix and silently dropped
    /// every boygroup announcement.
    /// </summary>
    private static void TestBoygroupDiscovery()
    {
        var port = FreeTcpPort();
        using var server = new BoygroupServerTransport(port, ["Main"]);
        var scanner = new LanDiscoveryScanner();
        try
        {
            scanner.Start(port + LanDiscoveryResponder.DiscoveryPortOffset, [IPAddress.Loopback]);

            JObject? announcement = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            var rawSeen = 0;

            // Independent control: one raw probe straight at the responder, bypassing the scanner.
            string? rawProbeOutcome;
            using (var raw = new UdpClient())
            {
                raw.Client.ReceiveTimeout = 1000;
                var probe = Encoding.UTF8.GetBytes(LanDiscoveryResponder.ProbePayload);
                raw.Send(probe, probe.Length, new IPEndPoint(IPAddress.Loopback, port + LanDiscoveryResponder.DiscoveryPortOffset));
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var reply = raw.Receive(ref endpoint);
                    rawProbeOutcome = $"raw probe answered ({Encoding.UTF8.GetString(reply)[..Math.Min(40, reply.Length)]}...)";
                }
                catch (SocketException)
                {
                    rawProbeOutcome = "raw probe got NO answer";
                }
            }

            while (DateTime.UtcNow < deadline && announcement == null)
            {
                while (scanner.Results.TryDequeue(out var result))
                {
                    rawSeen++;
                    if (result.Announcement["boygroup"] != null)
                    {
                        announcement = result.Announcement;
                        break;
                    }
                }

                if (announcement == null)
                    Thread.Sleep(50);
            }

            Check(announcement != null,
                  $"no boygroup announcement via scanner (raw results: {rawSeen}; responder alive: {server.DiscoveryResponder != null}; control probe: {rawProbeOutcome})");
            if (announcement == null)
                return;

            Check(announcement["server"]?.Value<string>() == Environment.MachineName,
                  "announcement carries the server name");
            Check(announcement["port"]?.Value<int>() == port, "announcement carries the TCP port");
            Check(announcement["groups"] is JArray groups && groups.Count == 1 && groups[0].Value<string>() == "Main",
                  "announcement carries the groups");
        }
        finally
        {
            scanner.Stop();
        }
    }

    /// <summary>
    /// A write time round-tripped through another filesystem does not reproduce the master's ticks
    /// bit-exactly; the deploy diff must tolerate the residue or every deploy re-sends every
    /// transferred file, which reboot-looped auto-deploy-on-join machines.
    /// </summary>
    private static void TestDeployMtimeTolerance()
    {
        var server = new Dictionary<string, BoygroupDeployer.InventoryEntry>
                         {
                             ["a.txt"] = new() { RelativePath = "a.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["b.txt"] = new() { RelativePath = "b.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["c.txt"] = new() { RelativePath = "c.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["d.txt"] = new() { RelativePath = "d.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                             ["e.txt"] = new() { RelativePath = "e.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 },
                         };
        var client = new Dictionary<string, BoygroupDeployer.InventoryEntry>
                         {
                             // Same size, tick residue within tolerance: up to date.
                             ["a.txt"] = new() { RelativePath = "a.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 + 4_812 },
                             // Same size, slightly older (clock skew inside tolerance): up to date.
                             ["b.txt"] = new() { RelativePath = "b.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 - 500_000 },
                             // Same size, 30 s older: the server copy really is newer — missing.
                             ["c.txt"] = new() { RelativePath = "c.txt", Size = 10, LastWriteTicks = 638_000_000_000_000_000 - 300_000_000 },
                             // Different size: missing regardless of time.
                             ["d.txt"] = new() { RelativePath = "d.txt", Size = 11, LastWriteTicks = 638_000_000_000_000_000 },
                             // Not present at all: missing.
                         };
        var missing = BoygroupDeployer.ComputeMissingFiles(server, client);
        Check(missing.Count == 3 && missing.Contains("c.txt") && missing.Contains("d.txt") && missing.Contains("e.txt"),
              $"expected [c.txt, d.txt, e.txt] missing, got [{string.Join(", ", missing)}]");
    }

    /// <summary>
    /// Identical content whose mtime moved (a render node re-saves an unchanged project file on
    /// launch) must not be re-sent: the hash decides when size matches and time does not.
    /// </summary>
    private static void TestDeployHashOverMtime()
    {
        const long t0 = 638_000_000_000_000_000;
        var server = new Dictionary<string, BoygroupDeployer.InventoryEntry>
                         {
                             ["same.txt"] = new() { RelativePath = "same.txt", Size = 10, LastWriteTicks = t0, Hash = "AAAA" },
                             ["diff.txt"] = new() { RelativePath = "diff.txt", Size = 10, LastWriteTicks = t0, Hash = "BBBB" },
                             ["unhashed.txt"] = new() { RelativePath = "unhashed.txt", Size = 10, LastWriteTicks = t0 },
                         };
        var client = new Dictionary<string, BoygroupDeployer.InventoryEntry>
                         {
                             // Same content, 30 s newer (the node rewrote it on launch): current.
                             ["same.txt"] = new() { RelativePath = "same.txt", Size = 10, LastWriteTicks = t0 + 300_000_000, Hash = "AAAA" },
                             // Different content, newer: missing.
                             ["diff.txt"] = new() { RelativePath = "diff.txt", Size = 10, LastWriteTicks = t0 + 300_000_000, Hash = "CCCC" },
                             // No hashes available (large file): time decides, so missing.
                             ["unhashed.txt"] = new() { RelativePath = "unhashed.txt", Size = 10, LastWriteTicks = t0 + 300_000_000 },
                         };

        var missing = BoygroupDeployer.ComputeMissingFiles(server, client);
        Check(missing.Count == 2 && missing.Contains("diff.txt") && missing.Contains("unhashed.txt"),
              $"expected [diff.txt, unhashed.txt] missing, got [{string.Join(", ", missing)}]");
    }

    private static void TestDeployPathSafety()
    {
        var root = Directory.CreateTempSubdirectory("tixl-path-");
        try
        {
            foreach (var evil in new[] { "../escape.txt", "sub/../../escape.txt", "a/../../b" })
            {
                try
                {
                    BoygroupDeployer.SafeProjectPath(root.FullName, evil);
                    throw new Exception($"path escape accepted: {evil}");
                }
                catch (System.IO.IOException)
                {
                    // expected
                }
            }

            // A sibling directory that merely shares the root's name prefix is still outside it;
            // both an absolute path and a relative climb must be rejected.
            var sibling = root.FullName + "-evil";
            foreach (var evil in new[]
                                 {
                                     Path.Combine(sibling, "x.txt"),
                                     "../" + Path.GetFileName(root.FullName) + "-evil/x.txt",
                                     "",
                                 })
            {
                try
                {
                    BoygroupDeployer.SafeProjectPath(root.FullName, evil);
                    throw new Exception($"path escape accepted: {evil}");
                }
                catch (System.IO.IOException)
                {
                    // expected
                }
            }

            var ok = BoygroupDeployer.SafeProjectPath(root.FullName, "sub/dir/file.txt");
            Check(ok.Replace('\\', '/').EndsWith("sub/dir/file.txt", StringComparison.Ordinal), "safe path mangled");
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Failover election — needs separate processes (the arbiter is a static singleton).
    // Spawns two `failover-node` children on the same host: multicast loopback makes this work
    // on Windows, Linux and macOS alike. ShowId isolates this run from any other on the machine.
    // ---------------------------------------------------------------------------------------

    private static void TestFailoverElection()
    {
        var showId = $"headless-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var heartbeatMs = 150;
        using var master = StartNode(showId, priority: 200, heartbeatMs);
        using var backup = StartNode(showId, priority: 100, heartbeatMs);

        // Master becomes ACTIVE, backup settles on Standby (output suppressed).
        var masterActive = WaitForRole(master, "ACTIVE", TimeSpan.FromSeconds(8));
        Check(masterActive, "master never became ACTIVE; events: " + backup.AllEvents);
        var backupStandby = WaitForRole(backup, "Standby", TimeSpan.FromSeconds(8));
        Check(backupStandby, "backup never settled on Standby; events: " + backup.AllEvents);

        // Kill the master → backup promotes within TimeoutFactor × Heartbeat (+ margin).
        var killAt = Stopwatch.StartNew();
        master.Kill();

        var promoted = WaitForRole(backup, "ACTIVE", TimeSpan.FromSeconds(8));
        Check(promoted, $"backup never promoted after master death; events: {backup.AllEvents}");
        var takeoverMs = killAt.ElapsedMilliseconds;

        // Restart the higher-priority master → preemption reclaims Active, backup demotes.
        using var master2 = StartNode(showId, priority: 200, heartbeatMs);
        Check(WaitForRole(master2, "ACTIVE", TimeSpan.FromSeconds(10)), "restarted master never reclaimed; events: " + master2.AllEvents);
        Check(WaitForRole(backup, "Standby", TimeSpan.FromSeconds(10)), "backup never demoted after preempt; events: " + backup.AllEvents);

        // The bound is the arbiter's timeout plus the watchdog tick granularity; a takeover that
        // needs noticeably longer than that is a show that sits dark.
        var budgetMs = 3 * heartbeatMs + 500;
        Check(takeoverMs <= budgetMs, $"takeover took {takeoverMs} ms, budget {budgetMs} ms");
        Console.WriteLine($"      failover: takeover after {takeoverMs} ms (budget {budgetMs} ms)");
    }

    /// <summary>
    /// Two nodes starting at the same time must never both stream. Each probe deadline used to see
    /// no *active* peer (a probing peer does not advertise itself) and both claimed Active.
    /// </summary>
    private static void TestFailoverNoDualActive()
    {
        var showId = $"headless-dual-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var heartbeatMs = 150;

        // Same priority: the tie-break by node id must still leave exactly one node on the wire.
        using var first = StartNode(showId, priority: 100, heartbeatMs);
        using var second = StartNode(showId, priority: 100, heartbeatMs);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            first.Poll();
            second.Poll();
            Check(!(first.IsUnsuppressed && second.IsUnsuppressed),
                  $"both nodes streamed at once (first: {first.CurrentRole}; second: {second.CurrentRole})");
            Thread.Sleep(25);
        }

        first.Poll();
        second.Poll();
        Check(first.IsUnsuppressed || second.IsUnsuppressed,
              $"neither node became active (first: {first.CurrentRole}; second: {second.CurrentRole})");
    }

    /// <summary>One failover participant: applies the config, streams role changes to stdout.</summary>
    private static int RunFailoverNode(string showId, int priority, int heartbeatMs)
    {
        FailoverArbiter.ApplyConfig(new FailoverConfig
                                        {
                                            Enabled = true,
                                            ShowId = showId,
                                            Priority = priority,
                                            HeartbeatMs = heartbeatMs,
                                            TimeoutFactor = 3,
                                            Preempt = true,
                                        });

        var reportedRole = "";
        var reportedSuppressed = -1;
        while (true)
        {
            Thread.Sleep(100);
            var role = FailoverArbiter.RoleText;
            var suppressed = FailoverArbiter.IsOutputSuppressed ? 1 : 0;
            if (role != reportedRole || suppressed != reportedSuppressed)
            {
                reportedRole = role;
                reportedSuppressed = suppressed;
                Console.WriteLine($"ROLE {role} suppressed={suppressed}");
            }
        }
    }

    private static FailoverNode StartNode(string showId, int priority, int heartbeatMs)
    {
        var self = Environment.ProcessPath
                   ?? throw new Exception("cannot resolve own executable for child spawn");
        var start = new ProcessStartInfo
                        {
                            FileName = self,
                            Arguments = $"failover-node {showId} {priority} {heartbeatMs}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                        };
        var process = Process.Start(start)
                      ?? throw new Exception("failed to spawn failover node");
        return new FailoverNode(process);
    }

    private static bool WaitForRole(FailoverNode node, string roleSubstring, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            node.Poll();
            if (node.CurrentRole.Contains(roleSubstring, StringComparison.Ordinal))
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>Async stdout pump for one child node (reading synchronously would deadlock).</summary>
    private sealed class FailoverNode : IDisposable
    {
        public string CurrentRole = "starting";
        public string AllEvents = "";

        /// <summary>False until the child reports suppressed=0; before its first report it streams nothing.</summary>
        public bool IsUnsuppressed { get; private set; }

        private readonly Process _process;
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _events = new();

        public FailoverNode(Process process)
        {
            _process = process;
            process.OutputDataReceived += (_, e) =>
                                              {
                                                  if (e.Data == null)
                                                      return;
                                                  lock (_stdout)
                                                      _stdout.AppendLine(e.Data);
                                              };
            process.ErrorDataReceived += (_, e) =>
                                             {
                                                 if (e.Data == null)
                                                     return;
                                                 lock (_events)
                                                     _events.AppendLine(e.Data);
                                             };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public void Poll()
        {
            string snapshot;
            lock (_stdout)
                snapshot = _stdout.ToString();
            var lastRoleLine = snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                       .LastOrDefault(l => l.StartsWith("ROLE ", StringComparison.Ordinal));
            if (lastRoleLine != null)
            {
                CurrentRole = lastRoleLine["ROLE ".Length..];
                IsUnsuppressed = CurrentRole.Contains("suppressed=0", StringComparison.Ordinal);
            }

            lock (_events)
                AllEvents = _events.ToString();
        }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
                _process.WaitForExit(2000);
            }
            catch
            {
                // already gone
            }
        }

        public void Dispose()
        {
            Kill();
            _process.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static byte[] MakeBlob(int length)
    {
        // Deterministic pseudo-random content: exercise compression-hostile bytes without RNG setup.
        var blob = new byte[length];
        var x = 0x9e3779b9;
        for (var i = 0; i < length; i++)
        {
            x = x * 1664525 + 1013904223;
            blob[i] = (byte)(x >> 24);
        }
        return blob;
    }

    private static int FreeTcpPort()
    {
        // The host binds TCP and UDP on the same wildcard port. Port 0 hands out ephemeral ports
        // that can fall inside a Windows excluded range (Hyper-V), where the UDP bind is denied, so
        // walk the registered range and verify that both protocols can take the port.
        for (var port = 21000; port < 31000; port++)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                using var udp = new UdpClient(port);
                return port;
            }
            catch (SocketException)
            {
                // Taken or excluded - try the next one.
            }
            finally
            {
                listener?.Stop();
            }
        }

        throw new Exception("no free TCP+UDP port for the host transport");
    }

    /// <summary>Reads one [length][type][payload] frame; false when the stream ended.</summary>
    private static bool TryReadRawFrame(System.IO.Stream stream, out CollabProtocol.FrameType type, out byte[] payload)
    {
        type = 0;
        payload = [];
        if (!CollabProtocol.TryReadFrameLength(stream, out var length) || length < 1)
            return false;

        var frame = new byte[length];
        if (!CollabProtocol.TryReadExact(stream, frame))
            return false;

        type = (CollabProtocol.FrameType)frame[0];
        payload = frame[1..];
        return true;
    }

    private static (TcpClient, TcpClient) LoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        var listenPort = ((IPEndPoint)listener.LocalEndpoint!).Port;
        client.Connect(IPAddress.Loopback, listenPort);
        var server = listener.AcceptTcpClient();
        listener.Stop();
        client.NoDelay = true;
        server.NoDelay = true;
        return (client, server);
    }
}
