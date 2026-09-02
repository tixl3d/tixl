using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace TiXL.DebugClient;

/// <summary>
/// Typed client for TiXL's JSON-lines debug protocol (editor started with
/// <c>--debug-server &lt;port&gt;</c>). One request/response at a time per client;
/// responses are matched by order, not id. Thread-unsafe by design - use one
/// client per driving thread.
/// </summary>
public sealed class DebugProtocolClient : IDisposable
{
    public static bool TryConnect(int port, TimeSpan timeout, out DebugProtocolClient? client)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var tcpClient = new TcpClient();
                tcpClient.Connect("127.0.0.1", port);
                client = new DebugProtocolClient(tcpClient);
                return true;
            }
            catch (SocketException)
            {
                Thread.Sleep(1000);
            }
        }

        client = null;
        return false;
    }

    private DebugProtocolClient(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        var stream = tcpClient.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _tcpClient.ReceiveTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }

    /// <summary>Sends a request and blocks for the response envelope.</summary>
    public Response Call(string method, object? args = null)
    {
        var request = args == null ? new JObject() : JObject.FromObject(args);
        request["id"] = (++_requestCounter).ToString();
        request["method"] = method;

        _writer.WriteLine(request.ToString(Newtonsoft.Json.Formatting.None));
        var line = _reader.ReadLine() ?? throw new IOException("Debug protocol connection closed");
        var envelope = JObject.Parse(line);

        return new Response(envelope["ok"]?.Value<bool>() ?? false,
                            envelope["result"] as JObject,
                            envelope["error"]?["code"]?.Value<string>(),
                            envelope["error"]?["detail"]?.Value<string>(),
                            envelope["frame"]?.Value<long>() ?? -1,
                            envelope["structureVersion"]?.Value<int>() ?? -1);
    }

    // --- convenience wrappers ----------------------------------------------

    public Response Ping() => Call("ping");
    public void PumpFrames(int count) => Call("pumpFrames", new { count }).Require("pumpFrames");

    /// <summary>Created projects get a "&lt;name&gt; (&lt;namespace&gt;)" display name - tries both forms.</summary>
    public Response OpenProject(string name)
    {
        var response = Call("openProject", new { name });
        if (!response.Ok && name.IndexOf('(') < 0)
        {
            var alternate = Call("openProject", new { name = $"{name} (pixtur.{name})" });
            if (alternate.Ok)
                return alternate;
        }

        return response;
    }

    public Guid AddOp(string symbolName, float posX = 0, float posY = 0)
    {
        var result = Call("addOp", new { symbolName, posX, posY }).Require("addOp");
        return Guid.Parse(result["childId"]!.Value<string>()!);
    }

    public void ConnectOps(Guid sourceChildId, string sourceOutput, Guid targetChildId, string targetInput)
        => Call("connect", new { sourceChildId, sourceOutput, targetChildId, targetInput }).Require("connect");

    public void DeleteOp(Guid childId) => Call("deleteOp", new { childId }).Require("deleteOp");
    public void Pin(Guid childId) => Call("pin", new { childId }).Require("pin");
    public void Select(Guid childId) => Call("select", new { childId }).Require("select");

    public void SetInput(Guid childId, string inputName, JToken value)
        => Call("setInput", new JObject { ["childId"] = childId, ["inputName"] = inputName, ["value"] = value }).Require("setInput");

    /// <summary>Vector4 inputs travel as an X/Y/Z/W object.</summary>
    public void SetVector4Input(Guid childId, string inputName, float x, float y, float z, float w)
        => SetInput(childId, inputName, new JObject { ["X"] = x, ["Y"] = y, ["Z"] = z, ["W"] = w });

    /// <summary>List inputs travel wrapped in a "Values" object.</summary>
    public void SetIntListInput(Guid childId, string inputName, IEnumerable<int> values)
        => SetInput(childId, inputName, new JObject { ["Values"] = new JArray(values) });

    public string? GetOutputValue(Guid? childId = null)
    {
        var result = (childId == null ? Call("getOutput") : Call("getOutput", new { childId })).Require("getOutput");
        return result["value"]?.Type == JTokenType.Null ? null : result["value"]?.Value<string>();
    }

    public JObject GetGraphState(bool includeDefaults = false)
        => Call("getGraphState", new { includeDefaults }).Require("getGraphState");

    public void Screenshot(string path) => Call("screenshot", new { path }).Require("screenshot");
    public Response Reload(string project) => Call("reload", new { project });
    public void Undo() => Call("undo").Require("undo");

    public JArray GetLogTail(long sinceSeq = -1, string minLevel = "debug", int maxCount = 200)
        => (JArray)Call("getLogTail", new { sinceSeq, minLevel, maxCount }).Require("getLogTail")["entries"]!;

    public long GetLatestLogSeq()
        => Call("getLogTail", new { maxCount = 1 }).Require("getLogTail")["latestSeq"]!.Value<long>();

    public JObject GetMetrics() => Call("getMetrics").Require("getMetrics");

    /// <summary>Fire-and-forget: the editor tears down before it can reply.</summary>
    public void Shutdown()
    {
        try
        {
            _writer.WriteLine("""{"id":"shutdown","method":"shutdown"}""");
        }
        catch (IOException)
        {
        }
    }

    public void Dispose() => _tcpClient.Dispose();

    private readonly TcpClient _tcpClient;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _requestCounter;
}

public sealed record Response(bool Ok, JObject? Result, string? ErrorCode, string? ErrorDetail, long Frame, int StructureVersion)
{
    /// <summary>Returns the result payload or throws with the protocol error.</summary>
    public JObject Require(string what)
    {
        if (!Ok)
            throw new DebugProtocolException($"{what} failed: {ErrorCode} - {ErrorDetail}");

        return Result ?? new JObject();
    }
}

public sealed class DebugProtocolException(string message) : Exception(message);
