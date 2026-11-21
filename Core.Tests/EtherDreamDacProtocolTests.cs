using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LaserCore.EtherDream.Net.Device;
using LaserCore.EtherDream.Net.Discovery;
using LaserCore.EtherDream.Net.Dto;
using LaserCore.EtherDream.Net.Enums;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Exercises the LaserCore.EtherDream.Net library (the transport used by the
/// EtherDreamOutput operator) against a protocol-accurate fake Ether Dream DAC,
/// covering every command type on the wire:
///   '?' ping, 'p' prepare, 'b' begin, 'd' data, 'q' rate-change, 's' stop,
///   0x00 emergency-stop, 'c' clear-estop
/// plus the response variants the device can produce (ack, NackBufferFull with
/// ping-pacing, NackInvalid with re-prepare, light-engine warm-up, safety flags)
/// and the UDP discovery broadcast.
///
/// Runs as one ordered scenario because all phases share TCP port 7765 - the port the EtherDream
/// library hardcodes. The scenario therefore needs loopback:7765 free: with a second TiXL instance
/// or a laser app running on the same machine it cannot be exercised and its phases fight the
/// foreign client. Discovered-DAC phases (T11/T12) additionally need UDP 7654 free.
/// </summary>
public class EtherDreamDacProtocolTests
{
    private bool _serverRunning;
    private TcpListener? _listener;
    private readonly ConcurrentQueue<(byte Cmd, byte[] Body)> _journal = new();
    private long _ackedPoints;
    private int _nackBufferFullCountdown;
    private bool _alwaysNackInvalid;
    private byte _lightEngineOverride;
    private ushort _lightEngineFlagsNext;
    private readonly object _clientLock = new();
    private readonly List<TcpClient> _liveClients = new();
    private int _sessionActive;
    private long _rejectedConnections;

    private void Check(bool ok, string what)
    {
        Assert.True(ok, what);
    }

    private void StatusResponse(NetworkStream s, byte cmd, byte ack, byte playState, ushort fullness, byte leState, ushort leFlags)
    {
        var b = new byte[22];
        b[0] = ack;
        b[1] = cmd;
        b[3] = leState;
        b[4] = playState;
        b[6] = (byte)(leFlags & 0xFF);
        b[7] = (byte)(leFlags >> 8);
        b[14] = (byte)(fullness & 0xFF);
        b[15] = (byte)(fullness >> 8);
        s.Write(b, 0, 22);
    }

    private async Task HandleClient(TcpClient client)
    {
        using var c = client;
        lock (_clientLock)
        {
            _liveClients.Add(c);
        }

        await using var s = c.GetStream();
        var leFlagsOneShot = Interlocked.Exchange(ref _lightEngineFlagsNext, (ushort)0);
        StatusResponse(s, (byte)'?', 97, 0, 0, 0, leFlagsOneShot);
        var buf = new byte[18 * 1800 + 8];
        ushort fullness = 0;
        const int capacity = 1799;
        byte playState = 0; // idle
        byte leState = 0;   // ready
        var estopFlag = false;
        try
        {
            while (true)
            {
                if (!await ReadExact(s, buf, 0, 1))
                    return;
                var cmd = buf[0];
                byte[] body = Array.Empty<byte>();
                byte ack = 97;
                var consume = 0;
                switch (cmd)
                {
                    case (byte)'?':
                        consume = 0;
                        break;
                    case (byte)'p':
                        consume = 0;
                        playState = Math.Max(playState, (byte)1);
                        break;
                    case (byte)'b':
                        consume = 6; // BeginCommandDto: u16 lwm + u32 rate (Pack=1 -> 7 bytes total)
                        playState = 2;
                        break;
                    case (byte)'q':
                        consume = 4;
                        break;
                    case (byte)'s':
                        consume = 0;
                        playState = 0;
                        fullness = 0;
                        break;
                    case 0x00: // estop
                        consume = 0;
                        leState = 3;
                        playState = 0;
                        fullness = 0;
                        estopFlag = true;
                        break;
                    case (byte)'c': // clear estop
                        consume = 0;
                        leState = 0;
                        estopFlag = false;
                        break;
                    case (byte)'d':
                    {
                        if (!await ReadExact(s, buf, 1, 2))
                            return;
                        var n = buf[1] | (buf[2] << 8);
                        if (!await ReadExact(s, buf, 3, 18 * n))
                            return;
                        body = new byte[2 + 18 * n];
                        Array.Copy(buf, 1, body, 0, body.Length);
                        fullness = (ushort)Math.Min(capacity, fullness + n);
                        if (_alwaysNackInvalid)
                            ack = 73;
                        else if (_nackBufferFullCountdown > 0)
                        {
                            ack = 70;
                            _nackBufferFullCountdown--;
                        }

                        if (ack == 97)
                            Interlocked.Add(ref _ackedPoints, n);
                        break;
                    }
                    default:
                        return;
                }

                if (consume > 0)
                {
                    if (!await ReadExact(s, buf, 1, consume))
                        return;
                    body = new byte[consume];
                    Array.Copy(buf, 1, body, 0, consume);
                }

                _journal.Enqueue((cmd, body));

                // simulate DAC draining its buffer between responses
                fullness = (ushort)Math.Max(0, fullness - Math.Min((int)fullness, 500));
                var le = _lightEngineOverride != 0 ? _lightEngineOverride : leState;
                var flags = estopFlag ? (ushort)1 : Interlocked.Exchange(ref _lightEngineFlagsNext, (ushort)0);
                estopFlag = false;
                StatusResponse(s, cmd, ack, playState, fullness, le, flags);
            }
        }
        finally
        {
            lock (_clientLock)
            {
                _liveClients.Remove(c);
            }
        }
    }

    private static async Task<bool> ReadExact(NetworkStream s, byte[] buf, int off, int len)
    {
        var read = 0;
        while (read < len)
        {
            var r = await s.ReadAsync(buf, off + read, len - read);
            if (r == 0)
                return false;
            read += r;
        }

        return true;
    }

    private void StartServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 7765);
        _listener.Start();
        _serverRunning = true;
        _ = Task.Run(async () =>
        {
            while (_serverRunning)
            {
                TcpClient cl;
                try
                {
                    cl = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(async () =>
                {
                    // Official protocol: the DAC communicates with one host at a time;
                    // a second connection while a session is active is rejected
                    if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) != 0)
                    {
                        Interlocked.Increment(ref _rejectedConnections);
                        cl.Close();
                        return;
                    }

                    try
                    {
                        await HandleClient(cl);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _sessionActive, 0);
                    }
                });
            }
        });
    }

    /// <summary>
    /// The fake DAC serves one session at a time. A client's Dispose() returns before the server's
    /// read loop has noticed the close and released the slot, so the next connect must wait for it;
    /// otherwise the server rejects it and the client sees an immediate close.
    /// </summary>
    private void WaitForSessionRelease()
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Volatile.Read(ref _sessionActive) != 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(5);
    }

    private void StopServer()
    {
        _serverRunning = false;
        _listener?.Stop();
        // simulate device death: break all live connections so clients see a reset
        lock (_clientLock)
        {
            foreach (var cl in _liveClients)
            {
                try
                {
                    cl.Dispose();
                }
                catch
                {
                    // already dead
                }
            }

            _liveClients.Clear();
        }
    }

    private List<(byte Cmd, byte[] Body)> JournalSince(int idx)
    {
        var all = _journal.ToArray();
        return all.Length > idx ? all[idx..].ToList() : new();
    }

    private static int Count(List<(byte Cmd, byte[] Body)> slice, byte cmd) => slice.Count(e => e.Cmd == cmd);

    // Mirrors EtherDreamOutput.ConvertPoints
    private static DacPointDto[] ConvertPoints(int[][] pts) => pts.Select(p => new DacPointDto
    {
        Control = 0,
        X = (short)Math.Clamp(p[0], short.MinValue, short.MaxValue),
        Y = (short)Math.Clamp(p[1], short.MinValue, short.MaxValue),
        R = (ushort)Math.Clamp(p[2], 0, ushort.MaxValue),
        G = (ushort)Math.Clamp(p[3], 0, ushort.MaxValue),
        B = (ushort)Math.Clamp(p[4], 0, ushort.MaxValue),
        I = 65535,
        U1 = 0,
        U2 = 0,
    }).ToArray();

    private static DacPointDto[] MakeFrame(int count, int seed = 1) =>
        Enumerable.Range(0, count).Select(i => new DacPointDto
        {
            Control = 0,
            X = (short)((i * seed) % 40000 - 20000),
            Y = (short)((i * 7) % 40000 - 20000),
            R = (ushort)(i % 65536),
            G = (ushort)((i * 3) % 65536),
            B = (ushort)((i * 11) % 65536),
            I = 65535,
            U1 = 0,
            U2 = 0,
        }).ToArray();

    // A simplified mirror of EtherDreamOutput.ConnectionLoopAsync + SendLoopAsync
    private static async Task<long> OperatorStyleLoop(DacPointDto[] frame, CancellationToken ct)
    {
        Dac? dac = null;
        var errors = 0;
        long sent = 0;
        while (!ct.IsCancellationRequested)
        {
            if (dac == null)
            {
                try
                {
                    dac = new Dac("127.0.0.1");
                    errors = 0;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(300, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }
            }

            try
            {
                dac.StreamPoints(frame, 30000);
                sent += frame.Length;
                errors = 0;
                try
                {
                    await Task.Delay(1, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (InvalidOperationException)
            {
                try
                {
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception) when (++errors < 5)
            {
                try
                {
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception)
            {
                try
                {
                    dac.Dispose();
                }
                catch
                {
                    // socket already dead
                }

                dac = null;
            }
        }

        try
        {
            dac?.Dispose();
        }
        catch
        {
            // socket already dead
        }

        return sent;
    }

    [Fact]
    public async Task FullDacProtocolScenario()
    {
        try
        {
            // ---- T1: connect + stream (hello, 'p', 'd', 'b' with byte-exact bodies) ----
            StartServer();
            var j = _journal.Count;
            WaitForSessionRelease();
            var dac = new Dac("127.0.0.1");
            var statusUpdates = 0;
            dac.StatusUpdated += (_, _, _, _) => Interlocked.Increment(ref statusUpdates);
            var t1 = JournalSince(j);
            Check(t1.Count == 0, "ctor sends no command (only consumes the hello response)");
            var frame2 = ConvertPoints(new[]
            {
                new[] { -32767, 32767, 65535, 0, 0 },
                new[] { 100, -200, 0, 65535, 65535 },
            });
            j = _journal.Count;
            dac.StreamPoints(frame2, 30000);
            var t1b = JournalSince(j);
            Check(Count(t1b, (byte)'p') == 1, "prepare 'p' sent (was idle)");
            var d1 = t1b.FirstOrDefault(e => e.Cmd == (byte)'d');
            Check(t1b.Count(e => e.Cmd == (byte)'d') == 1 && d1.Body.Length == 2 + 36, "single data 'd' with 2 points");
            var px = d1.Body;
            Check(BitConverter.ToInt16(px, 2 + 2) == -32767 && BitConverter.ToInt16(px, 2 + 4) == 32767
                                              && BitConverter.ToUInt16(px, 2 + 6) == 65535 && BitConverter.ToUInt16(px, 2 + 8) == 0 && BitConverter.ToUInt16(px, 2 + 10) == 0,
                  "point #1 values on wire (X/Y/R/G/B)");
            Check(BitConverter.ToInt16(px, 2 + 20) == 100 && BitConverter.ToInt16(px, 2 + 22) == -200
                                              && BitConverter.ToUInt16(px, 2 + 28) == 65535 && BitConverter.ToUInt16(px, 2 + 30) == 65535,
                  "point #2 values on wire (X/Y/B, G)");
            var b1 = t1b.FirstOrDefault(e => e.Cmd == (byte)'b');
            Check(b1.Cmd == (byte)'b' && b1.Body.Length == 6 && BitConverter.ToUInt16(b1.Body, 0) == 0 && BitConverter.ToUInt32(b1.Body, 2) == 30000,
                  "begin 'b' body: lwm=0, rate=30000 (7-byte command)");
            Check(Interlocked.Read(ref _ackedPoints) == 2, "server acked 2 points");
            Check(statusUpdates >= 3, $"status events fired ({statusUpdates})");

            // ---- T4: point-rate change while playing -> 'q' with correct body ----
            j = _journal.Count;
            dac.StreamPoints(frame2, 25000);
            var t4 = JournalSince(j);
            var q4 = t4.FirstOrDefault(e => e.Cmd == (byte)'q');
            Check(q4.Cmd == (byte)'q' && q4.Body.Length == 4 && BitConverter.ToUInt32(q4.Body, 0) == 25000, "queue-rate-change 'q' body rate=25000");
            Check(Count(t4, (byte)'b') == 0 && Count(t4, (byte)'p') == 0, "no spurious begin/prepare on rate change");
            Check(Count(t4, (byte)'d') == 1, "data still sent");

            // ---- T8: every direct command ----
            j = _journal.Count;
            dac.Ping();
            dac.Prepare();
            dac.Begin(30000);
            var s8 = JournalSince(j);
            Check(Count(s8, (byte)'?') >= 1 && Count(s8, (byte)'p') >= 1, "ping + prepare received");
            var b8 = s8.FirstOrDefault(e => e.Cmd == (byte)'b');
            Check(b8.Cmd == (byte)'b' && BitConverter.ToUInt32(b8.Body, 2) == 30000, "begin(30000) body correct");
            j = _journal.Count;
            dac.StreamPoints(frame2, 30000);
            var s8b = JournalSince(j);
            var s8q = s8b.Where(e => e.Cmd == (byte)'q').ToList();
            Check(Count(s8b, (byte)'b') == 0 && Count(s8b, (byte)'p') == 0 && s8q.Count <= 1
                  && (s8q.Count == 0 || BitConverter.ToUInt32(s8q[0].Body, 0) == 30000),
                  "stream after Begin(30000): data + at most one rate sync to 30000");
            j = _journal.Count;
            dac.QueueRateChange(15000);
            var s8c = JournalSince(j);
            var q8 = s8c.FirstOrDefault(e => e.Cmd == (byte)'q');
            Check(q8.Cmd == (byte)'q' && BitConverter.ToUInt32(q8.Body, 0) == 15000, "QueueRateChange(15000) body correct");
            j = _journal.Count;
            dac.Stop();
            var s8d = JournalSince(j);
            Check(Count(s8d, (byte)'s') == 1, "stop 's' received");
            var prepOk = dac.TryPrepare();
            j = _journal.Count;
            Check(prepOk, "TryPrepare() returns true from idle (sends 'p')");
            var safetyReasons = new ConcurrentQueue<string>();
            dac.SafetyFaultDetected += r => safetyReasons.Enqueue(r);
            dac.EStop();
            await Task.Delay(200);
            var s8e = JournalSince(j);
            Check(s8e.Any(e => e.Cmd == 0x00), "emergency stop 0x00 received");
            Check(safetyReasons.Any(r => r.Contains("Emergency stop")), $"safety fault event fired: '{string.Join(";", safetyReasons)}'");
            j = _journal.Count;
            dac.ClearEStop();
            var s8f = JournalSince(j);
            Check(s8f.Any(e => e.Cmd == (byte)'c'), "clear estop 'c' received");
            await Task.Delay(1200); // let a heartbeat ping arrive
            j = _journal.Count;
            dac.Dispose();
            var s8g = JournalSince(j);
            Check(s8g.Any(e => e.Cmd == (byte)'s'), "Dispose() sends final stop 's'");

            // ---- T10: overtemperature safety flag in status ----
            WaitForSessionRelease();
            var dac10 = new Dac("127.0.0.1");
            var reasons10 = new ConcurrentQueue<string>();
            dac10.SafetyFaultDetected += r => reasons10.Enqueue(r);
            _lightEngineFlagsNext = 8; // overtemperature bit
            dac10.Ping();
            await Task.Delay(200);
            Check(reasons10.Any(r => r.Contains("Overtemperature")), $"overtemperature event fired: '{string.Join(";", reasons10)}'");
            dac10.Dispose();

            // ---- T9: light engine warm-up -> StreamPoints throws, then works ----
            WaitForSessionRelease();
            var dac9 = new Dac("127.0.0.1");
            _lightEngineOverride = 1; // Warmup
            dac9.Ping();              // refresh last-known status (readiness is checked at entry)
            await Task.Delay(200);
            InvalidOperationException? warmEx = null;
            try
            {
                dac9.StreamPoints(frame2, 30000);
            }
            catch (InvalidOperationException e)
            {
                warmEx = e;
            }

            Check(warmEx != null && warmEx.Message.Contains("Light engine not ready"), $"warm-up throws InvalidOperationException ('{warmEx?.Message}')");
            _lightEngineOverride = 0;
            dac9.Ping();
            await Task.Delay(200);
            var before9 = Interlocked.Read(ref _ackedPoints);
            dac9.StreamPoints(frame2, 30000);
            Check(Interlocked.Read(ref _ackedPoints) == before9 + 2, "streams fine once engine reports Ready");
            dac9.Dispose();

            // ---- T5: chunked streaming of a large frame with constrained buffer ----
            WaitForSessionRelease();
            var dac5 = new Dac("127.0.0.1");
            var bigFrame = MakeFrame(4000);
            j = _journal.Count;
            await Task.Run(() => dac5.StreamPoints(bigFrame, 30000));
            var t5 = JournalSince(j);
            var d5 = t5.Where(e => e.Cmd == (byte)'d').ToList();
            Check(d5.Count >= 3, $"frame chunked into {d5.Count} data commands (buffer-constrained)");
            var pts5 = d5.Sum(e => BitConverter.ToUInt16(e.Body, 0));
            Check(pts5 == 4000, $"all {pts5}/4000 points delivered across chunks");
            var ok5 = true;
            var idx5 = 0;
            foreach (var e in d5)
            {
                var n5 = BitConverter.ToUInt16(e.Body, 0);
                for (var k = 0; k < n5; k++, idx5++)
                {
                    var off = 2 + k * 18;
                    if (BitConverter.ToInt16(e.Body, off + 2) != bigFrame[idx5].X || BitConverter.ToInt16(e.Body, off + 4) != bigFrame[idx5].Y
                     || BitConverter.ToUInt16(e.Body, off + 6) != bigFrame[idx5].R || BitConverter.ToUInt16(e.Body, off + 8) != bigFrame[idx5].G
                     || BitConverter.ToUInt16(e.Body, off + 10) != bigFrame[idx5].B)
                    {
                        ok5 = false;
                        break;
                    }
                }

                if (!ok5)
                    break;
            }

            Check(ok5, "every chunked point byte-identical to source order");
            dac5.Dispose();

            // ---- T6: NackBufferFull -> library pings and retries ----
            WaitForSessionRelease();
            var dac6 = new Dac("127.0.0.1");
            dac6.StreamPoints(frame2, 30000); // get to Playing state
            j = _journal.Count;
            _nackBufferFullCountdown = 3;
            var before6 = Interlocked.Read(ref _ackedPoints);
            dac6.StreamPoints(frame2, 30000);
            var t6 = JournalSince(j);
            Check(Count(t6, (byte)'d') == 4, $"data retried until ack ({Count(t6, (byte)'d')} attempts, 3 nacked)");
            Check(Count(t6, (byte)'?') >= 3, $"ping used as pacing between nacks ({Count(t6, (byte)'?')} pings incl. heartbeat)");
            Check(Interlocked.Read(ref _ackedPoints) == before6 + 2, "points delivered after nacks");
            dac6.Dispose();

            // ---- T7: persistent NackInvalid -> throws after internal retries ----
            WaitForSessionRelease();
            var dac7 = new Dac("127.0.0.1");
            _alwaysNackInvalid = true;
            InvalidOperationException? nackEx = null;
            j = _journal.Count;
            try
            {
                dac7.StreamPoints(frame2, 30000);
            }
            catch (InvalidOperationException e)
            {
                nackEx = e;
            }

            _alwaysNackInvalid = false;
            Check(nackEx != null && nackEx.Message.Contains("NACK"), $"throws DAC NACK after retries ('{nackEx?.Message}')");
            var t7 = JournalSince(j);
            Check(Count(t7, (byte)'d') >= 3 && Count(t7, (byte)'p') >= 2, $"library re-prepared between invalid nacks (data={Count(t7, (byte)'d')}, prepare={Count(t7, (byte)'p')})");
            dac7.Dispose();

            // ---- T2: server restart + fresh Dac (ConnectionLoopAsync recovery) ----
            StopServer();
            await Task.Delay(150);
            StartServer();
            WaitForSessionRelease();
            var dac2 = new Dac("127.0.0.1");
            var before2 = Interlocked.Read(ref _ackedPoints);
            dac2.StreamPoints(frame2, 30000);
            Check(Interlocked.Read(ref _ackedPoints) == before2 + 2, "fresh Dac streams after restart");
            dac2.Dispose();

            // ---- T3: server death under live connection ----
            WaitForSessionRelease();
            var dac3 = new Dac("127.0.0.1");
            dac3.StreamPoints(frame2, 30000);
            StopServer();
            Exception? dead3 = null;
            try
            {
                for (var i = 0; i < 100; i++)
                    dac3.StreamPoints(frame2, 30000);
            }
            catch (Exception e)
            {
                dead3 = e;
            }

            Check(dead3 != null, $"StreamPoints throws when device dies ({dead3?.GetType().Name})");
            dac3.Dispose();
            await Task.Delay(150);
            StartServer();

            // ---- T11: UDP discovery broadcast ----
            // The DAC discovery port (UDP 7654) is machine-global. If another laser app or a second
            // editor owns it, discovery cannot be exercised here - say so instead of failing the run.
            DeviceDiscovery? discovery = null;
            try
            {
                discovery = new DeviceDiscovery();
            }
            catch (SocketException)
            {
                Console.WriteLine("SKIPPED T11/T12: UDP 7654 is held by another process (a running editor or laser app).");
            }

            if (discovery != null)
            using (discovery)
            {
                var bc = new byte[36];
                var mac = new byte[] { 0x00, 0x1A, 0x2B, 0x33, 0x44, 0x55 };
                Array.Copy(mac, bc, 6);
                BitConverter.GetBytes((ushort)2).CopyTo(bc, 6);     // hw version
                BitConverter.GetBytes((ushort)5).CopyTo(bc, 8);     // sw version
                BitConverter.GetBytes((ushort)1799).CopyTo(bc, 10); // buffer capacity
                BitConverter.GetBytes((uint)20000).CopyTo(bc, 12);  // max point rate
                using var sender = new UdpClient();
                await sender.SendAsync(bc, bc.Length, new IPEndPoint(IPAddress.Loopback, 7654));
                await Task.Delay(300);
                var devices = discovery.GetAvailableDevices().ToList();
                var dev = devices.Where(d => d.Ip == "127.0.0.1").Cast<DacDto?>().FirstOrDefault();
                Check(dev != null, "broadcast discovered as device 127.0.0.1");
                if (dev != null)
                {
                    var devDto = dev.Value;
                    var name = DeviceDiscovery.GetDeviceName(devDto);
                    Check(name == "Ether Dream 334455", $"device name parsed from MAC ('{name}')");
                    Check(DacBroadcast.GetBufferCapacity(devDto) == 1799 && DacBroadcast.GetMaxPointRate(devDto) == 20000,
                          "identity fields parsed (capacity/max rate)");

                    // ---- T12: Dac from discovered dto honors MaxPointRate clamp ----
                    j = _journal.Count;
                    WaitForSessionRelease();
                    var dac12 = new Dac(devDto);
                    dac12.StreamPoints(frame2, 30000);
                    var t12 = JournalSince(j);
                    var b12 = t12.FirstOrDefault(e => e.Cmd == (byte)'b');
                    Check(b12.Cmd == (byte)'b' && BitConverter.ToUInt32(b12.Body, 2) == 20000, "begin rate clamped to advertised 20000 (asked 30000)");
                    dac12.Dispose();
                }
            }

            // ---- T13: operator send-loop simulation across kill/restart ----
            var cts13 = new CancellationTokenSource();
            var loop13 = Task.Run(() => OperatorStyleLoop(frame2, cts13.Token));
            await Task.Delay(800);
            var phaseA = Interlocked.Read(ref _ackedPoints);
            StopServer();
            await Task.Delay(700);
            StartServer();
            await Task.Delay(1500);
            var phaseB = Interlocked.Read(ref _ackedPoints);
            cts13.Cancel();
            var total13 = 0L;
            try
            {
                total13 = await loop13;
            }
            catch (OperationCanceledException)
            {
                // cancelled while connecting - fine
            }

            Check(phaseA > 0, $"points streamed before failure (acked={phaseA})");
            Check(phaseB > phaseA + 10, $"output recovered after restart without user action (acked {phaseA} -> {phaseB})");
            Check(total13 >= (phaseB - phaseA) - 10 && total13 > 100,
                  $"loop send counter consistent with device acks (sent={total13}, recovered acks={phaseB - phaseA})");

            // ---- T14: single-host exclusivity (official protocol point 2) ----
            await Task.Delay(200); // let the server release T13's session
            WaitForSessionRelease();
            var dacA = new Dac("127.0.0.1");
            dacA.StreamPoints(frame2, 30000);
            var rejectsBefore = Interlocked.Read(ref _rejectedConnections);
            var rejectedCount = 0;
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    var dacB = new Dac("127.0.0.1");
                    dacB.Dispose();
                }
                catch (Exception)
                {
                    rejectedCount++;
                }

                await Task.Delay(50);
            }

            Check(rejectedCount >= 2, $"second host rejected while a session is active ({rejectedCount}/3 attempts failed)");
            Check(Interlocked.Read(ref _rejectedConnections) > rejectsBefore, "server recorded the rejections");
            var beforeA = Interlocked.Read(ref _ackedPoints);
            dacA.StreamPoints(frame2, 30000);
            Check(Interlocked.Read(ref _ackedPoints) == beforeA + 2, "original session still streams while others are rejected");
            dacA.Dispose();
            await Task.Delay(200); // let the server release the session
            WaitForSessionRelease();
            var dacC = new Dac("127.0.0.1");
            dacC.StreamPoints(frame2, 30000);
            Check(true, "new host connects after the previous session ended");
            dacC.Dispose();

            // ---- T15: E-stop and remote recovery via ClearEStop ----
            WaitForSessionRelease();
            var dac15 = new Dac("127.0.0.1");
            dac15.StreamPoints(frame2, 30000);
            dac15.EStop();
            await Task.Delay(100);
            InvalidOperationException? estopEx = null;
            try
            {
                dac15.StreamPoints(frame2, 30000);
            }
            catch (InvalidOperationException e)
            {
                estopEx = e;
            }

            Check(estopEx != null && estopEx.Message.Contains("EmergencyStop"),
                  $"streaming refused while E-stop is active ('{estopEx?.Message}')");
            dac15.ClearEStop();
            await Task.Delay(100);
            var before15 = Interlocked.Read(ref _ackedPoints);
            dac15.StreamPoints(frame2, 30000);
            Check(Interlocked.Read(ref _ackedPoints) == before15 + 2, "streaming resumes after ClearEStop");
            dac15.Dispose();

            // ---- Packet coverage ----
            var cmds = _journal.ToArray().Select(e => e.Cmd).Distinct().OrderBy(c => c).ToList();
            var expected = new byte[] { 0x00, (byte)'?', (byte)'b', (byte)'c', (byte)'d', (byte)'p', (byte)'q', (byte)'s' }
                           .Select(b => (int)b).OrderBy(x => x).ToList();
            Check(cmds.OrderBy(c => c).SequenceEqual(expected.Select(c => (byte)c)),
                  $"all 8 command types exercised: {string.Join(", ", cmds.Select(c => $"0x{c:X2}"))}");
        }
        finally
        {
            StopServer();
        }
    }
}
