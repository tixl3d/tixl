#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.App.DebugProtocol;

/// <summary>
/// Local debug server (JSON-lines over TCP on 127.0.0.1) giving external clients read and
/// control access to the running editor. Opt-in via <c>--debug-server &lt;port&gt;</c>.
///
/// Socket threads only read lines and enqueue them; parsing, execution, and response
/// serialization all happen on the main thread once per frame (see
/// <see cref="ProcessMainThreadQueue"/>), so request handlers may touch model state
/// without locks. Handlers may respond asynchronously (e.g. screenshot readback) by
/// holding on to their <see cref="RequestContext"/>. Every response envelope carries the
/// ui frame count, playback frame count, and the global structure version, so clients
/// always know what state they observed.
/// </summary>
internal static class DebugServer
{
    public const int ProtocolVersion = 1;

    public static bool IsRunning => _listener != null;

    public static void Start(int port)
    {
        if (_listener != null)
            return;

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (SocketException e)
        {
            Log.Error($"Debug server: failed to listen on 127.0.0.1:{port} - {e.Message}");
            return;
        }

        _listener = listener;
        Log.AddWriter(DebugLogBuffer.Instance);
        _cancellation = new CancellationTokenSource();
        _ = Task.Run(() => AcceptConnectionsAsync(listener, _cancellation.Token));
        Log.Info($"Debug server listening on 127.0.0.1:{port}");
    }

    public static void Stop()
    {
        if (_listener == null)
            return;

        _cancellation?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Listener already torn down - nothing to release.
        }

        foreach (var client in _clients.Values)
        {
            client.Close();
        }

