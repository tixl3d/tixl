using System.Numerics;
using ImGuiNET;

namespace SilkWindows;

/// <summary>
/// The message box's content, independent of the window system that shows it: the title as a heading, the first
/// paragraph emphasized, the rest of the message scrolling above a row of buttons that always stays in view.
/// </summary>
public sealed class MessageBox<T> : IImguiDrawer<T>
{
    /// <param name="look">Draws the message as Markdown and the buttons in the host's style. Without one the
    /// message is wrapped plain text with ImGui buttons.</param>
    public MessageBox(string message, T[]? buttons, Func<T, string>? toString, IMessageBoxLook? look = null)
    {
        if (buttons == null || buttons.Length == 0)
        {
            buttons = [];
        }

        toString ??= item => item!.ToString()!;
        _message = message;
        _look = look;
        _buttons = buttons;
        _buttonLabels = buttons.Select(button => toString(button)).ToArray();
        SplitLeadParagraph(message, out _leadParagraph, out _remainingMessage);
    }

    public void Init()
    {
    }

    public void OnRender(string windowName, double deltaSeconds, ImFonts fonts)
    {
        // The buttons are measured first: the message scrolls in what remains, so they can never be pushed out.
        ImGui.PushFont(fonts.Regular);
        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonRowHeight = ButtonHeight(style);
        var buttonRows = MeasureButtonRows(availableWidth, style);
        var footerPadding = style.WindowPadding.Y;
        var footerHeight = buttonRows * buttonRowHeight + (buttonRows - 1) * style.ItemSpacing.Y + footerPadding * 2 + style.ItemSpacing.Y;
        ImGui.PopFont();

        if (ImGui.BeginChild("message", new Vector2(0, -footerHeight), ImGuiChildFlags.None))
        {
            DrawMessage(windowName, fonts, style);
        }

        ImGui.EndChild();

        ImGui.Dummy(new Vector2(0, footerPadding));
        ImGui.PushFont(fonts.Regular);
        DrawButtons(availableWidth, style);
        ImGui.PopFont();
    }

    private void DrawMessage(string title, ImFonts fonts, ImGuiStylePtr style)
    {
        if (_look != null)
        {
            _composedMarkdown ??= ComposeMarkdown(title);
            _look.DrawMarkdown(_composedMarkdown);
        }
        else
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.PushFont(fonts.Large);
            ImGui.TextWrapped(title);
            ImGui.PopFont();
            ImGui.Spacing();

            ImGui.PushFont(fonts.Bold);
            ImGui.TextWrapped(_leadParagraph);
            ImGui.PopFont();

            if (_remainingMessage.Length > 0)
            {
                ImGui.Spacing();
                ImGui.PushFont(fonts.Regular);
                ImGui.TextWrapped(_remainingMessage);
                ImGui.PopFont();
            }

            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushFont(fonts.Small);
        if (ImGui.Button("Copy to clipboard"))
        {
            ImGui.SetClipboardText(_message);
        }

        var originalHoverFlags = style.HoverFlagsForTooltipMouse;
        style.HoverFlagsForTooltipMouse = ImGuiHoveredFlags.DelayNone;
        if (ImGui.BeginItemTooltip())
        {
            ImGui.Text("Make sure to paste somewhere before closing the window,\nas some events copy text to the clipboard and can overwrite it.");
            ImGui.EndTooltip();
        }

        style.HoverFlagsForTooltipMouse = originalHoverFlags;
        ImGui.PopFont();
    }

    /** The lead paragraph is only made bold when that can't collide with formatting of its own. */
    private string ComposeMarkdown(string title)
    {
        var canEmbolden = _leadParagraph.Length > 0 && !_leadParagraph.Contains('\n') && !_leadParagraph.Contains('*');
        var lead = canEmbolden ? $"**{_leadParagraph}**" : _leadParagraph;
        return _remainingMessage.Length > 0
                   ? $"# {title}\n\n{lead}\n\n{_remainingMessage}"
                   : $"# {title}\n\n{lead}";
    }

    private static void SplitLeadParagraph(string message, out string lead, out string remaining)
    {
        var normalized = message.Replace("\r\n", "\n").Trim();
        var split = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (split < 0)
        {
            lead = normalized;
            remaining = string.Empty;
            return;
        }

        lead = normalized[..split].Trim();
        remaining = normalized[(split + 2)..].Trim();
    }

