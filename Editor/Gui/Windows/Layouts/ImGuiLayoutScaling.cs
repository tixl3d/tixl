#nullable enable

using System.Globalization;
using System.Numerics;

namespace T3.Editor.Gui.Windows.Layouts;

/// <summary>
/// Rewrites the main dock space of an ImGui ini so that every split keeps the proportion it had when the ini was
/// saved. ImGui itself only stores pixels and keeps every panel beside the central node at a fixed width.
/// </summary>
/// <remarks>
/// Has no ImGui dependency, so it can be unit tested on plain ini text.
/// </remarks>
internal static class ImGuiLayoutScaling
{
    /// <summary>
    /// Returns <paramref name="ini"/> with the dock tree resized to <paramref name="targetSize"/>. Nodes
    /// without visible windows are ignored, because ImGui prunes or hides them and their sibling takes the
    /// full parent. All keys other than the sizes and positions are kept unchanged.
    /// </summary>
    /// <param name="minPanelSize">Safety net in pixels: a panel is only shrunk below it when its parent is too small for both.</param>
    /// <param name="dockSpaceId">The dock space to rescale; 0 picks the first one.</param>
    /// <param name="sourceSize">The dock space size the split sizes were authored for. Defaults to the size stored in the ini.</param>
    public static string Rescale(string ini,
                                 Vector2 targetPos,
                                 Vector2 targetSize,
                                 IReadOnlySet<string> visibleWindowTitles,
                                 float minPanelSize,
                                 uint dockSpaceId = 0,
                                 Vector2? sourceSize = null)
    {
        if (string.IsNullOrEmpty(ini) || targetSize.X < 1 || targetSize.Y < 1)
            return ini;

        var lines = ini.Split('\n');
        var tree = ParsedIni.Parse(lines, dockSpaceId);
        if (tree.Root == null)
            return ini;

        var savedRootSize = sourceSize ?? tree.Root.SavedSize;
        if (savedRootSize.X < 1 || savedRootSize.Y < 1)
            return ini;

        foreach (var window in tree.Windows)
        {
            if (!visibleWindowTitles.Contains(window.Title))
                continue;

            if (window.DockId != 0 && tree.Nodes.TryGetValue(window.DockId, out var node))
                node.HasVisibleWindow = true;
        }

        UpdateVisibility(tree.Root);
        tree.Root.IsVisible = true;

        MeasureSavedSizes(tree.Root, savedRootSize);
        Distribute(tree.Root, Round(targetPos), Round(targetSize), minPanelSize, lines);

        lines[tree.Root.LineIndex] = SetValue(lines[tree.Root.LineIndex], "Pos", Round(targetPos));
        lines[tree.Root.LineIndex] = SetValue(lines[tree.Root.LineIndex], "Size", Round(targetSize));

        var sourcePos = tree.Root.SavedPos;
        var scale = targetSize / savedRootSize;
        foreach (var window in tree.Windows)
        {
            if (!visibleWindowTitles.Contains(window.Title))
                continue;

            if (window.DockId != 0)
            {
                if (tree.Nodes.TryGetValue(window.DockId, out var node) && node.IsVisible)
                    WriteWindowRect(window, node.Pos, node.Size, lines);

                continue;
            }

            if (window.IsInOtherViewport || window.PosLineIndex < 0 || window.SizeLineIndex < 0)
                continue;

            var size = Vector2.Min(Round(window.Size * scale), Round(targetSize));
            var pos = targetPos + (window.Pos - sourcePos) * scale;
            pos = Vector2.Clamp(pos, targetPos, targetPos + targetSize - size);
            WriteWindowRect(window, Round(pos), size, lines);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Reads the size stored for the dock space, which is the size its splits were authored for.</summary>
    public static bool TryReadDockSpaceSize(string ini, uint dockSpaceId, out Vector2 size)
    {
        var tree = ParsedIni.Parse(ini.Split('\n'), dockSpaceId);
        size = tree.Root?.SavedSize ?? Vector2.Zero;
        return size.X >= 1 && size.Y >= 1;
    }

    private static bool UpdateVisibility(DockNode node)
    {
        if (node.Children.Count == 0)
        {
            node.IsVisible = node.HasVisibleWindow || node.IsCentral;
            node.HasCentralNode = node.IsCentral;
            return node.IsVisible;
        }

        var isVisible = false;
        foreach (var child in node.Children)
        {
            isVisible |= UpdateVisibility(child);
            node.HasCentralNode |= child.HasCentralNode;
        }

        node.IsVisible = isVisible;
        return isVisible;
    }

    /// <summary>
    /// Reconstructs the sizes the user saw when the ini was saved, using ImGui's own distribution rule. The
    /// SizeRef of the side holding the central node is not updated by ImGui, so it can't be used directly.
    /// </summary>
    private static void MeasureSavedSizes(DockNode node, Vector2 size)
    {
        node.SavedSizeOnAxis = size;
        if (!TryGetSplitChildren(node, out var child0, out var child1))
        {
            var onlyVisibleChild = GetOnlyVisibleChild(node);
            if (onlyVisibleChild != null)
                MeasureSavedSizes(onlyVisibleChild, size);

            return;
        }

        var axis = node.SplitAxis;
        var available = MathF.Max(size[axis] - SplitterSize, 0);
        var minEach = MathF.Truncate(MathF.Min(available, ImGuiWindowMinSize * 2) * 0.5f);
        var ref0 = child0.SizeRef[axis];
        var ref1 = child1.SizeRef[axis];

        float size0;
        if (ref0 != 0 && child1.HasCentralNode)
        {
            size0 = MathF.Min(available - minEach, ref0);
        }
        else if (ref1 != 0 && child0.HasCentralNode)
        {
            size0 = available - MathF.Min(available - minEach, ref1);
        }
        else
        {
            var ratio = ref0 + ref1 > 0 ? ref0 / (ref0 + ref1) : 0.5f;
            size0 = MathF.Max(minEach, MathF.Truncate(available * ratio + 0.5f));
        }

        MeasureSavedSizes(child0, WithAxis(size, axis, size0));
        MeasureSavedSizes(child1, WithAxis(size, axis, available - size0));
    }

    private static void Distribute(DockNode node, Vector2 pos, Vector2 size, float minPanelSize, string[] lines)
    {
        node.Pos = pos;
        node.Size = size;

        if (!TryGetSplitChildren(node, out var child0, out var child1))
        {
            var onlyVisibleChild = GetOnlyVisibleChild(node);
            if (onlyVisibleChild != null)
                Distribute(onlyVisibleChild, pos, size, minPanelSize, lines);

            return;
        }

        var axis = node.SplitAxis;
        var available = MathF.Max(size[axis] - SplitterSize, 0);
        var saved0 = child0.SavedSizeOnAxis[axis];
        var saved1 = child1.SavedSizeOnAxis[axis];
        var fraction = saved0 + saved1 > 0 ? saved0 / (saved0 + saved1) : 0.5f;

        var size0 = MathF.Round(available * fraction);
        if (available >= 2 * minPanelSize)
            size0 = Math.Clamp(size0, minPanelSize, available - minPanelSize);

        var size1 = available - size0;

        var childSize0 = WithAxis(size, axis, size0);
        var childSize1 = WithAxis(size, axis, size1);
        lines[child0.LineIndex] = SetValue(lines[child0.LineIndex], "SizeRef", childSize0);
        lines[child1.LineIndex] = SetValue(lines[child1.LineIndex], "SizeRef", childSize1);

        Distribute(child0, pos, childSize0, minPanelSize, lines);
        Distribute(child1, WithAxis(pos, axis, pos[axis] + size0 + SplitterSize), childSize1, minPanelSize, lines);
    }

    private static bool TryGetSplitChildren(DockNode node, out DockNode child0, out DockNode child1)
    {
        child0 = child1 = null!;
        if (node.SplitAxis < 0 || node.Children.Count != 2)
            return false;

        child0 = node.Children[0];
        child1 = node.Children[1];
        return child0.IsVisible && child1.IsVisible;
    }

    private static DockNode? GetOnlyVisibleChild(DockNode node)
    {
        DockNode? visibleChild = null;
        foreach (var child in node.Children)
        {
            if (!child.IsVisible)
                continue;

            if (visibleChild != null)
                return null;

            visibleChild = child;
        }

        return visibleChild;
    }

    private static void WriteWindowRect(WindowEntry window, Vector2 pos, Vector2 size, string[] lines)
    {
        if (window.PosLineIndex >= 0)
            lines[window.PosLineIndex] = "Pos=" + FormatPair(pos);

        if (window.SizeLineIndex >= 0)
            lines[window.SizeLineIndex] = "Size=" + FormatPair(size);
    }

    /** Replaces the value of a <c>key=value</c> token in a docking line, keeping the line's other tokens as they are. */
    private static string SetValue(string line, string key, Vector2 value)
    {
        var tokenStart = FindToken(line, key);
        if (tokenStart < 0)
            return line;

        var valueStart = tokenStart + key.Length + 1;
        var valueEnd = line.IndexOf(' ', valueStart);
        if (valueEnd < 0)
            valueEnd = line.Length;

        return string.Concat(line.AsSpan(0, valueStart), FormatPair(value), line.AsSpan(valueEnd));
    }

    private static int FindToken(string line, string key)
    {
        var searchFrom = 0;
        while (true)
        {
            var index = line.IndexOf(key + "=", searchFrom, StringComparison.Ordinal);
            if (index < 0)
                return -1;

            if (index == 0 || line[index - 1] == ' ')
                return index;

            searchFrom = index + 1;
        }
    }

    private static bool TryGetValue(string line, string key, out string value)
    {
        value = string.Empty;
        var tokenStart = FindToken(line, key);
        if (tokenStart < 0)
            return false;

        var valueStart = tokenStart + key.Length + 1;
        var valueEnd = line.IndexOf(' ', valueStart);
        value = valueEnd < 0 ? line[valueStart..] : line[valueStart..valueEnd];
        return true;
    }

    private static bool TryParsePair(string text, out Vector2 value)
    {
        value = Vector2.Zero;
        var comma = text.IndexOf(',');
        if (comma < 0)
            return false;

        if (!float.TryParse(text.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(text.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        value = new Vector2(x, y);
        return true;
    }

    private static uint ParseId(string text)
    {
        var comma = text.IndexOf(',');
        var idText = comma < 0 ? text : text[..comma];
        if (idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            idText = idText[2..];

        return uint.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    private static string FormatPair(Vector2 value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{(int)MathF.Round(value.X)},{(int)MathF.Round(value.Y)}");
    }

    private static Vector2 Round(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));

    private static Vector2 WithAxis(Vector2 value, int axis, float axisValue)
    {
        value[axis] = axisValue;
        return value;
    }

    private sealed class DockNode
    {
        public uint Id;
        public uint ParentId;
        public int LineIndex;
        public int SplitAxis = -1;
        public bool IsCentral;
        public Vector2 SizeRef;
        public Vector2 SavedPos;
        public Vector2 SavedSize;
        public readonly List<DockNode> Children = [];

        public bool HasVisibleWindow;
        public bool IsVisible;
        public bool HasCentralNode;
        public Vector2 SavedSizeOnAxis;
        public Vector2 Pos;
        public Vector2 Size;
    }

    private sealed class WindowEntry
    {
        public required string Title;
        public uint DockId;
        public bool IsInOtherViewport;
        public int PosLineIndex = -1;
        public int SizeLineIndex = -1;
        public Vector2 Pos;
        public Vector2 Size;
    }

    private sealed class ParsedIni
    {
        public DockNode? Root;
        public readonly Dictionary<uint, DockNode> Nodes = new();
        public readonly List<WindowEntry> Windows = [];

        public static ParsedIni Parse(string[] lines, uint dockSpaceId)
        {
            var result = new ParsedIni();
            var allNodes = new List<DockNode>();
            WindowEntry? currentWindow = null;
            var isDockingSection = false;
            var isInSelectedTree = false;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].TrimEnd('\r');
                if (line.StartsWith('['))
                {
                    currentWindow = null;
                    isDockingSection = line.StartsWith(DockingSectionHeader, StringComparison.Ordinal);
                    if (line.StartsWith(WindowSectionPrefix, StringComparison.Ordinal) && line.EndsWith(']'))
                    {
                        currentWindow = new WindowEntry { Title = line[WindowSectionPrefix.Length..^1] };
                        result.Windows.Add(currentWindow);
                    }

                    continue;
                }

                if (currentWindow != null)
                {
                    ReadWindowLine(currentWindow, line, lineIndex);
                    continue;
                }

                if (!isDockingSection || line.Length == 0)
                    continue;

                var trimmed = line.TrimStart();
                var isTopLevel = line[0] != ' ';
                if (isTopLevel)
                {
                    isInSelectedTree = false;
                    if (result.Root != null || !trimmed.StartsWith("DockSpace", StringComparison.Ordinal))
                        continue;
                }
                else if (!isInSelectedTree)
                {
                    continue;
                }

                var node = ReadNode(trimmed, lineIndex);
                if (isTopLevel)
                {
                    if (dockSpaceId != 0 && node.Id != dockSpaceId)
                        continue;

                    result.Root = node;
                    isInSelectedTree = true;
                }

                result.Nodes[node.Id] = node;
                allNodes.Add(node);
            }

            foreach (var node in allNodes)
            {
                if (node.ParentId != 0 && result.Nodes.TryGetValue(node.ParentId, out var parent))
                    parent.Children.Add(node);
            }

            return result;
        }

        private static void ReadWindowLine(WindowEntry window, string line, int lineIndex)
        {
            if (line.StartsWith("Pos=", StringComparison.Ordinal) && TryParsePair(line[4..], out window.Pos))
            {
                window.PosLineIndex = lineIndex;
            }
            else if (line.StartsWith("Size=", StringComparison.Ordinal) && TryParsePair(line[5..], out window.Size))
            {
                window.SizeLineIndex = lineIndex;
            }
            else if (line.StartsWith("DockId=", StringComparison.Ordinal))
            {
                window.DockId = ParseId(line[7..]);
            }
            else if (line.StartsWith("ViewportPos=", StringComparison.Ordinal))
            {
                window.IsInOtherViewport = true;
            }
        }

        private static DockNode ReadNode(string line, int lineIndex)
        {
            var node = new DockNode { LineIndex = lineIndex };
            if (TryGetValue(line, "ID", out var text))
                node.Id = ParseId(text);

            if (TryGetValue(line, "Parent", out text))
                node.ParentId = ParseId(text);

            if (TryGetValue(line, "SizeRef", out text))
                TryParsePair(text, out node.SizeRef);

            if (TryGetValue(line, "Pos", out text))
                TryParsePair(text, out node.SavedPos);

            if (TryGetValue(line, "Size", out text))
                TryParsePair(text, out node.SavedSize);

            if (TryGetValue(line, "Split", out text))
                node.SplitAxis = text == "X" ? 0 : text == "Y" ? 1 : -1;

            node.IsCentral = TryGetValue(line, "CentralNode", out text) && text == "1";
            return node;
        }
    }

    /** Gap ImGui leaves between the two children of a split (DOCKING_SPLITTER_SIZE). */
    private const float SplitterSize = 2;

    /** ImGui's default style WindowMinSize, which its own size distribution reserves for each side. */
    private const float ImGuiWindowMinSize = 32;

    private const string WindowSectionPrefix = "[Window][";
    private const string DockingSectionHeader = "[Docking]";
}
