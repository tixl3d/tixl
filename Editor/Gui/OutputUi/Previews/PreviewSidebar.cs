#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OutputUi.Previews;

/// <summary>
/// The data sidebar shared by output views with a rendered preview (geometry, point buffers, point lists). One
/// segmented control picks <c>Hidden</c> (segment 0) or what the sidebar shows (tables per domain, stats); a vertical
/// splitter sizes the sidebar and a toggle maximizes it over the whole view. While the sidebar is open, the render is
/// contained in the rectangle right of it (<see cref="BeginView"/>, <see cref="TryGetViewRect"/>), so nothing is drawn
/// behind the toolbar or the table and orbiting turns around the middle of what you see. Without the sidebar the
/// control floats over the render, which fills the output window as usual.
/// </summary>
internal sealed class PreviewSidebar
{
    public readonly record struct Segment(Icon Icon, string Tooltip);

    /// <summary>Kept per kind of view, so all geometry outputs (or all point buffers) share their layout.</summary>
    public sealed class State
    {
        public int Selected;
        public float Width = 340;
        public bool IsMaximized;
    }

    public PreviewSidebar(State state)
    {
        _state = state;
    }

    /// <summary>0 is hidden; otherwise the index into the segments.</summary>
    public int Selected
    {
        get => _state.Selected;
        set => _state.Selected = value;
    }

    public bool IsOpen => _state.Selected > 0;

    /// <summary>True when the preview is visible and worth rendering.</summary>
    public bool ShowsPreview => !(IsOpen && _state.IsMaximized);

    /// <summary>True when the sidebar is open next to the render; its content is then worth updating every frame.</summary>
    public bool IsOpenNextToPreview => IsOpen && !_state.IsMaximized;

    /// <summary>
    /// While the sidebar is open next to the render, renders at the size of the rectangle right of it, with the
    /// projection's aspect corrected so the vertical field of view stays the same; transform gizmos are hidden meanwhile.
    /// Call before rendering the preview
    /// and <see cref="EndView"/> after.
    /// </summary>
    public void BeginView(EvaluationContext context)
    {
        _savedResolution = context.RequestedResolution;
        _savedProjection = context.CameraToClipSpace;
        _savedGizmos = context.ShowGizmos;
        if (!ContainsView() || _savedResolution.Width <= 0 || _savedResolution.Height <= 0)
            return;

        var resolution = new Int2((int)_viewRect.GetWidth(), (int)_viewRect.GetHeight());
        var aspectCorrection = (_savedResolution.Width / (float)_savedResolution.Height) / (resolution.Width / (float)resolution.Height);
        context.RequestedResolution = resolution;
        context.CameraToClipSpace = _savedProjection * Matrix4x4.CreateScale(aspectCorrection, 1, 1);

        // Transform gizmos map onto the whole output canvas, so they would sit beside a contained render
        context.ShowGizmos = GizmoVisibility.Off;
    }

    public void EndView(EvaluationContext context)
    {
        context.RequestedResolution = _savedResolution;
        context.CameraToClipSpace = _savedProjection;
        context.ShowGizmos = _savedGizmos;
    }

    /// <summary>Where the render goes while the sidebar is open next to it, as measured in the last draw.</summary>
    public bool TryGetViewRect(out ImRect rect)
    {
        rect = _viewRect;
        return ContainsView();
    }

    private bool ContainsView()
    {
        return IsOpen && !_state.IsMaximized && !_hasNoPreview && _viewRect.GetWidth() >= MinRenderSize && _viewRect.GetHeight() >= MinRenderSize;
    }

