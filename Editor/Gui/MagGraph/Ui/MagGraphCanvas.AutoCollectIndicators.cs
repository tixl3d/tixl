#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    /// <summary>
    /// Indicates the implicit links auto-collecting players create — a faint type-colored bezier from
    /// every sibling clip they pick up without a wire. Two kinds: [VideoClipPlayer] → [VideoClip]
    /// (texture-colored), and [TimeClipPlayer] → any unwired Command time clip (command-colored).
    /// Brightens when either end is hovered or selected, so the invisible data flow becomes discoverable.
    /// </summary>
    private void DrawAutoCollectIndicators(ImDrawListPtr drawList)
    {
        _autoCollectPlayers.Clear();
        _autoCollectClips.Clear();
        _timeClipPlayers.Clear();
        _commandClips.Clear();
        _commandClipSlotIds.Clear();
        _wiredClipIds.Clear();

        foreach (var item in _context.Layout.Items.Values)
        {
            if (item.Variant != MagGraphItem.Variants.Operator || item.IsCollapsedAway)
                continue;

            var symbolId = item.SymbolChild?.Symbol.Id;
            if (symbolId == _videoClipPlayerSymbolId)
            {
                if (IsAutoCollectEnabled(item, _autoCollectInputId))
                    _autoCollectPlayers.Add(item);
            }
            else if (symbolId == _videoClipSymbolId)
            {
                _autoCollectClips.Add(item);
            }
            else if (symbolId == _timeClipPlayerSymbolId)
            {
                if (IsAutoCollectEnabled(item, _timeClipPlayerAutoCollectInputId))
                    _timeClipPlayers.Add(item);
            }
            else if (TryGetCommandClipSlotId(item, out var clipSlotId))
            {
                _commandClips.Add(item);
                _commandClipSlotIds.Add(clipSlotId);
            }
        }

        DrawVideoClipIndicators(drawList);
        DrawCommandClipIndicators(drawList);
    }

    private void DrawVideoClipIndicators(ImDrawListPtr drawList)
    {
        if (_autoCollectPlayers.Count == 0 || _autoCollectClips.Count == 0)
            return;

        var typeColor = TypeUiRegistry.GetPropertiesForType(typeof(Texture2D)).Color;

        for (var playerIndex = 0; playerIndex < _autoCollectPlayers.Count; playerIndex++)
        {
            var player = _autoCollectPlayers[playerIndex];
            CollectWiredClipIds(player);

            for (var clipIndex = 0; clipIndex < _autoCollectClips.Count; clipIndex++)
            {
                var clip = _autoCollectClips[clipIndex];
                if (_wiredClipIds.Contains(clip.Id))
                    continue;

                DrawIndicatorLine(drawList, clip, player, typeColor);
            }
        }
    }

    /// <summary>
    /// Command-clip pass: unlike the video pass, "wired" means the clip's own time-clip output has ANY
    /// outgoing connection — the runtime then leaves the clip to that consumer (see [TimeClipPlayer]).
    /// </summary>
    private void DrawCommandClipIndicators(ImDrawListPtr drawList)
    {
        if (_timeClipPlayers.Count == 0 || _commandClips.Count == 0)
            return;

        var composition = _context.CompositionInstance;
        if (composition == null)
            return;

        var typeColor = TypeUiRegistry.GetPropertiesForType(typeof(Command)).Color;
        var connections = composition.Symbol.Connections;

        for (var playerIndex = 0; playerIndex < _timeClipPlayers.Count; playerIndex++)
        {
            var player = _timeClipPlayers[playerIndex];

            for (var clipIndex = 0; clipIndex < _commandClips.Count; clipIndex++)
            {
                var clip = _commandClips[clipIndex];
                var clipSlotId = _commandClipSlotIds[clipIndex];

                var isWired = false;
                for (var i = 0; i < connections.Count; i++)
                {
                    if (connections[i].SourceParentOrChildId == clip.Id && connections[i].SourceSlotId == clipSlotId)
                    {
                        isWired = true;
                        break;
                    }
                }

                if (!isWired)
                    DrawIndicatorLine(drawList, clip, player, typeColor);
            }
        }
    }

    private void DrawIndicatorLine(ImDrawListPtr drawList, MagGraphItem clip, MagGraphItem player, Color typeColor)
    {
        var sourceOnScreen = TransformPosition(clip.DampedPosOnCanvas
                                               + new Vector2(clip.Size.X, clip.Size.Y * 0.5f));
        var targetOnScreen = TransformPosition(player.DampedPosOnCanvas
                                               + new Vector2(0, player.Size.Y * 0.5f));

        var highlighted = _context.ActiveItem == player || _context.Selector.IsSelected(player)
                          || _context.ActiveItem == clip || _context.Selector.IsSelected(clip);

        // Deliberately much fainter than real connection lines — these only hint at an implicit link.
        var color = typeColor.Fade((highlighted ? 0.4f : 0.08f) * _context.GraphOpacity);
        var d = Vector2.Distance(sourceOnScreen, targetOnScreen) / 2;
        drawList.AddBezierCubic(sourceOnScreen,
                                sourceOnScreen + new Vector2(d, 0),
                                targetOnScreen - new Vector2(d, 0),
                                targetOnScreen,
                                color,
                                1.1f);
    }

    private static bool TryGetCommandClipSlotId(MagGraphItem item, out Guid clipSlotId)
    {
        clipSlotId = default;
        var instance = item.Instance;

        // Audio clips carry a Command time-clip slot for timeline presence but are collected by the
        // audio graph, not [TimeClipPlayer] — mirror the runtime's exclusion.
        if (instance == null || instance is T3.Core.Audio.IAudioClipProvider)
            return false;

        var outputs = instance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is TimeClipSlot<Command> clipSlot)
            {
                clipSlotId = clipSlot.Id;
                return true;
            }
        }

        return false;
    }

    // AutoCollect from the stored input value, or the live slot value when the input is connected.
    private static bool IsAutoCollectEnabled(MagGraphItem playerItem, in Guid autoCollectInputId)
    {
        var instance = playerItem.Instance;
        if (instance == null)
            return false;

        for (var i = 0; i < instance.Inputs.Count; i++)
        {
            if (instance.Inputs[i].Id != autoCollectInputId)
                continue;

            if (instance.Inputs[i] is not InputSlot<bool> boolSlot)
                return false;

            return boolSlot.Input.Value is InputValue<bool> boolValue && !boolSlot.HasInputConnections
                       ? boolValue.Value
                       : boolSlot.Value;
        }

        return false;
    }

    /// <summary>
    /// Marks every child feeding the player's VideoClips multi-input — directly or through inserted image
    /// effects — as wired, mirroring the runtime's upstream walk so wired clips get no indicator line.
    /// </summary>
    private void CollectWiredClipIds(MagGraphItem playerItem)
    {
        _wiredClipIds.Clear();
        var composition = _context.CompositionInstance;
        if (composition == null)
            return;

        var connections = composition.Symbol.Connections;
        _wiredWalkStack.Clear();

        for (var i = 0; i < connections.Count; i++)
        {
            var c = connections[i];
            if (c.TargetParentOrChildId == playerItem.Id && c.TargetSlotId == _videoClipsInputId)
                _wiredWalkStack.Push(c.SourceParentOrChildId);
        }

        // Bounded upstream flood over the connection list; mirrors MaxUpstreamSearchDepth in the runtime.
        var visitedBudget = 64;
        while (_wiredWalkStack.Count > 0 && visitedBudget-- > 0)
        {
            var childId = _wiredWalkStack.Pop();
            if (!_wiredClipIds.Add(childId))
                continue;

            for (var i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c.TargetParentOrChildId == childId)
                    _wiredWalkStack.Push(c.SourceParentOrChildId);
            }
        }
    }

    // [VideoClipPlayer] / [VideoClip] — the editor can't reference the video operator assembly, so the
    // implicit-link indicator identifies the ops by symbol/slot id (same pattern as TimeClipItem).
    private static readonly Guid _videoClipPlayerSymbolId = new("f98d28ed-5f30-4416-93be-f0d7a15a7f77");
    private static readonly Guid _videoClipSymbolId = new("04c1a6dc-3042-48a8-81d2-0a5a162016dc");
    private static readonly Guid _autoCollectInputId = new("ac80c531-90ff-449a-8d60-f6a4fa27b818");
    private static readonly Guid _videoClipsInputId = new("7b80b49f-c5f5-4c86-8c12-f854fff027c2");

    // [TimeClipPlayer] — identified by symbol id like the video ops; its clips are matched by output
    // type (TimeClipSlot<Command>) instead, which the editor can see through Core.
    private static readonly Guid _timeClipPlayerSymbolId = new("fb61e317-4b12-46c6-85d1-5925ccce09cd");
    private static readonly Guid _timeClipPlayerAutoCollectInputId = new("3e7347b1-642c-4719-8e89-d7281b916753");

    // Reused per frame to keep the draw loop allocation-free.
    private readonly List<MagGraphItem> _autoCollectPlayers = new();
    private readonly List<MagGraphItem> _autoCollectClips = new();
    private readonly List<MagGraphItem> _timeClipPlayers = new();
    private readonly List<MagGraphItem> _commandClips = new();
    private readonly List<Guid> _commandClipSlotIds = new();
    private readonly HashSet<Guid> _wiredClipIds = new();
    private readonly Stack<Guid> _wiredWalkStack = new();
}
