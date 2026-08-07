#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    /// <summary>
    /// Indicates the implicit links a [VideoClipPlayer] with AutoCollect creates: a faint texture-colored
    /// bezier from every sibling [VideoClip] it draws without a wire to the player. Brightens when either
    /// end is hovered or selected, so the invisible data flow becomes discoverable.
    /// </summary>
    private void DrawAutoCollectIndicators(ImDrawListPtr drawList)
    {
        _autoCollectPlayers.Clear();
        _autoCollectClips.Clear();
        _wiredClipIds.Clear();

        foreach (var item in _context.Layout.Items.Values)
        {
            if (item.Variant != MagGraphItem.Variants.Operator || item.IsCollapsedAway)
                continue;

            var symbolId = item.SymbolChild?.Symbol.Id;
            if (symbolId == _videoClipPlayerSymbolId)
            {
                if (IsAutoCollectEnabled(item))
                    _autoCollectPlayers.Add(item);
            }
            else if (symbolId == _videoClipSymbolId)
            {
                _autoCollectClips.Add(item);
            }
        }

        if (_autoCollectPlayers.Count == 0 || _autoCollectClips.Count == 0)
            return;

        var typeColor = TypeUiRegistry.GetPropertiesForType(typeof(Texture2D)).Color;

        for (var playerIndex = 0; playerIndex < _autoCollectPlayers.Count; playerIndex++)
        {
            var player = _autoCollectPlayers[playerIndex];
            CollectWiredClipIds(player);

            var targetOnScreen = TransformPosition(player.DampedPosOnCanvas
                                                   + new Vector2(player.Size.X * 0.5f, 0));

            var playerHighlighted = _context.ActiveItem == player || _context.Selector.IsSelected(player);

            for (var clipIndex = 0; clipIndex < _autoCollectClips.Count; clipIndex++)
            {
                var clip = _autoCollectClips[clipIndex];
                if (_wiredClipIds.Contains(clip.Id))
                    continue;

                var sourceOnScreen = TransformPosition(clip.DampedPosOnCanvas
                                                       + new Vector2(clip.Size.X * 0.5f, clip.Size.Y));

                var highlighted = playerHighlighted
                                  || _context.ActiveItem == clip
                                  || _context.Selector.IsSelected(clip);

                var color = typeColor.Fade((highlighted ? 0.6f : 0.2f) * _context.GraphOpacity);
                var d = Vector2.Distance(sourceOnScreen, targetOnScreen) / 2;
                drawList.AddBezierCubic(sourceOnScreen,
                                        sourceOnScreen + new Vector2(0, d),
                                        targetOnScreen - new Vector2(0, d),
                                        targetOnScreen,
                                        color,
                                        1.5f);
            }
        }
    }

    // AutoCollect from the stored input value, or the live slot value when the input is connected.
    private static bool IsAutoCollectEnabled(MagGraphItem playerItem)
    {
        var instance = playerItem.Instance;
        if (instance == null)
            return false;

        for (var i = 0; i < instance.Inputs.Count; i++)
        {
            if (instance.Inputs[i].Id != _autoCollectInputId)
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

    // Reused per frame to keep the draw loop allocation-free.
    private readonly List<MagGraphItem> _autoCollectPlayers = new();
    private readonly List<MagGraphItem> _autoCollectClips = new();
    private readonly HashSet<Guid> _wiredClipIds = new();
    private readonly Stack<Guid> _wiredWalkStack = new();
}