    /// <summary>
    /// Draws the control, and when a segment is open, starts the sidebar region. Returns true when the caller should
    /// draw the sidebar's content now and then call <see cref="End"/>.
    /// </summary>
    /// <param name="counts">Small text after an icon, e.g. an element count; null for none. Cache the strings.</param>
    /// <param name="disabledMask">Bit i set disables segment i.</param>
    /// <param name="hasNoPreview">
    /// Nothing can be rendered (e.g. a buffer that isn't made of points): the sidebar stays open over the whole view and
    /// Hidden is disabled.
    /// </param>
    public bool Begin(ReadOnlySpan<Segment> segments, ReadOnlySpan<string?> counts, int disabledMask = 0, bool hasNoPreview = false)
    {
        var scale = T3Ui.UiScaleFactor;
        _origin = ImGui.GetCursorScreenPos();
        _size = ImGui.GetContentRegionAvail();
        _hasNoPreview = hasNoPreview;
        if (hasNoPreview)
            disabledMask |= 1;

        if (_state.Selected >= segments.Length || (disabledMask & (1 << _state.Selected)) != 0)
            _state.Selected = FirstEnabled(segments.Length, disabledMask);

        if (!IsOpen)
        {
            ImGui.SetCursorScreenPos(_origin + new Vector2(ControlsPadding, ControlsPadding) * scale);
            DrawSegments(segments, counts, disabledMask);
            return false;
        }

        var isMaximized = _state.IsMaximized || hasNoPreview;
        _width = isMaximized
                     ? _size.X
                     : Math.Clamp(_state.Width * scale, MinWidth * scale, MathF.Max(MinWidth * scale, _size.X - MinViewWidth * scale));
        var viewMin = _origin + new Vector2(_width + SplitterWidth * scale, 0);
        _viewRect = new ImRect(viewMin, _origin + _size);

        ImGui.BeginChild("##previewSidebar", new Vector2(_width, _size.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(ControlsPadding, ControlsPadding) * scale);
        DrawSegments(segments, counts, disabledMask);

        // Maximize toggle at the right end of the control row
        if (!hasNoPreview)
        {
            var buttonSize = new Vector2(ImGui.GetFrameHeight());
            ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX(), _width - buttonSize.X - ControlsPadding * scale));
            if (CustomComponents.IconButton(Icon.ViewList, buttonSize, isMaximized ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _state.IsMaximized = !isMaximized;

            CustomComponents.TooltipForLastItem(isMaximized ? "Show the render next to the table again." : "Use the whole view for the table.");
        }

        ImGui.SetCursorPosX(ControlsPadding * scale);
        return true;
    }

    /// <summary>Ends the sidebar region and draws the splitter.</summary>
    public void End()
    {
        ImGui.EndChild();
        if (_state.IsMaximized || _hasNoPreview)
        {
            ImGui.SetCursorScreenPos(_origin);
            return;
        }

        var scale = T3Ui.UiScaleFactor;
        var splitterMin = _origin + new Vector2(_width, 0);
        ImGui.SetCursorScreenPos(splitterMin);
        ImGui.InvisibleButton("##previewSidebarSplitter", new Vector2(SplitterWidth * scale, _size.Y));
        var isActive = ImGui.IsItemActive();
        if (isActive || ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (isActive)
            _state.Width = MathF.Max(MinWidth, _state.Width + ImGui.GetIO().MouseDelta.X / scale);

        var color = isActive ? UiColors.ForegroundFull.Fade(0.5f) : UiColors.BackgroundFull;
        ImGui.GetWindowDrawList().AddRectFilled(splitterMin, splitterMin + new Vector2(SplitterWidth * scale, _size.Y), color);
        ImGui.SetCursorScreenPos(_origin);
    }

    private static int FirstEnabled(int count, int disabledMask)
    {
        for (var index = 0; index < count; index++)
        {
            if ((disabledMask & (1 << index)) == 0)
                return index;
        }

        return 0;
    }

    /// <summary>Short counts for the segments: 123, 4.5k, 12k, 1.2M.</summary>
    public static string FormatCount(int count)
    {
        return count switch
                   {
                       < 10000    => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       < 100000   => (count / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k",
                       < 1000000  => (count / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "k",
                       _          => (count / 1000000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M",
                   };
    }

    /** Like <see cref="CustomComponents.SegmentedButton{T}"/>, with an icon and an optional count per segment. */
    private void DrawSegments(ReadOnlySpan<Segment> segments, ReadOnlySpan<string?> counts, int disabledMask)
    {
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var padding = 6 * scale;
        var gap = 3 * scale;
        var rounding = 4 * scale;
        var inset = 2 * scale;
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();

        ImGui.PushFont(Fonts.FontSmall);
        var total = 0f;
        for (var index = 0; index < segments.Length; index++)
            total += GetSegmentWidth(segments[index].Icon, index < counts.Length ? counts[index] : null, padding, gap);

        drawList.AddRectFilled(start, start + new Vector2(total, height), UiColors.BackgroundInputField, rounding);
        ImGui.PushID("##previewSidebarSegments");
        var x = start.X;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var count = index < counts.Length ? counts[index] : null;
            var width = GetSegmentWidth(segment.Icon, count, padding, gap);
            var isSelected = index == _state.Selected;
            var isDisabled = (disabledMask & (1 << index)) != 0;
            if (index > 0)
                ImGui.SameLine(0, 0);

            ImGui.PushID(index);
            if (ImGui.InvisibleButton("##segment", new Vector2(width, height)) && !isDisabled)
                _state.Selected = index;

            ImGui.PopID();
            CustomComponents.TooltipForLastItem(segment.Tooltip);
            var isHovered = !isDisabled && ImGui.IsItemHovered();
            var segmentMin = new Vector2(x, start.Y);
            if (isSelected || isHovered)
            {
                drawList.AddRectFilled(segmentMin + new Vector2(inset, inset), segmentMin + new Vector2(width - inset, height - inset),
                                       isSelected ? UiColors.BackgroundButton : UiColors.BackgroundButton.Fade(0.4f), rounding);
            }

            var color = isDisabled
                            ? UiColors.TextMuted.Fade(0.25f)
                            : isSelected
                                ? UiColors.ForegroundFull
                                : isHovered ? UiColors.Text : UiColors.TextMuted;
            Icons.GetGlyphDefinition(segment.Icon, out _, out var iconSize);
            var iconPos = new Vector2(segmentMin.X + padding, segmentMin.Y + MathF.Round((height - iconSize.Y) * 0.5f));
            Icons.DrawIconAtScreenPosition(segment.Icon, iconPos, drawList, color);
            if (count != null)
            {
                var textSize = ImGui.CalcTextSize(count);
                drawList.AddText(new Vector2(iconPos.X + iconSize.X + gap, segmentMin.Y + (height - textSize.Y) * 0.5f), color, count);
            }

            x += width;
        }

        ImGui.PopID();
        ImGui.PopFont();
    }

    /** Call with the small font pushed. */
    private static float GetSegmentWidth(Icon icon, string? count, float padding, float gap)
    {
        Icons.GetGlyphDefinition(icon, out _, out var iconSize);
        var width = iconSize.X + padding * 2;
        if (count != null)
            width += gap + ImGui.CalcTextSize(count).X;

        return width;
    }

    private const float ControlsPadding = 6;
    private const float SplitterWidth = 4;
    private const float MinWidth = 160;
    private const float MinViewWidth = 120;
    private const float MinRenderSize = 16;

    private readonly State _state;
    private Vector2 _origin;
    private Vector2 _size;
    private float _width;
    private ImRect _viewRect;
    private Int2 _savedResolution;
    private Matrix4x4 _savedProjection;
    private GizmoVisibility _savedGizmos;
    private bool _hasNoPreview;
}
