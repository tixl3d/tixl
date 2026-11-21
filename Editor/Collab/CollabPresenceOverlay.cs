#nullable enable
using ImGuiNET;
using T3.SystemUi;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Collab;

/// <summary>
/// Draws remote participants on the graph canvas: colored cursors with name tags and a compact
/// interaction chip (mouse buttons and wheel, mirroring the local interaction overlay), plus
/// accent-colored outlines around nodes a remote user has selected. Also samples the local cursor,
/// selection and mouse interaction each frame and hands them to the manager.
///
/// Called from the graph window's draw after the canvas channels are merged, so
/// overlays clip to the graph window and render above nodes and connections.
/// </summary>
internal static class CollabPresenceOverlay
{
    public static void Draw(ProjectView projectView, IGraphView graphView, ImDrawListPtr drawList)
    {
        var manager = CollabManager.Instance;

        // A failover session mirrors a machine 1:1; cursors, selections and badges would only add noise.
        if (T3.Core.Show.FailoverArbiter.IsEnabled)
        {
            _peerVisuals.Clear();
            CollabSpectator.Stop();
            return;
        }

        if (!manager.IsSessionActive)
        {
            _peerVisuals.Clear();
            CollabSpectator.Stop();
            return;
        }

        if (projectView?.InstView == null || graphView?.Canvas == null)
            return;

        var canvas = graphView.Canvas;
        var compositionId = projectView.InstView.Symbol.Id;

        SampleLocalPresence(manager, projectView, graphView, canvas, compositionId);
        DrawRemotePresence(manager, projectView, graphView, canvas, compositionId, drawList);
        CollabSpectator.Update(manager, canvas, compositionId, drawList);
        DrawLockBadges(manager, projectView, graphView, canvas, compositionId, drawList);
    }

    // ---------------------------------------------------------------------------------------
    // Local sampling
    // ---------------------------------------------------------------------------------------

