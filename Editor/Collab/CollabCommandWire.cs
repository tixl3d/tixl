#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Collab;

/// <summary>
/// Converts undoable editor commands into compact wire ops and applies received ops to the local
/// graph. Encoders read the command's own data via internal accessors added to the command classes;
/// decoders rebuild the equivalent command from GUIDs plus plain data and execute it directly.
///
/// Remote ops deliberately bypass <see cref="UndoRedoStack"/>: they call command.Do() without Add(),
/// so a remote edit neither enters the local undo history nor re-triggers the broadcast hook (the
/// hook lives in UndoRedoStack.Add). Apply runs on the UI thread only.
/// </summary>
internal static class CollabCommandWire
{
    private static readonly HashSet<string> _warnedOnce = [];

    /// <summary>Encodes a locally executed command into a wire op, or null when it can't be replicated.</summary>
    public static JObject? EncodeCommand(ICommand command)
    {
        try
        {
            return command switch
                       {
                           MacroCommand macro          => EncodeMacro(macro),
                           ChangeInputValueCommand iv  => EncodeInputValue(iv),
                           AddSymbolChildCommand ac    => EncodeAddChild(ac),
                           DeleteSymbolChildrenCommand dc => EncodeDeleteChildren(dc),
                           AddConnectionCommand aconn  => EncodeConnection("add_conn", aconn.ConnectionForCollab, aconn.CompositionSymbolIdForCollab, aconn.MultiInputIndexForCollab),
                           DeleteConnectionCommand dconn => EncodeConnection("del_conn", dconn.ConnectionForCollab, dconn.CompositionSymbolIdForCollab, dconn.MultiInputIndexForCollab),
                           ChangeSymbolChildNameCommand rn => EncodeRename(rn),
                           ModifyCanvasElementsCommand mv => EncodeMove(mv),
                           _                           => WarnUnencoded(command),
                       };
        }
        catch (Exception e)
        {
            // Encoding must never break the local edit that produced the command.
            Log.Warning($"Collab: failed to encode {command.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static JObject? WarnUnencoded(ICommand command)
    {
        var typeName = command.GetType().Name;
        if (_warnedOnce.Add(typeName))
            Log.Debug($"Collab: {typeName} has no wire encoder and is not synced");

        return null;
    }

    private static JObject? EncodeMacro(MacroCommand macro)
    {
        var ops = new JArray();
        foreach (var subCommand in macro.CommandsForCollab)
        {
            var encoded = EncodeCommand(subCommand);
            if (encoded != null)
                ops.Add(encoded);
        }

        if (ops.Count == 0)
            return null;

        return new JObject
                   {
                       ["type"] = "batch",
                       ["ops"] = ops,
                   };
    }

    private static JObject? EncodeInputValue(ChangeInputValueCommand command)
    {
        // Animated values replay as keyframe edits which need the receiving animator state to match;
        // out of scope for this pass. The plain value path covers the common parameter editing case.
        if (command.WasAnimatedForCollab)
            return WarnUnencoded(command);

        return new JObject
                   {
                       ["type"] = "set_input",
                       ["c"] = command.CompositionSymbolIdForCollab,
                       ["ch"] = command.ChildIdForCollab,
                       ["i"] = command.InputIdForCollab,
                       ["v"] = InputValueToToken(command.NewValueForCollab),
                       ["pv"] = InputValueToToken(command.OriginalValueForCollab),
                   };
    }

    private static JObject EncodeAddChild(AddSymbolChildCommand command)
    {
        return new JObject
                   {
                       ["type"] = "add_child",
                       ["c"] = command.ParentSymbolIdForCollab,
                       ["sym"] = command.SymbolIdForCollab,
                       ["id"] = command.AddedChildId,
                       ["x"] = command.PosOnCanvas.X,
                       ["y"] = command.PosOnCanvas.Y,
                       ["w"] = command.Size.X,
                       ["h"] = command.Size.Y,
                       ["name"] = command.ChildName ?? "",
                   };
    }

    private static JObject EncodeDeleteChildren(DeleteSymbolChildrenCommand command)
    {
        var ids = new JArray();
        foreach (var childId in command.RemovedChildIdsForCollab)
            ids.Add(childId);

        // Ship the full pre-delete state so the delete is invertible in the session's global undo.
        var restoreItems = new JArray();
        foreach (var restoreData in command.CaptureCollabRestoreData())
        {
            restoreItems.Add(ChildRestoreToJson(restoreData));
        }

        return new JObject
                   {
                       ["type"] = "del_children",
                       ["c"] = command.CompositionSymbolIdForCollab,
                       ["ids"] = ids,
                       ["items"] = restoreItems,
                   };
    }

    private static JObject ChildRestoreToJson(CollabChildRestoreData restoreData)
    {
        var inputs = new JObject();
        foreach (var (inputId, value) in restoreData.InputValues)
        {
            // Tokens, not JSON-encoded strings: the restore side feeds these straight into
            // SetValueFromJson, which types them by token kind.
            inputs[inputId.ToString()] = InputValueToToken(value);
        }

        var clips = new JObject();
        foreach (var (outputId, timeClip) in restoreData.TimeClips)
        {
            clips[outputId.ToString()] = TimeClipToToken(timeClip);
        }

        var connections = new JArray();
        foreach (var (connection, multiInputIndex) in restoreData.Connections)
        {
            connections.Add(new JObject
                                {
                                    ["src"] = connection.SourceParentOrChildId,
                                    ["ss"] = connection.SourceSlotId,
                                    ["dst"] = connection.TargetParentOrChildId,
                                    ["ds"] = connection.TargetSlotId,
                                    ["idx"] = multiInputIndex,
                                });
        }

        return new JObject
                   {
                       ["sym"] = restoreData.SymbolId,
                       ["id"] = restoreData.ChildId,
                       ["name"] = restoreData.Name,
                       ["x"] = restoreData.PosOnCanvas.X,
                       ["y"] = restoreData.PosOnCanvas.Y,
                       ["w"] = restoreData.Size.X,
                       ["h"] = restoreData.Size.Y,
                       ["sec"] = restoreData.SectionId,
                       ["inputs"] = inputs,
                       ["clips"] = clips,
                       ["conns"] = connections,
                   };
    }

    private static JObject EncodeConnection(string type, Symbol.Connection connection, Guid compositionId, int multiInputIndex)
    {
        return new JObject
                   {
                       ["type"] = type,
                       ["c"] = compositionId,
                       ["src"] = connection.SourceParentOrChildId,
                       ["ss"] = connection.SourceSlotId,
                       ["dst"] = connection.TargetParentOrChildId,
                       ["ds"] = connection.TargetSlotId,
                       ["idx"] = multiInputIndex,
                   };
    }

    private static JObject EncodeRename(ChangeSymbolChildNameCommand command)
    {
        return new JObject
                   {
                       ["type"] = "rename",
                       ["c"] = command.ParentSymbolIdForCollab,
                       ["ch"] = command.ChildIdForCollab,
                       ["name"] = command.NewName ?? "",
                       ["pn"] = command.OriginalNameForCollab,
                   };
    }

    private static JObject EncodeMove(ModifyCanvasElementsCommand command)
    {
        var items = new JArray();
        var previousItems = new JArray();
        foreach (var entry in command.EntriesForCollab)
        {
            items.Add(new JObject
                          {
                              ["id"] = entry.SelectableId,
                              ["x"] = entry.PosOnCanvas.X,
                              ["y"] = entry.PosOnCanvas.Y,
                              ["w"] = entry.Size.X,
                              ["h"] = entry.Size.Y,
                          });
            previousItems.Add(new JObject
                                  {
                                      ["id"] = entry.SelectableId,
                                      ["x"] = entry.OriginalPosOnCanvas.X,
                                      ["y"] = entry.OriginalPosOnCanvas.Y,
                                      ["w"] = entry.OriginalSize.X,
                                      ["h"] = entry.OriginalSize.Y,
                                  });
        }

        return new JObject
                   {
                       ["type"] = "move",
                       ["c"] = command.CompositionSymbolIdForCollab,
                       ["items"] = items,
                       ["pitems"] = previousItems,
                   };
    }

    // ----------------------------------------------------------------------------------------
    // Decoding and applying
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Applies a wire op to the local graph. Every op is individually guarded: a failed op logs and
    /// skips instead of desyncing or killing the session (e.g. when a client briefly misses an
    /// operator type another user just added).
    /// </summary>
    /// <param name="markModified">False for values a render node receives from its master: they are
    /// live show state, not local edits, so they must not dirty the node's project and feed the
    /// deploy diff (which saves the file, sees a difference and restarts the node).</param>
    public static void ApplyOp(JObject op, bool markModified = true)
    {
        try
        {
            var type = CollabProtocol.GetString(op, "type");
            switch (type)
            {
                case "batch":
                    if (op["ops"] is JArray ops)
                    {
                        foreach (var child in ops)
                        {
                            if (child is JObject subOp)
                                ApplyOp(subOp, markModified);
                        }
                    }

                    break;

                case "set_input":
                    ApplySetInput(op, markModified);
                    break;

                case "add_child":
                    ApplyAddChild(op);
                    break;

                case "del_children":
                    ApplyDeleteChildren(op);
                    break;

                case "add_conn":
                    ApplyConnection(op, true);
                    break;

                case "del_conn":
                    ApplyConnection(op, false);
                    break;

                case "rename":
                    ApplyRename(op);
                    break;

                case "move":
                    ApplyMove(op);
                    break;

                case "restore_children":
                    ApplyRestoreChildren(op);
                    break;

                case "apply_snapshot":
                    ApplySnapshot(op);
                    break;

                default:
                    Log.Debug($"Collab: ignoring unknown op type '{type}'");
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Collab: failed to apply op '{CollabProtocol.GetString(op, "type")}': {e.Message}");
        }
    }

    /// <summary>
    /// Captures the complete state of a composition (children with values, names, sections,
    /// time-clip settings, plus all connections) as one apply_snapshot wire op. Sent to late
    /// joiners so their copy converges to the host's state without requiring identical project
    /// bases.
    /// </summary>
    public static JObject BuildSnapshotOp(SymbolUi compositionUi)
    {
        var compositionSymbol = compositionUi.Symbol;
        var items = new JArray();
        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            items.Add(ChildRestoreToJson(CaptureChildRestoreData(compositionSymbol, childUi)));
        }

        return new JObject
                   {
                       ["type"] = "apply_snapshot",
                       ["c"] = compositionSymbol.Id,
                       ["items"] = items,
                   };
    }

    /// <summary>Target size of one snapshot message, well under <see cref="CollabProtocol.MaxJsonPayload"/>.</summary>
    private const int SnapshotChunkChars = 256 * 1024;

    /// <summary>
    /// The state of a composition as a sequence of ops that each fit into one message: an
    /// apply_snapshot that clears the composition and restores the first children, followed by
    /// restore_children ops for the rest. A connection between children of different chunks is
    /// carried by both ends and gets added when the second end arrives.
    /// </summary>
    public static List<JObject> BuildSnapshotOps(SymbolUi compositionUi)
    {
        var compositionSymbol = compositionUi.Symbol;
        var ops = new List<JObject>();
        var items = new JArray();
        var chars = 0;

        void Flush()
        {
            ops.Add(new JObject
                        {
                            ["type"] = ops.Count == 0 ? "apply_snapshot" : "restore_children",
                            ["c"] = compositionSymbol.Id,
                            ["items"] = items,
                        });
            items = new JArray();
            chars = 0;
        }

        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            var item = ChildRestoreToJson(CaptureChildRestoreData(compositionSymbol, childUi));
            var itemChars = item.ToString(Formatting.None).Length;
            if (itemChars > CollabProtocol.MaxJsonPayload / 2)
            {
                Log.Warning($"Collab: '{childUi.SymbolChild.ReadableName}' is too large to sync ({itemChars / 1024} KiB) and was left out");
                continue;
            }

            if (items.Count > 0 && chars + itemChars > SnapshotChunkChars)
                Flush();

            items.Add(item);
            chars += itemChars;
        }

        // Also for an empty composition: the receiver still has to clear its copy.
        if (items.Count > 0 || ops.Count == 0)
            Flush();

        return ops;
    }

    /// <summary>
    /// Fingerprint of everything <see cref="BuildSnapshotOp"/> would carry for a composition. Host
    /// and clients compare it to detect a drifted copy. It must not depend on enumeration order
    /// (dictionaries and the connection list are filled in a different order on each machine), so
    /// children are hashed in id order, object properties in name order and each child's
    /// connections sorted. Value arrays keep their order — it is part of the value.
    /// </summary>
    public static string ComputeCompositionHash(SymbolUi compositionUi)
    {
        var compositionSymbol = compositionUi.Symbol;
        var lines = new List<string>(compositionUi.ChildUis.Count);
        var builder = new System.Text.StringBuilder();
        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            builder.Clear();
            AppendCanonical(builder, ChildRestoreToJson(CaptureChildRestoreData(compositionSymbol, childUi)));
            lines.Add(builder.ToString());
        }

        lines.Sort(StringComparer.Ordinal);

        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var line in lines)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(line));
            hash.AppendData(_hashLineSeparator);
        }

        return Convert.ToHexString(hash.GetHashAndReset(), 0, 8);
    }

    private static readonly byte[] _hashLineSeparator = [(byte)'\n'];

    private static void AppendCanonical(System.Text.StringBuilder builder, JToken? token)
    {
        if (token == null)
        {
            builder.Append("null");
            return;
        }

        switch (token)
        {
            case JObject obj:
                builder.Append('{');
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    builder.Append(property.Name).Append(':');
                    if (property.Name == "conns" && property.Value is JArray connections)
                    {
                        var entries = new List<string>(connections.Count);
                        var entryBuilder = new System.Text.StringBuilder();
                        foreach (var connection in connections)
                        {
                            entryBuilder.Clear();
                            AppendCanonical(entryBuilder, connection);
                            entries.Add(entryBuilder.ToString());
                        }

                        entries.Sort(StringComparer.Ordinal);
                        builder.Append('[');
                        foreach (var entry in entries)
                            builder.Append(entry).Append(',');

                        builder.Append(']');
                    }
                    else
                    {
                        AppendCanonical(builder, property.Value);
                    }

                    builder.Append(',');
                }

                builder.Append('}');
                break;

            case JArray array:
                builder.Append('[');
                foreach (var element in array)
                {
                    AppendCanonical(builder, element);
                    builder.Append(',');
                }

                builder.Append(']');
                break;

            default:
                builder.Append(token.ToString(Formatting.None));
                break;
        }
    }

    /// <summary>Live capture of one child's state — the snapshot counterpart of the delete command's payload.</summary>
    private static CollabChildRestoreData CaptureChildRestoreData(Symbol compositionSymbol, SymbolUi.Child childUi)
    {
        var child = childUi.SymbolChild;
        var inputValues = new Dictionary<Guid, InputValue>();
        foreach (var (inputId, input) in child.Inputs)
        {
            if (input.IsDefault)
                continue;

            inputValues[inputId] = input.Value.Clone();
        }

        var timeClips = new Dictionary<Guid, TimeClip>();
        foreach (var (outputId, output) in child.Outputs)
        {
            if (output.OutputData is TimeClip timeClip)
                timeClips[outputId] = timeClip.Clone();
        }

        var removedConnections = new List<(Symbol.Connection Connection, int MultiInputIndex)>();
        foreach (var connection in compositionSymbol.Connections)
        {
            if (connection.SourceParentOrChildId != childUi.Id && connection.TargetParentOrChildId != childUi.Id)
                continue;

            removedConnections.Add((connection, compositionSymbol.GetMultiInputIndexFor(connection)));
        }

        return new CollabChildRestoreData
                   {
                       SymbolId = child.Symbol.Id,
                       ChildId = childUi.Id,
                       Name = child.Name,
                       PosOnCanvas = childUi.PosOnCanvas,
                       Size = childUi.Size,
                       SectionId = childUi.SectionId,
                       InputValues = inputValues,
                       TimeClips = timeClips,
                       Connections = removedConnections,
                   };
    }

    /// <summary>
    /// Replaces the local state of one composition with the snapshot's. Used for late joiners:
    /// their local edits to that composition are intentionally replaced by the host's state.
    /// </summary>
    private static void ApplySnapshot(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi) || op["items"] is not JArray items)
            return;

        var allChildren = compositionUi.ChildUis.Values.ToList();
        if (allChildren.Count > 0)
        {
            var clearCommand = new DeleteSymbolChildrenCommand(compositionUi, allChildren);
            clearCommand.Do();
        }

        RestoreChildrenCore(compositionUi, items);
        Log.Debug($"Collab: applied snapshot to composition {compositionUi.Symbol.Id} ({items.Count} children)");
    }

    /// <summary>
    /// Builds the inverse of a wire op, or null when it can't be inverted. Only ops that carry their
    /// previous state (encoded at origin time) are invertible; this powers the session-wide undo.
    /// </summary>
    public static JObject? InvertOp(JObject op)
    {
        try
        {
            var type = CollabProtocol.GetString(op, "type");
            switch (type)
            {
                case "batch":
                {
                    // Undoing a macro reverts its sub-ops in reverse order.
                    if (op["ops"] is not JArray ops || ops.Count == 0)
                        return null;

                    var inverted = new JArray();
                    for (var index = ops.Count - 1; index >= 0; index--)
                    {
                        if (ops[index] is not JObject subOp)
                            continue;

                        var invertedSubOp = InvertOp(subOp);
                        if (invertedSubOp == null)
                            return null;

                        inverted.Add(invertedSubOp);
                    }

                    if (inverted.Count == 0)
                        return null;

                    return new JObject
                               {
                                   ["type"] = "batch",
                                   ["ops"] = inverted,
                               };
                }

                case "set_input":
                    return op["pv"] == null ? null : CloneWith(op, ("v", op["pv"]), ("pv", op["v"]));

                case "rename":
                    return op["pn"] == null ? null : CloneWith(op, ("name", op["pn"]), ("pn", op["name"]));

                case "move":
                    return op["pitems"] == null ? null : CloneWith(op, ("items", op["pitems"]), ("pitems", op["items"]));

                case "add_conn":
                    return CloneWith(op, ("type", "del_conn"));

                case "del_conn":
                    return CloneWith(op, ("type", "add_conn"));

                case "add_child":
                {
                    var addedId = op["id"];
                    if (addedId == null)
                        return null;

                    return new JObject
                               {
                                   ["type"] = "del_children",
                                   ["c"] = op["c"],
                                   ["ids"] = new JArray(addedId),
                               };
                }

                case "del_children":
                    return op["items"] == null ? null : CloneWith(op, ("type", "restore_children"));

                default:
                    return null;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Collab: failed to invert op '{CollabProtocol.GetString(op, "type")}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether the graph still shows what this op did. Per-user undo asks this before reverting
    /// one of a user's edits: when someone else changed the same target since, reverting would
    /// throw their change away, so that edit is skipped instead. Anything that can't be checked
    /// counts as still in effect.
    /// </summary>
    public static bool IsOpStillInEffect(JObject op)
    {
        try
        {
            return IsOpStillInEffect(op, null);
        }
        catch (Exception e)
        {
            Log.Debug($"Collab: could not check op '{CollabProtocol.GetString(op, "type")}': {e.Message}");
            return true;
        }
    }

    /// <param name="superseded">Targets a later op of the same batch already decided; an earlier
    /// op on the same target is expected to be overwritten and is not checked.</param>
    private static bool IsOpStillInEffect(JObject op, HashSet<string>? superseded)
    {
        var type = CollabProtocol.GetString(op, "type");
        switch (type)
        {
            case "batch":
            {
                if (op["ops"] is not JArray ops)
                    return true;

                var decided = superseded ?? new HashSet<string>(StringComparer.Ordinal);
                for (var index = ops.Count - 1; index >= 0; index--)
                {
                    if (ops[index] is JObject subOp && !IsOpStillInEffect(subOp, decided))
                        return false;
                }

                return true;
            }

            case "set_input":
            {
                if (!CollabProtocol.TryGetGuid(op, "ch", out var childId) || !CollabProtocol.TryGetGuid(op, "i", out var inputId))
                    return true;

                if (superseded != null && !superseded.Add($"input|{childId}|{inputId}"))
                    return true;

                if (!TryGetComposition(op, out var compositionUi)
                    || !compositionUi.Symbol.Children.TryGetValue(childId, out var child)
                    || !child.Inputs.TryGetValue(inputId, out var input))
                    return false;

                var expected = op["v"];
                if (expected == null || expected.Type == JTokenType.Null)
                    return true;

                // Compared as JSON text: both sides went through the same writer, while token
                // comparison trips over float-versus-double representations of one value.
                var current = InputValueToToken(input.Value);
                return current != null
                       && string.Equals(current.ToString(Formatting.None), expected.ToString(Formatting.None), StringComparison.Ordinal);
            }

            case "rename":
            {
                if (!CollabProtocol.TryGetGuid(op, "ch", out var childId))
                    return true;

                if (superseded != null && !superseded.Add($"name|{childId}"))
                    return true;

                if (!TryGetComposition(op, out var compositionUi) || !compositionUi.ChildUis.TryGetValue(childId, out var childUi))
                    return false;

                return string.Equals(childUi.SymbolChild.Name ?? "", CollabProtocol.GetString(op, "name"), StringComparison.Ordinal);
            }

            case "move":
            {
                if (!TryGetComposition(op, out var compositionUi) || op["items"] is not JArray items)
                    return true;

                foreach (var token in items)
                {
                    if (token is not JObject item || !CollabProtocol.TryGetGuid(item, "id", out var id))
                        continue;

                    if (superseded != null && !superseded.Add($"pos|{id}"))
                        continue;

                    // Only operators are checked; other canvas elements can't be looked up here.
                    if (!compositionUi.ChildUis.TryGetValue(id, out var childUi))
                        continue;

                    var x = item["x"]?.Value<float>() ?? 0f;
                    var y = item["y"]?.Value<float>() ?? 0f;
                    if (childUi.PosOnCanvas.X != x || childUi.PosOnCanvas.Y != y)
                        return false;
                }

                return true;
            }

            case "add_child":
            {
                if (!CollabProtocol.TryGetGuid(op, "id", out var childId))
                    return true;

                if (superseded != null && !superseded.Add($"child|{childId}"))
                    return true;

                return TryGetComposition(op, out var compositionUi) && compositionUi.Symbol.Children.ContainsKey(childId);
            }

            case "del_children":
            {
                if (!TryGetComposition(op, out var compositionUi) || op["ids"] is not JArray ids)
                    return true;

                foreach (var token in ids)
                {
                    if (!Guid.TryParse(token.ToString(), out var childId))
                        continue;

                    if (superseded != null && !superseded.Add($"child|{childId}"))
                        continue;

                    if (compositionUi.Symbol.Children.ContainsKey(childId))
                        return false;
                }

                return true;
            }

            case "add_conn":
            case "del_conn":
            {
                if (!CollabProtocol.TryGetGuid(op, "src", out var sourceId)
                    || !CollabProtocol.TryGetGuid(op, "ss", out var sourceSlotId)
                    || !CollabProtocol.TryGetGuid(op, "dst", out var targetId)
                    || !CollabProtocol.TryGetGuid(op, "ds", out var targetSlotId))
                    return true;

                if (superseded != null && !superseded.Add($"conn|{sourceId}|{sourceSlotId}|{targetId}|{targetSlotId}"))
                    return true;

                if (!TryGetComposition(op, out var compositionUi))
                    return false;

                var exists = compositionUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == sourceId
                                                                       && c.SourceSlotId == sourceSlotId
                                                                       && c.TargetParentOrChildId == targetId
                                                                       && c.TargetSlotId == targetSlotId);
                return exists == (type == "add_conn");
            }

            default:
                return true;
        }
    }

    private static JObject CloneWith(JObject op, params (string Key, JToken? Value)[] overrides)
    {
        var clone = (JObject)op.DeepClone();
        foreach (var (key, value) in overrides)
        {
            clone[key] = value;
        }

        return clone;
    }

    private static bool TryGetComposition(JObject op, [NotNullWhen(true)] out SymbolUi? compositionUi)
    {
        compositionUi = null;
        return CollabProtocol.TryGetGuid(op, "c", out var compositionId)
               && SymbolUiRegistry.TryGetSymbolUi(compositionId, out compositionUi);
    }

    private static void ApplySetInput(JObject op, bool markModified = true)
    {
        if (!TryGetComposition(op, out var compositionUi)
            || !CollabProtocol.TryGetGuid(op, "ch", out var childId)
            || !CollabProtocol.TryGetGuid(op, "i", out var inputId)
            || op["v"] == null)
            return;

        if (!compositionUi.Symbol.Children.TryGetValue(childId, out var child)
            || !child.Inputs.TryGetValue(inputId, out var input))
            return;

        var valueToken = op["v"];
        if (valueToken == null || valueToken.Type == JTokenType.Null)
            return;

        var newValue = input.Value.Clone();
        try
        {
            newValue.SetValueFromJson(valueToken);
        }
        catch (Exception e)
        {
            // One input whose wire token disagrees with its stored type must not kill the whole
            // op; the log names the input and both types so the mismatch is fixable.
            Log.Warning($"Collab: set_input skipped for {input.InputDefinition.Name} "
                      + $"(wire token {valueToken.Type}, stored type {input.Value.ValueType}): {e.Message}");
            return;
        }

        var command = new ChangeInputValueCommand(compositionUi.Symbol, childId, input, newValue)
                          {
                              MarksModified = markModified,
                          };
        command.Do();
    }

    private static void ApplyAddChild(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi)
            || !CollabProtocol.TryGetGuid(op, "sym", out var symbolId)
            || !CollabProtocol.TryGetGuid(op, "id", out var childId)
            || !SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolToAddUi))
            return;

        if (compositionUi.Symbol.Children.ContainsKey(childId))
            return; // Already added (e.g. replayed sync op).

        var x = op["x"]?.Value<float>() ?? 0f;
        var y = op["y"]?.Value<float>() ?? 0f;
        var w = op["w"]?.Value<float>() ?? SymbolUi.Child.DefaultOpSize.X;
        var h = op["h"]?.Value<float>() ?? SymbolUi.Child.DefaultOpSize.Y;

        var command = new AddSymbolChildCommand(compositionUi.Symbol, symbolId, childId)
                          {
                              PosOnCanvas = new Vector2(x, y),
                              Size = new Vector2(w, h),
                              ChildName = CollabProtocol.GetString(op, "name"),
                          };
        command.Do();
    }

    private static void ApplyDeleteChildren(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi) || op["ids"] is not JArray ids)
            return;

        var childrenToRemove = new List<SymbolUi.Child>();
        foreach (var token in ids)
        {
            if (!Guid.TryParse(token.Value<string>(), out var childId))
                continue;

            // A child may already be gone (client-side deletes race). Deleting the remainder is
            // still the correct outcome.
            if (compositionUi.ChildUis.TryGetValue(childId, out var childUi))
                childrenToRemove.Add(childUi);
        }

        if (childrenToRemove.Count == 0)
            return;

        var command = new DeleteSymbolChildrenCommand(compositionUi, childrenToRemove);
        command.Do();
    }

    private static void ApplyConnection(JObject op, bool add)
    {
        if (!TryGetComposition(op, out var compositionUi)
            || !CollabProtocol.TryGetGuid(op, "src", out var sourceId)
            || !CollabProtocol.TryGetGuid(op, "ss", out var sourceSlotId)
            || !CollabProtocol.TryGetGuid(op, "dst", out var targetId)
            || !CollabProtocol.TryGetGuid(op, "ds", out var targetSlotId))
            return;

        var multiInputIndex = op["idx"]?.Value<int>() ?? 0;
        var connection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
        var symbol = compositionUi.Symbol;

        if (add)
        {
            // A concurrent delete may have removed an endpoint before this op was ordered behind
            // it. Guid.Empty addresses the composition's own inputs/outputs.
            if ((sourceId != Guid.Empty && !symbol.Children.ContainsKey(sourceId))
                || (targetId != Guid.Empty && !symbol.Children.ContainsKey(targetId)))
                return;

            // Re-adding an existing connection is a no-op in Symbol.AddConnection's normal index
            // handling, but skip explicitly to keep the sync replay idempotent.
            if (compositionUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == sourceId
                                                           && c.SourceSlotId == sourceSlotId
                                                           && c.TargetParentOrChildId == targetId
                                                           && c.TargetSlotId == targetSlotId))
                return;

            var command = new AddConnectionCommand(compositionUi.Symbol, connection, multiInputIndex);
            command.Do();
        }
        else
        {
            // Symbol.RemoveConnection removes whatever sits at (target slot, index). After a
            // concurrent edit, or when an op is applied a second time, that can be a different
            // connection — so resolve the index of the connection this op actually names and do
            // nothing when it is already gone.
            var atInput = new List<Symbol.Connection>();
            foreach (var existing in symbol.Connections)
            {
                if (existing.TargetParentOrChildId == targetId && existing.TargetSlotId == targetSlotId)
                    atInput.Add(existing);
            }

            bool IsNamedConnection(Symbol.Connection c) => c.SourceParentOrChildId == sourceId && c.SourceSlotId == sourceSlotId;

            var index = multiInputIndex >= 0 && multiInputIndex < atInput.Count && IsNamedConnection(atInput[multiInputIndex])
                            ? multiInputIndex
                            : atInput.FindIndex(IsNamedConnection);
            if (index < 0)
                return;

            var command = new DeleteConnectionCommand(symbol, connection, index);
            command.Do();
        }
    }

    private static void ApplyRename(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi)
            || !CollabProtocol.TryGetGuid(op, "ch", out var childId)
            || !compositionUi.ChildUis.TryGetValue(childId, out var childUi))
            return;

        var command = new ChangeSymbolChildNameCommand(childUi, compositionUi.Symbol)
                          {
                              NewName = CollabProtocol.GetString(op, "name"),
                          };
        command.Do();
    }

    private static void ApplyMove(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi) || op["items"] is not JArray items)
            return;

        var targets = new List<(Guid Id, Vector2 Pos, Vector2 Size)>();
        foreach (var token in items)
        {
            if (token is not JObject item)
                continue;

            if (!CollabProtocol.TryGetGuid(item, "id", out var id))
                continue;

            targets.Add((id,
                         new Vector2(item["x"]?.Value<float>() ?? 0, item["y"]?.Value<float>() ?? 0),
                         new Vector2(item["w"]?.Value<float>() ?? 100, item["h"]?.Value<float>() ?? 100)));
        }

        if (targets.Count == 0)
            return;

        var command = ModifyCanvasElementsCommand.CreateForCollab(compositionUi.Symbol.Id, targets);
        command.Do();
    }

    // ----------------------------------------------------------------------------------------
    // Value serialization helpers
    // ----------------------------------------------------------------------------------------

    private static string InputValueToJson(InputValue value)
    {
        if (value == null)
            return "null";

        using var stringWriter = new StringWriter();
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            value.ToJson(jsonWriter);
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Serializes an input value as a real JSON token. The receiving side hands this token straight
    /// to <c>InputValue.SetValueFromJson</c>, whose converters expect a number/string/object — a
    /// JSON-encoded *string* reads as the wrong token type and silently becomes the type default.
    /// </summary>
    public static JToken? InputValueToToken(InputValue value)
    {
        if (value == null)
            return JValue.CreateNull();

        var json = InputValueToJson(value);
        return string.IsNullOrEmpty(json) || json == "null" ? JValue.CreateNull() : JToken.Parse(json);
    }

    /// <summary>
    /// All non-default input values of a composition as <c>set_input</c> ops. Used when a render node
    /// joins so it starts from the master's live values, not from the ones last saved to disk.
    /// </summary>
    public static JArray BuildValueOps(SymbolUi compositionUi)
    {
        var ops = new JArray();
        foreach (var childUi in compositionUi.ChildUis.Values)
        {
            var child = childUi.SymbolChild;
            foreach (var (inputId, input) in child.Inputs)
            {
                if (input.IsDefault)
                    continue;

                var token = InputValueToToken(input.Value);
                if (token == null)
                    continue;

                ops.Add(new JObject
                            {
                                ["type"] = "set_input",
                                ["c"] = compositionUi.Symbol.Id,
                                ["ch"] = child.Id,
                                ["i"] = inputId,
                                ["v"] = token,
                            });
            }
        }

        return ops;
    }

    private static string TimeClipToJson(TimeClip timeClip)
    {
        using var stringWriter = new StringWriter();
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            timeClip.ToJson(jsonWriter);
        }

        return stringWriter.ToString();
    }

    /// <summary><see cref="TimeClip.ReadFromJson"/> expects a JSON object, not its text.</summary>
    private static JToken TimeClipToToken(TimeClip timeClip)
    {
        var json = TimeClipToJson(timeClip);
        return string.IsNullOrEmpty(json) || json == "null" ? JValue.CreateNull() : JToken.Parse(json);
    }

    /// <summary>
    /// Re-creates children removed by a del_children op from its restore payload (global undo of a
    /// delete). Mirrors DeleteSymbolChildrenCommand.Undo: re-add with stored ids, then name,
    /// section, input values and time-clip output settings.
    /// </summary>
    private static void ApplyRestoreChildren(JObject op)
    {
        if (!TryGetComposition(op, out var compositionUi) || op["items"] is not JArray items)
            return;

        RestoreChildrenCore(compositionUi, items);
    }

    /// <summary>Shared body of restore_children and apply_snapshot.</summary>
    private static void RestoreChildrenCore(SymbolUi compositionUi, JArray items)
    {
        // Connections are re-added after all children exist, since restored children may wire to
        // each other.
        var pendingConnections = new List<(Symbol.Connection Connection, int MultiInputIndex)>();

        foreach (var token in items)
        {
            try
            {
                if (token is not JObject item)
                    continue;

                if (!CollabProtocol.TryGetGuid(item, "sym", out var symbolId)
                    || !CollabProtocol.TryGetGuid(item, "id", out var childId)
                    || !SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolToAddUi))
                    continue;

                if (compositionUi.Symbol.Children.ContainsKey(childId))
                    continue;

                var symbolChildUi = compositionUi.AddChild(symbolToAddUi.Symbol,
                                                           childId,
                                                           new Vector2(item["x"]?.Value<float>() ?? 0, item["y"]?.Value<float>() ?? 0),
                                                           new Vector2(item["w"]?.Value<float>() ?? 100, item["h"]?.Value<float>() ?? 100));
                if (symbolChildUi == null)
                    continue;

                if (CollabProtocol.TryGetGuid(item, "sec", out var sectionId))
                    symbolChildUi.SectionId = sectionId;

                var child = symbolChildUi.SymbolChild;
                child.Name = CollabProtocol.GetString(item, "name");

                if (item["inputs"] is JObject inputs)
                {
                    foreach (var (inputIdText, valueToken) in inputs)
                    {
                        if (!Guid.TryParse(inputIdText, out var inputId)
                            || !child.Inputs.TryGetValue(inputId, out var input)
                            || valueToken == null
                            || valueToken.Type == JTokenType.Null)
                            continue;

                        input.Value.SetValueFromJson(valueToken);
                        input.IsDefault = false;
                    }
                }

                if (item["clips"] is JObject clips)
                {
                    foreach (var (outputIdText, clipToken) in clips)
                    {
                        if (!Guid.TryParse(outputIdText, out var outputId)
                            || !child.Outputs.TryGetValue(outputId, out var output)
                            || output.OutputData is not TimeClip timeClip
                            || clipToken == null)
                            continue;

                        timeClip.ReadFromJson(clipToken);
                    }
                }

                if (item["conns"] is JArray connections)
                {
                    foreach (var connectionToken in connections)
                    {
                        if (connectionToken is not JObject connectionJson)
                            continue;

                        if (!CollabProtocol.TryGetGuid(connectionJson, "src", out var sourceId)
                            || !CollabProtocol.TryGetGuid(connectionJson, "ss", out var sourceSlotId)
                            || !CollabProtocol.TryGetGuid(connectionJson, "dst", out var targetId)
                            || !CollabProtocol.TryGetGuid(connectionJson, "ds", out var targetSlotId))
                            continue;

                        var connection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
                        pendingConnections.Add((connection, connectionJson["idx"]?.Value<int>() ?? 0));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Collab: failed to restore child during undo: {e.Message}");
            }
        }

        // Lower multi-input indices first, so each connection lands at the index it names.
        pendingConnections.Sort((a, b) => a.MultiInputIndex.CompareTo(b.MultiInputIndex));

        foreach (var (connection, multiInputIndex) in pendingConnections)
        {
            // An end that is not here (yet): with a snapshot sent in several parts the other end
            // arrives in a later part and brings the connection with it.
            var children = compositionUi.Symbol.Children;
            if ((connection.SourceParentOrChildId != Guid.Empty && !children.ContainsKey(connection.SourceParentOrChildId))
                || (connection.TargetParentOrChildId != Guid.Empty && !children.ContainsKey(connection.TargetParentOrChildId)))
                continue;

            // Re-adding an existing connection is skipped to keep replays idempotent.
            if (compositionUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == connection.SourceParentOrChildId
                                                           && c.SourceSlotId == connection.SourceSlotId
                                                           && c.TargetParentOrChildId == connection.TargetParentOrChildId
                                                           && c.TargetSlotId == connection.TargetSlotId))
                continue;

            compositionUi.Symbol.AddConnection(connection, multiInputIndex);
        }

        compositionUi.FlagAsModified();
    }
}
