#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Compilation;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
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
    public static int Port { get; private set; }
    public static int RequestCount { get; private set; }

    /// <summary>Environment.TickCount64 of the last handled request or sent response — drives the app-bar activity indicator.</summary>
    public static long LastActivityTicksMs { get; private set; }

    /// <summary>Short "method -> outcome" lines of the most recent requests, oldest first. Main-thread only (indicator tooltip).</summary>
    public static IReadOnlyList<string> RecentMessages => _recentMessages;

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
        Port = port;
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

        // Complete pending pumpFrames requests: each call to this method is one frame.
        for (var i = _pendingPumps.Count - 1; i >= 0; i--)
        {
            var pump = _pendingPumps[i];
            pump.FramesRemaining--;
            if (pump.FramesRemaining > 0)
                continue;

            _pendingPumps.RemoveAt(i);
            pump.Context.SendOk(new JObject());
        }

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
        RequestCount++;
        LastActivityTicksMs = Environment.TickCount64;

        JObject request;
        try
        {
            request = JObject.Parse(line);
        }
        catch (JsonReaderException e)
        {
            new RequestContext(client, null, null).SendError("PARSE_ERROR", e.Message);
            return;
        }

        var method = request["method"]?.Value<string>();
        var context = new RequestContext(client, request["id"], method);
        try
        {
            ExecuteMethod(method, request, context);
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

            case "openProject":
                HandleOpenProject(request, context);
                break;

            case "select":
            {
                var view = ProjectView.Focused;
                if (view?.CompositionInstance == null)
                {
                    context.SendError("NO_COMPOSITION", "No composition focused");
                    break;
                }

                if (!Guid.TryParse(request["childId"]?.Value<string>(), out var selectId))
                {
                    context.SendError("MISSING_PARAM", "select requires a 'childId'");
                    break;
                }

                view.NodeSelection.Clear();
                view.NodeSelection.TrySelectCompositionChild(view.CompositionInstance, selectId, false);
                context.SendOk(new JObject());
                break;
            }

            case "setInput":
                HandleSetInput(request, context);
                break;

            case "getOutput":
                HandleGetOutput(request, context);
                break;

            case "pumpFrames":
            {
                var count = Math.Clamp(request["count"]?.Value<int>() ?? 1, 1, 100000);
                _pendingPumps.Add(new PendingPump(context, count));
                break;
            }

            case "newProject":
                HandleNewProject(request, context);
                break;

            case "addOp":
                HandleAddOp(request, context);
                break;

            case "connect":
                HandleConnect(request, context);
                break;

            case "deleteOp":
                HandleDeleteOp(request, context);
                break;

            case "pin":
                HandlePin(request, context);
                break;

            case "reload":
                HandleReload(request, context);
                break;

            case "undo":
                if (UndoRedoStack.CanUndo)
                    UndoRedoStack.Undo();
                context.SendOk(new JObject { ["canUndo"] = UndoRedoStack.CanUndo, ["canRedo"] = UndoRedoStack.CanRedo });
                break;

            case "redo":
                if (UndoRedoStack.CanRedo)
                    UndoRedoStack.Redo();
                context.SendOk(new JObject { ["canUndo"] = UndoRedoStack.CanUndo, ["canRedo"] = UndoRedoStack.CanRedo });
                break;

            case "setTime":
            {
                var playback = Playback.Current;
                if (request["timeInBars"] is { } bars)
                    playback.TimeInBars = bars.Value<double>();
                else if (request["timeInSecs"] is { } secs)
                    playback.TimeInSecs = secs.Value<double>();

                context.SendOk(new JObject
                                   {
                                       ["timeInBars"] = playback.TimeInBars,
                                       ["timeInSecs"] = playback.TimeInSecs,
                                   });
                break;
            }

            case "setPlayback":
            {
                var playback = Playback.Current;
                if (request["speed"] is { } speed)
                    playback.PlaybackSpeed = speed.Value<double>();
                else if (request["playing"] is { } playing)
                    playback.PlaybackSpeed = playing.Value<bool>() ? 1 : 0;

                context.SendOk(new JObject { ["playbackSpeed"] = playback.PlaybackSpeed });
                break;
            }

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

    private static void HandleOpenProject(JObject request, RequestContext context)
    {
        var name = request["name"]?.Value<string>();
        var symbolIdText = request["symbolId"]?.Value<string>();

        EditorSymbolPackage? package = null;
        Guid homeSymbolId = default;
        var useExplicitHome = false;

        if (symbolIdText != null)
        {
            if (!Guid.TryParse(symbolIdText, out homeSymbolId))
            {
                context.SendError("INVALID_PARAM", $"symbolId '{symbolIdText}' is not a Guid");
                return;
            }

            foreach (var p in SymbolPackage.AllPackages)
            {
                if (p is EditorSymbolPackage editorPackage && p.Symbols.ContainsKey(homeSymbolId))
                {
                    package = editorPackage;
                    break;
                }
            }

            if (package == null)
            {
                context.SendError("NOT_FOUND", $"No package contains symbol {homeSymbolId}");
                return;
            }

            useExplicitHome = true;
        }
        else if (name != null)
        {
            foreach (var p in SymbolPackage.AllPackages)
            {
                // Created projects get a "<name> (<namespace>)" display name - accept the short form too.
                if (p is EditorSymbolPackage editorPackage
                    && (string.Equals(editorPackage.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                        || editorPackage.DisplayName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase)))
                {
                    package = editorPackage;
                    break;
                }
            }

            if (package == null)
            {
                context.SendError("NOT_FOUND", $"No project named '{name}'");
                return;
            }
        }
        else
        {
            context.SendError("MISSING_PARAM", "openProject requires 'name' or 'symbolId'");
            return;
        }

        OpenedProject? openedProject;
        string? failureLog;
        var created = useExplicitHome
                          ? OpenedProject.TryCreateWithExplicitHome(package, homeSymbolId, out openedProject, out failureLog)
                          : OpenedProject.TryCreate(package, out openedProject, out failureLog);
        if (!created || openedProject == null)
        {
            context.SendError("OPEN_FAILED", failureLog ?? "unknown reason");
            return;
        }

        // Prefer the visible graph window - hidden instances can exist while the hub is shown.
        GraphWindow? graphWindow = null;
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (!window.Config.Visible)
                continue;

            graphWindow = window;
            break;
        }

        graphWindow ??= GraphWindow.GraphWindowInstances.Count > 0 ? GraphWindow.GraphWindowInstances[0] : null;
        if (graphWindow == null)
        {
            context.SendError("NO_GRAPH_WINDOW", "No graph window available");
            return;
        }

        if (!graphWindow.TrySetToProject(openedProject, tryRestoreViewArea: false))
        {
            context.SendError("OPEN_FAILED", "Graph window rejected the project");
            return;
        }

        var rootInstance = openedProject.Structure.GetRootInstance();
        var pinOutput = request["pinOutput"]?.Value<bool>() ?? true;
        var pinned = false;
        if (pinOutput && rootInstance != null && OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            outputWindow.Pinning.PinInstance(rootInstance, graphWindow.ProjectView);
            pinned = true;
        }

        context.SendOk(new JObject
                           {
                               ["rootSymbolId"] = rootInstance?.Symbol.Id.ToString(),
                               ["rootSymbolName"] = rootInstance?.Symbol.Name,
                               ["pinnedOutput"] = pinned,
                           });
    }

    private static void HandleSetInput(JObject request, RequestContext context)
    {
        var symbol = ProjectView.Focused?.CompositionInstance?.Symbol;
        var compositionIdText = request["compositionId"]?.Value<string>();
        if (compositionIdText != null)
        {
            if (!Guid.TryParse(compositionIdText, out var compositionId) || !SymbolRegistry.TryGetSymbol(compositionId, out symbol))
            {
                context.SendError("NOT_FOUND", $"Unknown composition '{compositionIdText}'");
                return;
            }
        }

        if (symbol == null)
        {
            context.SendError("NO_COMPOSITION", "No composition focused and no compositionId given");
            return;
        }

        if (!Guid.TryParse(request["childId"]?.Value<string>(), out var childId)
            || !symbol.Children.TryGetValue(childId, out var child))
        {
            context.SendError("NOT_FOUND", "Unknown or missing childId");
            return;
        }

        Symbol.Child.Input? input = null;
        if (Guid.TryParse(request["inputId"]?.Value<string>(), out var inputId))
        {
            child.Inputs.TryGetValue(inputId, out input);
        }
        else if (request["inputName"]?.Value<string>() is { } inputName)
        {
            foreach (var candidate in child.Inputs.Values)
            {
                if (string.Equals(candidate.Name, inputName, StringComparison.OrdinalIgnoreCase))
                {
                    input = candidate;
                    break;
                }
            }
        }

        if (input == null)
        {
            context.SendError("NOT_FOUND", "Unknown input - give 'inputId' or 'inputName'");
            return;
        }

        var valueToken = request["value"];
        if (valueToken == null)
        {
            context.SendError("MISSING_PARAM", "setInput requires 'value'");
            return;
        }

        var newValue = input.Value.Clone();
        try
        {
            newValue.SetValueFromJson(valueToken);
        }
        catch (Exception e)
        {
            context.SendError("INVALID_PARAM", $"Can't read value as {input.DefaultValue.ValueType.Name}: {e.Message}");
            return;
        }

        UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(symbol, childId, input, newValue));
        context.SendOk(new JObject { ["input"] = input.Name });
    }

    private static void HandleGetOutput(JObject request, RequestContext context)
    {
        Instance? instance = null;
        var view = ProjectView.Focused;
        if (Guid.TryParse(request["childId"]?.Value<string>(), out var childId))
        {
            view?.CompositionInstance?.Children.TryGetChildInstance(childId, out instance);
        }
        else
        {
            instance = view?.RootInstance;
        }

        if (instance == null)
        {
            context.SendError("NOT_FOUND", "No matching instance (open a project, or give a childId within the focused composition)");
            return;
        }

        ISlot? slot = null;
        if (Guid.TryParse(request["outputId"]?.Value<string>(), out var outputId))
        {
            foreach (var candidate in instance.Outputs)
            {
                if (candidate.Id == outputId)
                {
                    slot = candidate;
                    break;
                }
            }
        }
        else if (instance.Outputs.Count > 0)
        {
            slot = instance.Outputs[0];
        }

        if (slot == null)
        {
            context.SendError("NOT_FOUND", "Instance has no matching output");
            return;
        }

        // Optional pull for outputs nothing else evaluates (e.g. string results with no view
        // pulling them). Forces evaluation - IsDirty is visit-based and unreliable outside the
        // normal traversal. May double-evaluate an op that a visible view already pulls.
        var update = request["update"]?.Value<bool>() ?? false;
        if (update)
        {
            // Same forced-pull sequence [VisualTest] uses: the tick bump defeats
            // InvalidateGraph's once-per-tick visited guard, and the graph-wide
            // invalidation crosses composed-op boundaries (ForceInvalidate alone only
            // dirties this slot; a forwarded inner slot would return its cached value).
            DirtyFlag.IncrementGlobalTicks();
            slot.InvalidateGraph();
            slot.DirtyFlag.ForceInvalidate();
            var wasDirty = slot.DirtyFlag.IsDirty;
            slot.Update(new EvaluationContext());
            Log.Debug($"debug-server: pulled {instance.Symbol.Name}.{slot.ValueType.Name} dirtyBefore={wasDirty} dirtyAfter={slot.DirtyFlag.IsDirty}");
        }

        var result = new JObject
                         {
                             ["outputId"] = slot.Id.ToString(),
                             ["valueType"] = slot.ValueType.Name,
                             ["value"] = slot switch
                                             {
                                                 Slot<string> s  => s.Value,
                                                 Slot<float> s   => s.Value,
                                                 Slot<double> s  => s.Value,
                                                 Slot<int> s     => s.Value,
                                                 Slot<bool> s    => s.Value,
                                                 Slot<Vector2> s => new JArray(s.Value.X, s.Value.Y),
                                                 Slot<Vector3> s => new JArray(s.Value.X, s.Value.Y, s.Value.Z),
                                                 Slot<Vector4> s => new JArray(s.Value.X, s.Value.Y, s.Value.Z, s.Value.W),
                                                 _               => JValue.CreateNull(),
                                             },
                         };
        context.SendOk(result);
    }

    private static void HandleNewProject(JObject request, RequestContext context)
    {
        var name = request["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            context.SendError("MISSING_PARAM", "newProject requires a 'name' (namespace, e.g. 'pixtur._agentTests')");
            return;
        }

        // Scaffolds files and compiles - blocks the frame loop for the duration.
        if (!ProjectSetup.TryCreateProject(name, shareResources: true, out var newProject, out var failureLog))
        {
            context.SendError("CREATE_FAILED", failureLog ?? "unknown reason");
            return;
        }

        Guid homeSymbolId = default;
        if (newProject.AssemblyInformation.TryGetReleaseInfo(out var releaseInfo))
            homeSymbolId = releaseInfo.HomeGuid;

        context.SendOk(new JObject
                           {
                               ["displayName"] = newProject.DisplayName,
                               ["homeSymbolId"] = homeSymbolId.ToString(),
                           });
    }

    private static bool TryResolveCompositionSymbol(JObject request, RequestContext context, out Symbol symbol)
    {
        symbol = null!;
        var compositionIdText = request["compositionId"]?.Value<string>();
        if (compositionIdText != null)
        {
            if (!Guid.TryParse(compositionIdText, out var compositionId)
                || !SymbolRegistry.TryGetSymbol(compositionId, out var resolved))
            {
                context.SendError("NOT_FOUND", $"Unknown composition '{compositionIdText}'");
                return false;
            }

            symbol = resolved;
            return true;
        }

        var focused = ProjectView.Focused?.CompositionInstance?.Symbol;
        if (focused == null)
        {
            context.SendError("NO_COMPOSITION", "No composition focused and no compositionId given");
            return false;
        }

        symbol = focused;
        return true;
    }

    private static void HandleAddOp(JObject request, RequestContext context)
    {
        if (!TryResolveCompositionSymbol(request, context, out var compositionSymbol))
            return;

        Guid symbolIdToAdd = default;
        if (Guid.TryParse(request["symbolId"]?.Value<string>(), out var parsedId))
        {
            symbolIdToAdd = parsedId;
        }
        else if (request["symbolName"]?.Value<string>() is { } symbolName)
        {
            var matches = new List<Symbol>();
            foreach (var package in SymbolPackage.AllPackages)
            {
                foreach (var candidate in package.Symbols.Values)
                {
                    if (string.Equals(candidate.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                context.SendError("NOT_FOUND", $"No symbol named '{symbolName}'");
                return;
            }

            if (matches.Count > 1)
            {
                var candidates = new JArray();
                foreach (var m in matches)
                {
                    candidates.Add($"{m.Name} ({m.SymbolPackage.DisplayName}) {m.Id}");
                }

                context.SendError("AMBIGUOUS", $"Multiple symbols named '{symbolName}': {candidates}");
                return;
            }

            symbolIdToAdd = matches[0].Id;
        }

        if (symbolIdToAdd == default)
        {
            context.SendError("MISSING_PARAM", "addOp requires 'symbolId' or 'symbolName'");
            return;
        }

        var command = new AddSymbolChildCommand(compositionSymbol, symbolIdToAdd)
                          {
                              PosOnCanvas = new Vector2(request["posX"]?.Value<float>() ?? 0,
                                                        request["posY"]?.Value<float>() ?? 0),
                          };
        UndoRedoStack.AddAndExecute(command);
        context.SendOk(new JObject
                           {
                               ["childId"] = command.AddedChildId.ToString(),
                               ["symbolId"] = symbolIdToAdd.ToString(),
                           });
    }

    private static void HandleConnect(JObject request, RequestContext context)
    {
        if (!TryResolveCompositionSymbol(request, context, out var compositionSymbol))
            return;

        if (!Guid.TryParse(request["sourceChildId"]?.Value<string>(), out var sourceChildId)
            || !compositionSymbol.Children.TryGetValue(sourceChildId, out var sourceChild))
        {
            context.SendError("NOT_FOUND", "Unknown or missing sourceChildId");
            return;
        }

        if (!Guid.TryParse(request["targetChildId"]?.Value<string>(), out var targetChildId)
            || !compositionSymbol.Children.TryGetValue(targetChildId, out var targetChild))
        {
            context.SendError("NOT_FOUND", "Unknown or missing targetChildId");
            return;
        }

        // Output: by guid, by name, or the first one.
        Guid outputId = default;
        var sourceOutputText = request["sourceOutput"]?.Value<string>();
        foreach (var outputDef in sourceChild.Symbol.OutputDefinitions)
        {
            if (sourceOutputText == null
                || string.Equals(outputDef.Name, sourceOutputText, StringComparison.OrdinalIgnoreCase)
                || (Guid.TryParse(sourceOutputText, out var g) && outputDef.Id == g))
            {
                outputId = outputDef.Id;
                break;
            }
        }

        // Input: by guid, by name, or the first one.
        Guid inputId = default;
        var targetInputText = request["targetInput"]?.Value<string>();
        foreach (var inputDef in targetChild.Symbol.InputDefinitions)
        {
            if (targetInputText == null
                || string.Equals(inputDef.Name, targetInputText, StringComparison.OrdinalIgnoreCase)
                || (Guid.TryParse(targetInputText, out var g) && inputDef.Id == g))
            {
                inputId = inputDef.Id;
                break;
            }
        }

        if (outputId == default || inputId == default)
        {
            context.SendError("NOT_FOUND", $"Can't resolve output '{sourceOutputText}' or input '{targetInputText}'");
            return;
        }

        var compositionInstance = ProjectView.Focused?.CompositionInstance;
        if (compositionInstance != null
            && compositionInstance.Symbol.Id == compositionSymbol.Id
            && compositionInstance.Children.TryGetChildInstance(sourceChildId, out var sourceInstance)
            && Structure.CheckForCycle(sourceInstance, targetChildId))
        {
            context.SendError("CYCLE", "Connection would create a cycle");
            return;
        }

        var connection = new Symbol.Connection(sourceChildId, outputId, targetChildId, inputId);
        var multiInputIndex = request["multiInputIndex"]?.Value<int>() ?? 0;
        UndoRedoStack.AddAndExecute(new AddConnectionCommand(compositionSymbol, connection, multiInputIndex));
        context.SendOk(new JObject());
    }

    private static void HandleDeleteOp(JObject request, RequestContext context)
    {
        if (!TryResolveCompositionSymbol(request, context, out var compositionSymbol))
            return;

        var compositionUi = compositionSymbol.GetSymbolUi();
        if (!Guid.TryParse(request["childId"]?.Value<string>(), out var childId)
            || !compositionUi.ChildUis.TryGetValue(childId, out var childUi))
        {
            context.SendError("NOT_FOUND", "Unknown or missing childId");
            return;
        }

        UndoRedoStack.AddAndExecute(new DeleteSymbolChildrenCommand(compositionUi, [childUi]));
        context.SendOk(new JObject());
    }

    private static void HandleReload(JObject request, RequestContext context)
    {
        var name = request["project"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            context.SendError("MISSING_PARAM", "reload requires a 'project' (editable project display name)");
            return;
        }

        EditableSymbolProject? project = null;
        foreach (var candidate in EditableSymbolProject.AllProjects)
        {
            if (string.Equals(candidate.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || candidate.DisplayName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase))
            {
                project = candidate;
                break;
            }
        }

        if (project == null)
        {
            context.SendError("NOT_FOUND", $"No editable project '{name}' (built-in packages need an editor restart)");
            return;
        }

        // Blocks the frame loop for the duration of the compile - clients need a generous timeout.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var success = project.TryRecompile(updatePackage: true, out var failureLog);
        stopwatch.Stop();
        if (success)
        {
            context.SendOk(new JObject { ["durationSeconds"] = Math.Round(stopwatch.Elapsed.TotalSeconds, 1) });
        }
        else
        {
            context.SendError("COMPILE_FAILED", failureLog ?? "unknown reason");
        }
    }

    private static void HandlePin(JObject request, RequestContext context)
    {
        var view = ProjectView.Focused;
        var composition = view?.CompositionInstance;
        if (view == null || composition == null)
        {
            context.SendError("NO_COMPOSITION", "No composition focused");
            return;
        }

        if (!Guid.TryParse(request["childId"]?.Value<string>(), out var childId)
            || !composition.Children.TryGetChildInstance(childId, out var instance))
        {
            context.SendError("NOT_FOUND", "Unknown or missing childId");
            return;
        }

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            context.SendError("NO_OUTPUT", "No visible output window");
            return;
        }

        outputWindow.Pinning.PinInstance(instance, view);
        context.SendOk(new JObject());
    }

    private static void RecordMessage(string message)
    {
        if (_recentMessages.Count >= RecentMessageCapacity)
            _recentMessages.RemoveAt(0);

        _recentMessages.Add(message);
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
        public RequestContext(ClientConnection client, JToken? id, string? method)
        {
            _client = client;
            _id = id;
            _method = method;
        }

        public void SendOk(JObject result)
        {
            RecordMessage($"{_method ?? "?"} → ok");
            _client.Send(StampEnvelope(new JObject
                                           {
                                               ["ok"] = true,
                                               ["result"] = result,
                                           }, _id));
        }

        public void SendError(string code, string detail)
        {
            RecordMessage($"{_method ?? "?"} → {code}");
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
        private readonly string? _method;
    }

    private sealed class PendingPump
    {
        public PendingPump(RequestContext context, int framesRemaining)
        {
            Context = context;
            FramesRemaining = framesRemaining;
        }

        public readonly RequestContext Context;
        public int FramesRemaining;
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
            LastActivityTicksMs = Environment.TickCount64;
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

    private const int RecentMessageCapacity = 8;
    private static readonly List<string> _recentMessages = new(RecentMessageCapacity);
    private static readonly List<PendingPump> _pendingPumps = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cancellation;
    private static SharpDX.DXGI.Adapter3? _gpuAdapter;
    private static readonly ConcurrentQueue<(ClientConnection Client, string Line)> _requestQueue = new();
    private static readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
}