    private static void SampleLocalPresence(CollabManager manager, ProjectView projectView, IGraphView graphView, ScalableCanvas canvas, Guid compositionId)
    {
        // The graph canvas lives in a child window (ScalableCanvas uses the same flags), so a plain
        // IsWindowHovered() is false whenever the pointer is over the canvas — which left the remote
        // cursor permanently hidden.
        var hovering = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows
                                             | ImGuiHoveredFlags.AllowWhenBlockedByPopup
                                             | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var cursorOnCanvas = hovering
                                 ? canvas.InverseTransformPositionFloat(ImGui.GetMousePos())
                                 : Vector2.Zero;

        // Decays across frames so a peer's wheel indicator fades out on its own; presence is sent at
        // 20 Hz, and a per-interval delta would be invisible between two packets.
        // Only the wheel over this canvas counts: the IO wheel is global and would also report scrolling elsewhere.
        _wheelSpin = _wheelSpin * 0.82f + (hovering ? ImGui.GetIO().MouseWheel * 6f : 0f);
        if (MathF.Abs(_wheelSpin) < 0.05f)
            _wheelSpin = 0;

        Guid[] selectionIds = [];
        if (projectView.NodeSelection is { } selection)
        {
            // Sorted so the array is stable while dragging — otherwise every micro-selection
            // change would flag presence as modified.
            foreach (var childUi in selection.GetSelectedChildUis())
                _selectionIdScratch.Add(childUi.Id);

            if (_selectionIdScratch.Count > 0)
            {
                _selectionIdScratch.Sort();
                selectionIds = [.. _selectionIdScratch];
            }

            _selectionIdScratch.Clear();
        }

        if (hovering && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            _doubleClickUntil = ImGui.GetTime() + DoubleClickSendSec;

        manager.SetLocalPresence(compositionId,
                                 cursorOnCanvas,
                                 hovering,
                                 selectionIds,
                                 ImGui.IsMouseDown(ImGuiMouseButton.Left),
                                 ImGui.IsMouseDown(ImGuiMouseButton.Right),
                                 ImGui.IsMouseDown(ImGuiMouseButton.Middle),
                                 _wheelSpin,
                                 ImGui.GetTime() < _doubleClickUntil,
                                 SamplePressedKeys(),
                                 graphView is MagGraphView magGraphView
                                 && magGraphView.Context.StateMachine.CurrentState == GraphStates.Placeholder);
    }

    /// <summary>
    /// The keys held right now, as the interaction overlay labels them. Nothing is shared while a text
    /// field has focus, so typed text and passwords never reach other participants.
    /// </summary>
    private static byte[] SamplePressedKeys()
    {
        var count = 0;
        if (!ImGui.GetIO().WantTextInput)
        {
            var pressed = KeyHandler.PressedKeys;
            var codeCount = Math.Min(KeyboardAndMouseOverlay.TrackedKeyCodeCount, byte.MaxValue + 1);
            for (var code = 0; code < codeCount && count < _keyScratch.Length; code++)
            {
                if (pressed[code] && KeyboardAndMouseOverlay.TryGetKeyLabel(code, out _))
                    _keyScratch[count++] = (byte)code;
            }
        }

        // The array only changes when the held keys do, so most frames reuse the one already sent.
        var unchanged = count == _sentKeys.Length;
        for (var i = 0; unchanged && i < count; i++)
            unchanged = _sentKeys[i] == _keyScratch[i];

        if (!unchanged)
            _sentKeys = _keyScratch.AsSpan(0, count).ToArray();

        return _sentKeys;
    }

    /** How long the double-click flag stays set, so the receiver's 20 Hz sampling cannot miss it. */
    private const double DoubleClickSendSec = 0.6;

    private static double _doubleClickUntil;
    private static byte[] _sentKeys = [];
    private static readonly byte[] _keyScratch = new byte[CollabProtocol.MaxPressedKeysPerPresence];
    private static float _wheelSpin;

    // ---------------------------------------------------------------------------------------
    // Remote drawing
    // ---------------------------------------------------------------------------------------

    private static void DrawRemotePresence(CollabManager manager, ProjectView projectView, IGraphView graphView, ScalableCanvas canvas, Guid compositionId, ImDrawListPtr drawList)
    {
        var deltaTime = ImGui.GetIO().DeltaTime;

        foreach (var (name, display) in manager.GetPeersInComposition(compositionId))
        {
            var color = display.AccentColor;
            var peerName = string.IsNullOrEmpty(name) ? "peer" : name;

            DrawSelectionOutlines(drawList, projectView, graphView, canvas, display.SelectedChildIds, color, peerName);

            if (!_peerVisuals.TryGetValue(display.UserId, out var visual))
            {
                visual = new PeerVisual();
                _peerVisuals[display.UserId] = visual;
            }

            visual.Update(display, deltaTime);
            if (visual.Alpha > 0.01f)
                DrawCursor(drawList, canvas, visual, display, color, peerName);
        }
    }

    /// <summary>
    /// Smoothed view of one remote cursor. Presence arrives at 20 Hz, which looks steppy when drawn
    /// as is; easing towards the latest position and fading in and out when the pointer enters or
    /// leaves the canvas keeps the cursor calm without adding any latency worth noticing.
    /// </summary>
    private sealed class PeerVisual
    {
        public Vector2 PosOnCanvas;
        public float Alpha;
        public readonly MouseInteractionGraphic Mouse = new();

        /// <summary>Keys the peer holds or released within the fade-out, in the order they were pressed.</summary>
        public readonly List<HeldKey> Keys = new();

        public double LastDoubleClickTime = double.NegativeInfinity;

        public struct HeldKey
        {
            public byte Code;
            public bool IsPressed;
            public double ReleaseTime;
        }

        /// <summary>1 while the peer is pressing or scrolling, fading to 0 once they stop.</summary>
        public float InteractionFade => 1f - Math.Clamp((float)(ImGui.GetTime() - _lastInteractionTime) / InteractionFadeoutSec, 0f, 1f);

        private bool _hasPosition;
        private double _lastInteractionTime = double.NegativeInfinity;

        public void Update(CollabManager.CollabPeerDisplay display, float deltaTime)
        {
            if (display.IsHoveringCanvas)
            {
                // A cursor that was away re-appears where the peer is now instead of gliding in from the old spot.
                if (!_hasPosition || Alpha < 0.05f)
                    PosOnCanvas = display.CursorOnCanvas;
                else
                    PosOnCanvas = Vector2.Lerp(PosOnCanvas, display.CursorOnCanvas, 1f - MathF.Exp(-deltaTime * CursorFollowSpeed));

                _hasPosition = true;
            }

            var targetAlpha = display.IsHoveringCanvas ? 1f : 0f;
            Alpha += (targetAlpha - Alpha) * (1f - MathF.Exp(-deltaTime * FadeSpeed));

            // WheelSpin is the sender's decaying scroll speed; its time integral is about two ticks
            // per unit, which moves the wheel by the amount the peer actually scrolled.
            Mouse.AddWheelTicks(display.WheelSpin * deltaTime * WheelTicksPerSpinSecond);
            if (display.LeftButtonDown || display.RightButtonDown || display.MiddleButtonDown || display.WheelSpin != 0)
                _lastInteractionTime = ImGui.GetTime();

            UpdateKeys(display.PressedKeys);
            if (display.IsDoubleClick)
            {
                LastDoubleClickTime = ImGui.GetTime();
                _lastInteractionTime = LastDoubleClickTime;
            }
        }

        public float DoubleClickFade => 1f - Math.Clamp((float)(ImGui.GetTime() - LastDoubleClickTime) / InteractionFadeoutSec, 0f, 1f);

        public float KeyFade(in HeldKey key)
        {
            return key.IsPressed ? 1f : MathF.Pow(1f - Math.Clamp((float)(ImGui.GetTime() - key.ReleaseTime) / InteractionFadeoutSec, 0f, 1f), 4);
        }

        private void UpdateKeys(byte[] pressedKeys)
        {
            var now = ImGui.GetTime();
            for (var i = 0; i < Keys.Count; i++)
            {
                var key = Keys[i];
                if (key.IsPressed)
                {
                    key.IsPressed = false;
                    key.ReleaseTime = now;
                    Keys[i] = key;
                }
            }

            for (var p = 0; p < pressedKeys.Length; p++)
            {
                var index = -1;
                for (var i = 0; i < Keys.Count; i++)
                {
                    if (Keys[i].Code == pressedKeys[p])
                    {
                        index = i;
                        break;
                    }
                }

                var held = new HeldKey { Code = pressedKeys[p], IsPressed = true, ReleaseTime = now };
                if (index >= 0)
                    Keys[index] = held;
                else
                    Keys.Add(held);

                _lastInteractionTime = now;
            }

            for (var i = Keys.Count - 1; i >= 0; i--)
            {
                if (!Keys[i].IsPressed && now - Keys[i].ReleaseTime > InteractionFadeoutSec)
                    Keys.RemoveAt(i);
            }
        }

        private const float CursorFollowSpeed = 22f;
        private const float FadeSpeed = 14f;
        private const float InteractionFadeoutSec = 2f;
        private const float WheelTicksPerSpinSecond = 2f;
    }

    private static readonly Dictionary<Guid, PeerVisual> _peerVisuals = new();

    /// <summary>Per-frame scratch for the sorted selection ids the presence packet carries.</summary>
    private static readonly List<Guid> _selectionIdScratch = [];

    private static uint Tint(Vector4 color, float alpha) => CollabColors.ToUint(new Vector4(color.X, color.Y, color.Z, color.W * alpha));

    private static void DrawCursor(ImDrawListPtr drawList, ScalableCanvas canvas, PeerVisual visual, CollabManager.CollabPeerDisplay display, Vector4 color, string name)
    {
        var screenPos = canvas.TransformPositionFloat(visual.PosOnCanvas);
        if (screenPos.X < -100 || screenPos.Y < -100)
            return;

        var scale = T3Ui.UiScaleFactor;
        var alpha = visual.Alpha;

        // Pointer arrow: a head and a tail built from convex pieces, with a soft drop shadow and a thin
        // dark rim so it reads on both dark node bodies and the lighter graph background.
        var shadowOffset = new Vector2(1.2f, 1.8f) * scale;
        DrawArrow(drawList, screenPos + shadowOffset, scale, CollabColors.Black(0.35f * alpha), CollabColors.Black(0.35f * alpha));
        DrawArrow(drawList, screenPos, scale, Tint(color, alpha), CollabColors.Black(0.75f * alpha));

        // Name tag: accent-filled pill with dark text, so it is legible whatever sits behind it.
        var textSize = ImGui.CalcTextSize(name);
        var padding = new Vector2(7, 2.5f) * scale;
        var tagMin = screenPos + new Vector2(13, 17) * scale;
        var tagMax = tagMin + textSize + padding * 2;
        var rounding = (tagMax.Y - tagMin.Y) * 0.5f;
        drawList.AddRectFilled(tagMin + new Vector2(0, 1.5f * scale), tagMax + new Vector2(0, 1.5f * scale), CollabColors.Black(0.25f * alpha), rounding);
        drawList.AddRectFilled(tagMin, tagMax, Tint(color, alpha), rounding);
        drawList.AddText(tagMin + padding, Tint(CollabColors.Ink, alpha), name);

        if (display.IsAddingOperator)
            DrawAddingOperatorChip(drawList, new Vector2(tagMax.X + 6 * scale, tagMin.Y), tagMax.Y - tagMin.Y, color, alpha);

        var interactionFade = visual.InteractionFade * alpha;
        if (interactionFade > 0.01f)
            DrawInteractionMouse(drawList, new Vector2(tagMin.X, tagMax.Y + 4 * scale), visual, display, color, interactionFade);
    }

    /// <summary>Beside the name tag while the peer has the operator search open, so others know a node is about to appear.</summary>
    private static void DrawAddingOperatorChip(ImDrawListPtr drawList, Vector2 topLeft, float height, Vector4 color, float alpha)
    {
        var scale = T3Ui.UiScaleFactor;
        const string label = "+ adding operator";
        var padding = new Vector2(7, 2.5f) * scale;
        var size = new Vector2(ImGui.CalcTextSize(label).X + padding.X * 2, height);
        drawList.AddRectFilled(topLeft, topLeft + size, CollabColors.Black(0.7f * alpha), size.Y * 0.5f);
        drawList.AddRect(topLeft, topLeft + size, Tint(color, alpha), size.Y * 0.5f, 0, 1.2f * scale);
        drawList.AddText(topLeft + padding, Tint(color, alpha), label);
    }

    private static void DrawArrow(ImDrawListPtr drawList, Vector2 tip, float scale, uint fill, uint rim)
    {
        var leftBottom = tip + new Vector2(0, 16.5f) * scale;
        var notch = tip + new Vector2(4.3f, 12.8f) * scale;
        var right = tip + new Vector2(11.8f, 11.8f) * scale;
        var tailLeft = tip + new Vector2(7.3f, 19.6f) * scale;
        var tailRight = tip + new Vector2(10f, 18.4f) * scale;
        var notchRight = tip + new Vector2(7.4f, 12.1f) * scale;

        drawList.AddTriangleFilled(tip, leftBottom, notch, fill);
        drawList.AddTriangleFilled(tip, notch, right, fill);
        drawList.AddQuadFilled(notch, tailLeft, tailRight, notchRight, fill);

        var thickness = 1.1f * scale;
        drawList.AddLine(tip, leftBottom, rim, thickness);
        drawList.AddLine(leftBottom, notch, rim, thickness);
        drawList.AddLine(notch, tailLeft, rim, thickness);
        drawList.AddLine(tailLeft, tailRight, rim, thickness);
        drawList.AddLine(tailRight, notchRight, rim, thickness);
        drawList.AddLine(notchRight, right, rim, thickness);
        drawList.AddLine(right, tip, rim, thickness);
    }

    /// <summary>
    /// The peer's mouse as the local interaction overlay draws it: which buttons they hold and how they
    /// scroll, in their accent color on a dark plate right under the name tag. Only shown while they
    /// interact, so an idle cursor stays a plain arrow with a name.
    /// </summary>
    private static void DrawInteractionMouse(ImDrawListPtr drawList, Vector2 topLeft, PeerVisual visual, CollabManager.CollabPeerDisplay display, Vector4 color, float fade)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var mouseScale = MouseScale * uiScale;

        topLeft.Y += DrawPressedKeys(drawList, topLeft, visual, color, fade);

        var plateSize = new Vector2(MouseExtent * 2, MouseExtent * 2 - 6) * mouseScale;
        drawList.AddRectFilled(topLeft, topLeft + plateSize, CollabColors.Black(0.6f * fade), 6 * uiScale);

        var center = topLeft + new Vector2(MouseExtent, MouseExtent - 3) * mouseScale;
        visual.Mouse.Draw(drawList, center, display.LeftButtonDown, display.RightButtonDown, display.MiddleButtonDown, color, fade, mouseScale);

        var doubleClickFade = visual.DoubleClickFade;
        if (doubleClickFade > 0.01f)
            drawList.AddText(new Vector2(topLeft.X + plateSize.X + 4 * uiScale, topLeft.Y + 4 * uiScale), Tint(color, doubleClickFade * fade), "×2");
    }