        _clients.Clear();
        Log.RemoveWriter(DebugLogBuffer.Instance);
        _listener = null;
    }

    /// <summary>
    /// Drains and executes queued requests. Called once per frame from T3Ui.ProcessFrame,
    /// after ImGui.NewFrame (input latched) and before any window drawing.
    /// </summary>
    public static void ProcessMainThreadQueue()
    {
        if (_listener == null)
            return;

        while (_requestQueue.TryDequeue(out var request))
        {
            HandleRequestLine(request.Client, request.Line);
        }
    }

    private static async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await listener.AcceptTcpClientAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }

            var connection = new ClientConnection(tcpClient);
            _clients[connection.Id] = connection;
            _ = Task.Run(() => connection.ReadLoopAsync(cancellation));
        }
    }

    private static void HandleRequestLine(ClientConnection client, string line)
    {
        var context = new RequestContext(client, null);
        JObject request;
        try
        {
            request = JObject.Parse(line);
        }
        catch (JsonReaderException e)
        {
            context.SendError("PARSE_ERROR", e.Message);
            return;
        }

        context = new RequestContext(client, request["id"]);
        try
        {
            ExecuteMethod(request["method"]?.Value<string>(), request, context);
        }
        catch (Exception e)
        {
            context.SendError("INTERNAL_ERROR", e.ToString());
        }
    }

    private static void ExecuteMethod(string? method, JObject request, RequestContext context)
    {
        switch (method)
        {
            case "ping":
                context.SendOk(new JObject());
                break;

            case "getVersion":
                context.SendOk(new JObject
                                   {
                                       ["protocolVersion"] = ProtocolVersion,
                                       ["editorVersion"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                                   });
                break;

            case "shutdown":
                context.SendOk(new JObject());
                // Same path as the exit dialog's confirmed Exit button; unsaved changes are discarded.
                T3.Editor.SystemUi.EditorUi.Instance.ExitApplication();
                break;

            case "getStructureVersion":
                context.SendOk(new JObject
                                   {
                                       ["symbolStructureVersion"] = EditorSymbolPackage.SymbolStructureVersionCounter,
                                   });
                break;

            case "getLogTail":
                HandleGetLogTail(request, context);
                break;

            case "getContext":
                HandleGetContext(context);
                break;

            case "getGraphState":
                HandleGetGraphState(request, context);
                break;

            case "getMetrics":
                HandleGetMetrics(context);
                break;

            case "screenshot":
                HandleScreenshot(request, context);
                break;

            case null:
                context.SendError("MISSING_METHOD", "Request has no 'method' field");
                break;

            default:
                context.SendError("UNKNOWN_METHOD", $"Unknown method '{method}'");
                break;
        }
    }

    private static void HandleGetLogTail(JObject request, RequestContext context)
    {
        var sinceSeq = request["sinceSeq"]?.Value<long>() ?? -1;
        var maxCount = request["maxCount"]?.Value<int>() ?? 200;
        var minLevel = ParseMinLevel(request["minLevel"]?.Value<string>());

        var entries = new JArray();
        DebugLogBuffer.Instance.CollectEntries(sinceSeq, minLevel, maxCount, entries,
                                               out var latestSeq, out var oldestAvailableSeq);
        context.SendOk(new JObject
                           {
                               ["entries"] = entries,
                               ["latestSeq"] = latestSeq,
                               ["oldestAvailableSeq"] = oldestAvailableSeq,
                           });
    }

    private static ILogEntry.EntryLevel ParseMinLevel(string? minLevel)
    {
        return minLevel?.ToLowerInvariant() switch
                   {
                       "info"              => ILogEntry.EntryLevel.Info,
                       "warning" or "warn" => ILogEntry.EntryLevel.Warning,
                       "error"             => ILogEntry.EntryLevel.Error,
                       _                   => ILogEntry.EntryLevel.Debug,
                   };
    }

    private static void HandleGetContext(RequestContext context)
    {
        var view = ProjectView.Focused;
        var playback = Playback.Current;
        var result = new JObject
                         {
                             ["hasOpenProject"] = view != null,
                             ["time"] = new JObject
                                            {
                                                ["timeInBars"] = playback.TimeInBars,
                                                ["timeInSecs"] = playback.TimeInSecs,
                                                ["playbackSpeed"] = playback.PlaybackSpeed,
                                                ["isPlaying"] = Math.Abs(playback.PlaybackSpeed) > 0.001,
                                                ["bpm"] = playback.Bpm,
                                            },
                         };

        var composition = view?.CompositionInstance;
        if (view != null && composition != null)
        {
            result["compositionSymbolId"] = composition.Symbol.Id.ToString();
            result["compositionName"] = composition.Symbol.Name;
            result["compositionPath"] = new JArray(view.Structure.GetReadableInstancePath(composition.InstancePath));

            var selection = new JArray();
            foreach (var childUi in view.NodeSelection.GetSelectedChildUis())
            {
                selection.Add(new JObject
                                  {
                                      ["childId"] = childUi.Id.ToString(),
                                      ["name"] = childUi.SymbolChild.ReadableName,
                                  });
            }

            result["selectedChildren"] = selection;
        }

        context.SendOk(result);
    }

    private static void HandleGetGraphState(JObject request, RequestContext context)
    {
        Symbol? symbol = null;
        var compositionIdToken = request["compositionId"]?.Value<string>();
        if (compositionIdToken != null)
        {
            if (!Guid.TryParse(compositionIdToken, out var symbolId))
            {
                context.SendError("INVALID_PARAM", $"compositionId '{compositionIdToken}' is not a Guid");
                return;
            }

            if (!SymbolRegistry.TryGetSymbol(symbolId, out symbol))
            {
                context.SendError("NOT_FOUND", $"No symbol with id {symbolId}");
                return;
            }
        }
        else
        {
            symbol = ProjectView.Focused?.CompositionInstance?.Symbol;
            if (symbol == null)
            {
                context.SendError("NO_COMPOSITION", "No composition is focused and no compositionId was given");
                return;
            }
        }

        var includeDefaults = request["includeDefaults"]?.Value<bool>() ?? false;

        var children = new JArray();
        foreach (var (childId, child) in symbol.Children)
        {
            var inputs = new JArray();
            foreach (var (inputId, input) in child.Inputs)
            {
                if (input.IsDefault && !includeDefaults)
                    continue;

                var inputJson = new JObject
                                    {
                                        ["id"] = inputId.ToString(),
                                        ["name"] = input.Name,
                                        ["isDefault"] = input.IsDefault,
                                    };
                inputJson["value"] = TrySerializeInputValue(input.Value);
                inputs.Add(inputJson);
            }

            var childJson = new JObject
                                {
                                    ["childId"] = childId.ToString(),
                                    ["symbolId"] = child.Symbol.Id.ToString(),
                                    ["symbolName"] = child.Symbol.Name,
                                };
            if (child.HasCustomName)
                childJson["name"] = child.Name;
            if (child.IsBypassed)
                childJson["isBypassed"] = true;
            if (child.IsDisabled)
                childJson["isDisabled"] = true;
            childJson["inputs"] = inputs;

            children.Add(childJson);
        }

        var connections = new JArray();
        foreach (var connection in symbol.Connections)
        {
            connections.Add(new JObject
                                {
                                    ["sourceParentOrChildId"] = connection.SourceParentOrChildId.ToString(),
                                    ["sourceSlotId"] = connection.SourceSlotId.ToString(),
                                    ["targetParentOrChildId"] = connection.TargetParentOrChildId.ToString(),
                                    ["targetSlotId"] = connection.TargetSlotId.ToString(),
                                });
        }

        context.SendOk(new JObject
                           {
                               ["symbolId"] = symbol.Id.ToString(),
                               ["symbolName"] = symbol.Name,
                               ["children"] = children,
                               ["connections"] = connections,
                           });
    }

    private static JToken TrySerializeInputValue(InputValue value)
    {
        try
        {
            var stringWriter = new StringWriter();
            using var jsonWriter = new JsonTextWriter(stringWriter);
            value.ToJson(jsonWriter);
            jsonWriter.Flush();
            var text = stringWriter.ToString();
            return string.IsNullOrEmpty(text) ? JValue.CreateNull() : JToken.Parse(text);
        }
        catch (Exception)
        {
            return new JObject { ["unserializable"] = value.ValueType.Name };
        }
    }

    private static void HandleGetMetrics(RequestContext context)
    {
        var io = ImGui.GetIO();
        var result = new JObject
                         {
                             ["frameDeltaSeconds"] = io.DeltaTime,
                             ["fps"] = io.DeltaTime > 0 ? Math.Round(1f / io.DeltaTime, 1) : 0,
                             ["gcTotalMemoryMb"] = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
                         };

        var renderStats = new JObject();
        foreach (var (name, count) in RenderStatsCollector.ResultsForLastFrame)
        {
            renderStats[name] = count;
        }

        result["renderStats"] = renderStats;
        result["gpuMemory"] = TryGetGpuMemoryInfo();
        context.SendOk(result);
    }

    private static JToken TryGetGpuMemoryInfo()
    {
        try
        {
            if (_gpuAdapter == null)
            {
                using var dxgiDevice = T3.Core.Resource.ResourceManager.Device.QueryInterface<SharpDX.DXGI.Device>();
                using var adapter = dxgiDevice.Adapter;
                _gpuAdapter = adapter.QueryInterface<SharpDX.DXGI.Adapter3>();
            }

            var info = _gpuAdapter.QueryVideoMemoryInfo(0, SharpDX.DXGI.MemorySegmentGroup.Local);
            return new JObject
                       {
                           ["currentUsageMb"] = Math.Round(info.CurrentUsage / (1024.0 * 1024.0), 1),
                           ["budgetMb"] = Math.Round(info.Budget / (1024.0 * 1024.0), 1),
                       };
        }
        catch (Exception)
        {
            return JValue.CreateNull();
        }
    }

    private static void HandleScreenshot(JObject request, RequestContext context)
    {
        var path = request["path"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            context.SendError("MISSING_PARAM", "screenshot requires a 'path'");
            return;
        }

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            context.SendError("NO_OUTPUT", "No output window is open");
            return;
        }

        var texture = outputWindow.GetCurrentTexture();
        if (texture == null)
        {
            context.SendError("NO_OUTPUT", "Output window has no current texture (is a renderable op pinned?)");
            return;
        }

        var format = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         ? ScreenshotWriter.FileFormats.Jpg
                         : ScreenshotWriter.FileFormats.Png;

        // Completes on a later frame from ScreenshotWriter.Update (main thread), so the
        // deferred response is safe to build there.
        var started = ScreenshotWriter.StartSavingToFile(texture, path, format,
                                                         onComplete: filename =>
                                                         {
                                                             if (filename != null)
                                                                 context.SendOk(new JObject { ["path"] = filename });
                                                             else
                                                                 context.SendError("SCREENSHOT_FAILED", "Readback or encoding failed - see log");
                                                         });
        if (!started)
        {
            context.SendError("SCREENSHOT_BUSY", "Screenshot queue rejected the request");
        }
    }

    private static JObject StampEnvelope(JObject response, JToken? id)
    {
        response["id"] = id;
        response["frame"] = ImGui.GetFrameCount();
        response["playbackFrame"] = Playback.FrameCount;
        response["structureVersion"] = SymbolUi.GlobalVersionCounter;
        return response;
    }

    private sealed class RequestContext
    {
        public RequestContext(ClientConnection client, JToken? id)
        {
            _client = client;
            _id = id;
        }

        public void SendOk(JObject result)
        {
            _client.Send(StampEnvelope(new JObject
                                           {
                                               ["ok"] = true,
                                               ["result"] = result,
                                           }, _id));
        }

        public void SendError(string code, string detail)
        {
            _client.Send(StampEnvelope(new JObject
                                           {
                                               ["ok"] = false,
                                               ["error"] = new JObject
                                                               {
                                                                   ["code"] = code,
                                                                   ["detail"] = detail,
                                                               },
                                           }, _id));
        }

        private readonly ClientConnection _client;
        private readonly JToken? _id;
    }

    private sealed class ClientConnection
    {
        public ClientConnection(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            var stream = tcpClient.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public Guid Id { get; } = Guid.NewGuid();

        public async Task ReadLoopAsync(CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync(cancellation);
                    if (line == null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    _requestQueue.Enqueue((this, line));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Client went away mid-read - normal disconnect.
            }
            finally
            {
                Close();
                _clients.TryRemove(Id, out _);
            }
        }

        public void Send(JObject response)
        {
            try
            {
                lock (_writeLock)
                {
                    _writer.WriteLine(response.ToString(Formatting.None));
                }
            }
            catch (Exception)
            {
                Close();
                _clients.TryRemove(Id, out _);
            }
        }

        public void Close()
        {
            try
            {
                _tcpClient.Close();
            }
            catch (Exception)
            {
                // Best effort - the socket may already be gone.
            }
        }

        private readonly TcpClient _tcpClient;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _writeLock = new();
    }

    private static TcpListener? _listener;
    private static CancellationTokenSource? _cancellation;
    private static SharpDX.DXGI.Adapter3? _gpuAdapter;
    private static readonly ConcurrentQueue<(ClientConnection Client, string Line)> _requestQueue = new();
    private static readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
}