    /** Right-aligned in one row; buttons that do not fit wrap to the next row instead of leaving the window. */
    private void DrawButtons(float availableWidth, ImGuiStylePtr style)
    {
        var rowStart = ImGui.GetCursorPosX();
        var rowWidth = 0f;
        var rowFirstIndex = 0;

        for (var index = 0; index < _buttons.Length; index++)
        {
            var width = ButtonWidth(index, style);
            var startsNewRow = index > rowFirstIndex && rowWidth + style.ItemSpacing.X + width > availableWidth;
            if (startsNewRow)
            {
                rowFirstIndex = index;
                rowWidth = 0;
            }

            if (index == rowFirstIndex)
            {
                // Right-align the row that starts here.
                ImGui.SetCursorPosX(rowStart + availableWidth - RowWidth(index, availableWidth, style));
            }
            else
            {
                ImGui.SameLine();
            }

            rowWidth += (index > rowFirstIndex ? style.ItemSpacing.X : 0) + width;

            ImGui.PushID(index);
            var isPrimary = index == 0;
            var clicked = _look != null
                              ? _look.DrawButton(_buttonLabels[index], isPrimary)
                              : ImGui.Button(_buttonLabels[index], new Vector2(width, 0));
            if (clicked)
            {
                _result ??= _buttons[index];
            }

            ImGui.PopID();
        }
    }

    private int MeasureButtonRows(float availableWidth, ImGuiStylePtr style)
    {
        var rows = _buttons.Length > 0 ? 1 : 0;
        var rowWidth = 0f;
        for (var index = 0; index < _buttons.Length; index++)
        {
            var width = ButtonWidth(index, style);
            if (rowWidth > 0 && rowWidth + style.ItemSpacing.X + width > availableWidth)
            {
                rows++;
                rowWidth = 0;
            }

            rowWidth += (rowWidth > 0 ? style.ItemSpacing.X : 0) + width;
        }

        return rows;
    }

    /** Width of the row that starts with the given button, as far as it fits. */
    private float RowWidth(int firstIndex, float availableWidth, ImGuiStylePtr style)
    {
        var rowWidth = 0f;
        for (var index = firstIndex; index < _buttons.Length; index++)
        {
            var width = ButtonWidth(index, style);
            var next = rowWidth + (index > firstIndex ? style.ItemSpacing.X : 0) + width;
            if (index > firstIndex && next > availableWidth)
                break;

            rowWidth = next;
        }

        return rowWidth;
    }

    /** Sized to the label, but plain ImGui buttons never narrower than a comfortable click target. */
    private float ButtonWidth(int index, ImGuiStylePtr style)
    {
        if (_look != null)
            return _look.GetButtonSize(_buttonLabels[index]).X;

        var labelWidth = ImGui.CalcTextSize(_buttonLabels[index]).X + style.FramePadding.X * 4;
        return MathF.Max(labelWidth, ImGui.GetFontSize() * MinButtonWidthInFontSizes);
    }

    private float ButtonHeight(ImGuiStylePtr style)
    {
        var height = ImGui.GetFrameHeight();
        if (_look == null)
            return height;

        foreach (var label in _buttonLabels)
        {
            height = MathF.Max(height, _look.GetButtonSize(label).Y);
        }

        return height;
    }

    public void OnWindowUpdate(double deltaSeconds, out bool shouldClose)
    {
        shouldClose = _result != null;
    }
    
    public void OnClose()
    {
    }
    
    public void OnFileDrop(string[] filePaths)
    {
        // do nothing - drag and drop could be supported by another window!
    }
    
    public void OnWindowFocusChanged(bool changedTo)
    {
        // do nothing
    }
    
    public T Result => _result!;
    
    private T? _result;
    private readonly T[] _buttons;
    private readonly string[] _buttonLabels;
    private const float MinButtonWidthInFontSizes = 6;
    private readonly string _message;
    private readonly string _leadParagraph;
    private readonly string _remainingMessage;
    private readonly IMessageBoxLook? _look;
    private string? _composedMarkdown;
}