    /// <summary>The keys the peer holds, labelled like the local interaction overlay; returns the height used.</summary>
    private static float DrawPressedKeys(ImDrawListPtr drawList, Vector2 topLeft, PeerVisual visual, Vector4 color, float fade)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var keyPos = topLeft;
        var rowHeight = 0f;
        for (var i = 0; i < visual.Keys.Count; i++)
        {
            var key = visual.Keys[i];
            if (!KeyboardAndMouseOverlay.TryGetKeyLabel(key.Code, out var label))
                continue;

            var keyFade = visual.KeyFade(key) * fade;
            if (keyFade < 0.01f)
                continue;

            var size = ImGui.CalcTextSize(label) + new Vector2(8, 2) * uiScale;
            drawList.AddRectFilled(keyPos, keyPos + size, Tint(color, keyFade), 3 * uiScale);
            drawList.AddText(keyPos + new Vector2(4, 0) * uiScale, Tint(CollabColors.Ink, keyFade), label);
            keyPos.X += size.X + 3 * uiScale;
            rowHeight = MathF.Max(rowHeight, size.Y);
        }

        return rowHeight > 0 ? rowHeight + 4 * uiScale : 0;
    }

    /** Half the width of the mouse drawing at scale 1, buttons and stroke included. */
    private const float MouseExtent = 34f;

    /** Size of a peer's mouse relative to the local overlay's. */
    private const float MouseScale = 0.55f;

    /// <summary>
    /// Frames the nodes a remote user has selected: a soft tinted fill, a glow and a crisp outline that
    /// follows the node's real bounds, plus one name tag on the first node so it is clear whose selection it is.
    /// </summary>
    private static void DrawSelectionOutlines(ImDrawListPtr drawList, ProjectView projectView, IGraphView graphView, ScalableCanvas canvas, Guid[] selectedIds, Vector4 color, string name)
    {
        if (selectedIds.Length == 0 || projectView.InstView == null)
            return;

        var symbolUi = projectView.InstView.Symbol.GetSymbolUi();
        if (symbolUi == null)
            return;

        var scale = T3Ui.UiScaleFactor;
        var padding = new Vector2(3, 3) * scale;
        var rounding = 7f * scale;
        var tagDrawn = false;

        foreach (var childId in selectedIds)
        {
            if (!TryGetNodeBounds(graphView, symbolUi, childId, out var posOnCanvas, out var size))
                continue;

            var min = canvas.TransformPositionFloat(posOnCanvas) - padding;
            var max = canvas.TransformPositionFloat(posOnCanvas + size) + padding;

            drawList.AddRectFilled(min, max, Tint(color, 0.08f), rounding);
            drawList.AddRect(min, max, Tint(color, 0.2f), rounding, 0, 4.5f * scale);
            drawList.AddRect(min, max, Tint(color, 0.95f), rounding, 0, 1.6f * scale);

            if (tagDrawn || max.X - min.X < 36 * scale)
                continue;

            tagDrawn = true;
            var textSize = ImGui.CalcTextSize(name);
            var tagPadding = new Vector2(6, 1.5f) * scale;
            var tagSize = textSize + tagPadding * 2;
            var tagMin = new Vector2(min.X + rounding, min.Y - tagSize.Y + 1.5f * scale);
            drawList.AddRectFilled(tagMin, tagMin + tagSize, Tint(color, 0.95f), tagSize.Y * 0.5f);
            drawList.AddText(tagMin + tagPadding, Tint(CollabColors.Ink, 1f), name);
        }
    }

    /// <summary>
    /// The magnetic graph lays nodes out taller than their symbol UI size (input rows are part of the
    /// node), so frames must use its own bounds; other views fall back to the symbol UI.
    /// </summary>
    private static bool TryGetNodeBounds(IGraphView graphView, SymbolUi symbolUi, Guid childId, out Vector2 posOnCanvas, out Vector2 size)
    {
        if (graphView is MagGraphView magGraphView)
            return magGraphView.TryGetItemBounds(childId, out posOnCanvas, out size);

        if (symbolUi.ChildUis.TryGetValue(childId, out var childUi))
        {
            posOnCanvas = childUi.PosOnCanvas;
            size = childUi.Size;
            return true;
        }

        posOnCanvas = default;
        size = default;
        return false;
    }

    /// <summary>
    /// Marks nodes whose parameters another participant is currently editing (soft lock). The chip
    /// shows the holder's color and name so everyone sees who is working on what at a glance.
    /// </summary>
    private static void DrawLockBadges(CollabManager manager, ProjectView projectView, IGraphView graphView, ScalableCanvas canvas, Guid compositionId, ImDrawListPtr drawList)
    {
        if (projectView.InstView == null)
            return;

        var symbolUi = projectView.InstView.Symbol.GetSymbolUi();
        if (symbolUi == null)
            return;

        var scale = T3Ui.UiScaleFactor;
        var padding = new Vector2(3, 3) * scale;

        foreach (var lockInfo in manager.GetInputLocksForComposition(compositionId))
        {
            if (!TryGetNodeBounds(graphView, symbolUi, lockInfo.ChildId, out var posOnCanvas, out var size))
                continue;

            var min = canvas.TransformPositionFloat(posOnCanvas) - padding;
            var max = canvas.TransformPositionFloat(posOnCanvas + size) + padding;
            var color = CollabColors.ToUint(lockInfo.Color);

            drawList.AddRect(min, max, color, 7f * scale, 0, 1.4f * scale);

            var label = $"{lockInfo.HolderName} editing";
            var labelSize = ImGui.CalcTextSize(label);
            var tagPadding = new Vector2(6, 1.5f) * scale;
            var tagSize = labelSize + tagPadding * 2;
            var tagMin = new Vector2(max.X - tagSize.X - 8 * scale, min.Y - tagSize.Y - 3 * scale);
            drawList.AddRectFilled(tagMin, tagMin + tagSize, Tint(CollabColors.Ink, 0.9f), tagSize.Y * 0.5f);
            drawList.AddRect(tagMin, tagMin + tagSize, color, tagSize.Y * 0.5f, 0, 1f);
            drawList.AddText(tagMin + tagPadding, color, label);
        }
    }
}
