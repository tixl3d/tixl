#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource.ShaderCompiling;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

/// <summary>
/// A resizable popup for editing a <see cref="StringInputUi.UsageType.Code"/> input at a comfortable size: line numbers,
/// marked problem lines and a list of every problem the compiler reported. For ops that splice the code into a shader
/// template it can also show the source that was actually compiled, read-only and with its own line numbers, because
/// a problem outside the snippet can only be found there.
/// </summary>
[HelpUiID("CodeEditorPopup")]
internal static class CodeEditorPopup
{
    /// <summary>Must be called in the same ImGui ID scope as <see cref="Draw"/>.</summary>
    public static void Open(Instance instance, Guid inputId, Vector2 position)
    {
        ImGui.OpenPopup(PopupId);
        ImGui.SetNextWindowPos(position);
        _instanceId = instance.SymbolChildId;
        _inputId = inputId;
        _showCompiledSource = false;
        _pendingCursorLine = 0;
        _scrollToLine = 0;
        _hasUnfinishedEdit = false;
    }

    /// <param name="status">The problem shown below the inline field; listed when the compiler's own list isn't available.</param>
    public static InputEditStateFlags Draw(Instance instance, Guid inputId, string inputName, string language, ref string value,
                                           InputStatus? status)
    {
        if (instance.SymbolChildId != _instanceId || inputId != _inputId)
            return InputEditStateFlags.Nothing;

        var scale = T3Ui.UiScaleFactor;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8) * scale);
        // NoMove: a press in the corner resizes instead of moving; the header moves the popup explicitly.
        var isOpen = ImGui.BeginPopup(PopupId, ImGuiWindowFlags.NoMove);
        ImGui.PopStyleVar();
        if (!isOpen)
        {
            // Closing removes the field before it can report the end of an edit, which would leave it out of undo.
            if (!_hasUnfinishedEdit)
                return InputEditStateFlags.Nothing;

            _hasUnfinishedEdit = false;
            return InputEditStateFlags.Finished;
        }

        var hasCompiledSource = CodeInputStatus.TryGetCompiledSource(instance, inputId, out var compiledSource, out var firstLine,
                                                                     out var diagnostics);
        if (!hasCompiledSource)
            _showCompiledSource = false;

        var snippetLineCount = CountLines(value);
        UpdateProblems(diagnostics, status, firstLine, snippetLineCount);

        var width = MathF.Round(_size.X * scale);
        DrawHeader(width, inputName, language, hasCompiledSource);
        FormInputs.AddVerticalSpace(4);

        DrawUtils.ClaimEscapeFromTextInputs();
        var editResult = DrawCode(width, MathF.Round(_size.Y * scale), ref value, compiledSource, firstLine, snippetLineCount);
        FormInputs.AddVerticalSpace(4);
        DrawProblems(width);

        CustomComponents.DrawPopupResizeGrip(ref _size, MinSize, ref _isResizing);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
            if (_hasUnfinishedEdit)
            {
                _hasUnfinishedEdit = false;
                editResult |= InputEditStateFlags.Finished;
            }
        }

        ImGui.EndPopup();
        return editResult;
    }

    private static void DrawHeader(float width, string inputName, string language, bool hasCompiledSource)
    {
        var rowStart = ImGui.GetCursorScreenPos();
        var iconSize = new Vector2(ImGui.GetFrameHeight());
        CustomComponents.DrawPopupMoveGrip("##moveCodePopup", iconSize);

        ImGui.SameLine(0, MathF.Round(2 * T3Ui.UiScaleFactor));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(inputName);
        if (language.Length > 0)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(language);
            ImGui.PopStyleColor();
        }

        if (hasCompiledSource)
            DrawCompiledSourceToggle(rowStart, width, iconSize.Y);

        CustomComponents.HandlePopupHeaderDrag(rowStart.Y + iconSize.Y, ref _isMoving);
    }

    private static void DrawCompiledSourceToggle(Vector2 rowStart, float width, float height)
    {
        const string label = "Compiled source";
        var buttonWidth = MathF.Round(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + width - buttonWidth, rowStart.Y));
        if (CustomComponents.ToggleButton(ref _showCompiledSource, label, new Vector2(buttonWidth, height)))
        {
            _pendingCursorLine = 0;
            _scrollToLine = 0;
        }

        CustomComponents.TooltipForLastItem("Compiled source",
                                            "The full shader this code was spliced into, read-only and with its own line numbers. "
                                            + "Problems outside your code, e.g. in the template, are only found here.");
    }

    private static InputEditStateFlags DrawCode(float width, float height, ref string value, string compiledSource, int firstLine,
                                                int snippetLineCount)
    {
        var editResult = InputEditStateFlags.Nothing;
        var showsCompiled = _showCompiledSource;

        ImGui.PushFont(Fonts.Code);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundInputField.Rgba);
        ImGui.BeginChild("##codeBody", new Vector2(width, height));

        var lineHeight = ImGui.GetTextLineHeight();
        var framePadding = ImGui.GetStyle().FramePadding;
        var lineCount = showsCompiled ? CountLines(compiledSource) : snippetLineCount;
        var digitCount = lineCount < 10 ? 1 : (int)MathF.Floor(MathF.Log10(lineCount)) + 1;
        var gutterWidth = MathF.Round((digitCount + 1) * ImGui.CalcTextSize("0").X + framePadding.X);

        var origin = ImGui.GetCursorScreenPos();
        var visibleTop = ImGui.GetWindowPos().Y;
        var visibleBottom = visibleTop + ImGui.GetWindowHeight();
        var textTop = origin.Y + framePadding.Y;

        if (_scrollToLine > 0)
        {
            var targetTop = framePadding.Y + (_scrollToLine - 1) * lineHeight;
            ImGui.SetScrollY(MathF.Max(0, targetTop - height / 3));
            _scrollToLine = 0;
        }

        // Only the visible rows get a number and a mark, so long compiled sources cost nothing extra.
        var drawList = ImGui.GetWindowDrawList();
        var firstVisible = Math.Max(0, (int)((visibleTop - textTop) / lineHeight));
        var lastVisible = Math.Min(lineCount - 1, (int)((visibleBottom - textTop) / lineHeight) + 1);
        var rowRight = origin.X + ImGui.GetContentRegionAvail().X;
        for (var index = firstVisible; index <= lastVisible; index++)
        {
            var lineNumber = index + 1;
            var rowTop = MathF.Round(textTop + index * lineHeight);
            var mark = FindMark(lineNumber, showsCompiled);
            var isInSnippet = showsCompiled && firstLine > 0 && lineNumber >= firstLine && lineNumber < firstLine + snippetLineCount;
            if (mark != null)
            {
                drawList.AddRectFilled(new Vector2(origin.X, rowTop), new Vector2(rowRight, rowTop + lineHeight), mark.Value.Fade(0.18f));
            }
            else if (isInSnippet)
            {
                drawList.AddRectFilled(new Vector2(origin.X, rowTop), new Vector2(rowRight, rowTop + lineHeight), UiColors.StatusAutomated.Fade(0.1f));
            }

            var label = GetNumberLabel(lineNumber);
            var labelWidth = ImGui.CalcTextSize(label).X;
            var numberColor = mark ?? (isInSnippet ? UiColors.TextMuted : UiColors.TextMuted.Fade(0.6f));
            drawList.AddText(new Vector2(origin.X + gutterWidth - framePadding.X - labelWidth, rowTop).Floor(), numberColor, label);
        }

        // Sized to the whole text, so it never scrolls itself and this child scrolls numbers and text together.
        var fieldHeight = MathF.Max(lineCount * lineHeight + framePadding.Y * 2 + lineHeight, ImGui.GetContentRegionAvail().Y);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + gutterWidth, origin.Y));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
        var fieldSize = new Vector2(ImGui.GetContentRegionAvail().X, fieldHeight);
        if (showsCompiled)
        {
            var readOnlySource = compiledSource;
            ImGui.InputTextMultiline("##compiledSource", ref readOnlySource, (uint)Math.Max(readOnlySource.Length + 1, 1), fieldSize,
                                     ImGuiInputTextFlags.ReadOnly);
        }
        else
        {
            // Clicking a problem took the focus, so the field is activated again; the callback then places the cursor.
            if (_pendingCursorLine > 0)
                ImGui.SetKeyboardFocusHere();

            if (ImGui.InputTextMultiline("##code", ref value, MaxCodeLength, fieldSize, ImGuiInputTextFlags.CallbackAlways, _textCallback))
            {
                editResult |= InputEditStateFlags.Modified;
                _hasUnfinishedEdit = true;
            }

            if (ImGui.IsItemActivated())
                editResult |= InputEditStateFlags.Started;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                editResult |= InputEditStateFlags.Finished;
                _hasUnfinishedEdit = false;
            }

            if (ImGui.IsItemActive() && _cursorLine != _followedCursorLine)
            {
                _followedCursorLine = _cursorLine;
                KeepLineVisible(textTop + _cursorLine * lineHeight, lineHeight, visibleTop, visibleBottom);
            }
        }

        ImGui.PopStyleColor();
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopFont();
        return editResult;
    }

    private static void KeepLineVisible(float lineTop, float lineHeight, float visibleTop, float visibleBottom)
    {
        if (lineTop < visibleTop)
        {
            ImGui.SetScrollY(ImGui.GetScrollY() - (visibleTop - lineTop));
        }
        else if (lineTop + lineHeight > visibleBottom)
        {
            ImGui.SetScrollY(ImGui.GetScrollY() + (lineTop + lineHeight - visibleBottom));
        }
    }

    private static void DrawProblems(float width)
    {
        ImGui.PushFont(Fonts.FontSmall);
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        var visibleRows = Math.Clamp(_problems.Count, 1, MaxVisibleProblemRows);
        ImGui.BeginChild("##problems", new Vector2(width, MathF.Ceiling(visibleRows * rowHeight)));

        if (_problems.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("No problems");
            ImGui.PopStyleColor();
        }

        var labelWidth = 0f;
        foreach (var problem in _problems)
            labelWidth = MathF.Max(labelWidth, ImGui.CalcTextSize(problem.Label).X);

        labelWidth = MathF.Round(labelWidth + 8 * T3Ui.UiScaleFactor);
        for (var index = 0; index < _problems.Count; index++)
        {
            var problem = _problems[index];
            ImGui.PushID(index);
            var rowStart = ImGui.GetCursorScreenPos();
            var canJump = problem.TargetLine > 0;
            if (ImGui.InvisibleButton("##problem", new Vector2(width, rowHeight)) && canJump)
                JumpTo(problem);

            var isHovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            if (isHovered && canJump)
                drawList.AddRectFilled(rowStart, rowStart + new Vector2(width, rowHeight), UiColors.BackgroundHover);

            var color = problem.IsError ? UiColors.StatusError : UiColors.StatusWarning;
            drawList.AddText(rowStart, color, problem.Label);
            drawList.AddText(rowStart + new Vector2(labelWidth, 0), isHovered ? UiColors.ForegroundFull : UiColors.Text, problem.Text);
            if (isHovered)
                CustomComponents.TooltipForLastItem(problem.Text, problem.Note);

            ImGui.PopID();
        }

        ImGui.EndChild();
        ImGui.PopFont();
    }

    private static void JumpTo(Problem problem)
    {
        _showCompiledSource = problem.IsInCompiledSource;
        _scrollToLine = problem.TargetLine;
        _pendingCursorLine = problem.IsInCompiledSource ? 0 : problem.TargetLine;
    }

    private static Color? FindMark(int lineNumber, bool showsCompiled)
    {
        Color? mark = null;
        foreach (var problem in _problems)
        {
            if (problem.TargetLine != lineNumber || problem.IsInCompiledSource != showsCompiled)
                continue;

            if (problem.IsError)
                return UiColors.StatusError;

            mark = UiColors.StatusWarning;
        }

        return mark;
    }

    /// <summary>
    /// Rebuilt only when what it's made from changed, so the labels are not formatted every frame. A line in the
    /// compiled source maps to the code when it falls into it; otherwise it can only be shown in the compiled source.
    /// </summary>
    private static void UpdateProblems(IReadOnlyList<ShaderCompileFailures.Diagnostic> diagnostics, InputStatus? status, int firstLine,
                                       int snippetLineCount)
    {
        var statusMessage = status?.Message;
        if (ReferenceEquals(diagnostics, _problemsDiagnostics) && ReferenceEquals(statusMessage, _problemsStatusMessage)
                                                               && firstLine == _problemsFirstLine && snippetLineCount == _problemsSnippetLineCount)
            return;

        _problemsDiagnostics = diagnostics;
        _problemsStatusMessage = statusMessage;
        _problemsFirstLine = firstLine;
        _problemsSnippetLineCount = snippetLineCount;
        _problems.Clear();

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.File.Length > 0)
            {
                _problems.Add(new Problem(diagnostic.IsError, $"{diagnostic.File}:{diagnostic.Line}", diagnostic.Text, 0, false,
                                          "In an included file, which can't be edited here."));
                continue;
            }

            var snippetLine = firstLine > 0 ? diagnostic.Line - firstLine + 1 : 0;
            if (snippetLine >= 1 && snippetLine <= snippetLineCount)
            {
                _problems.Add(new Problem(diagnostic.IsError, $"Line {snippetLine}", diagnostic.Text, snippetLine, false, null));
            }
            else if (diagnostic.Line > 0)
            {
                _problems.Add(new Problem(diagnostic.IsError, $"Template line {diagnostic.Line}", diagnostic.Text, diagnostic.Line, true,
                                          "Outside your code. Click to show it in the compiled source."));
            }
            else
            {
                _problems.Add(new Problem(diagnostic.IsError, string.Empty, diagnostic.Text, 0, false, null));
            }
        }

        if (diagnostics.Count > 0 || status is not { Message.Length: > 0 } shown)
            return;

        // Ops that check their code themselves (e.g. C# snippets) report one problem, already counted within the code.
        var text = shown.Message;
        var lineEnd = text.IndexOf('\n');
        if (lineEnd >= 0)
            text = text[..lineEnd];

        if (shown.Line > 0 && text.StartsWith("Line ", StringComparison.OrdinalIgnoreCase) && text.IndexOf(':') is var colon and > 0)
            text = text[(colon + 1)..].Trim();

        _problems.Add(new Problem(shown.IsError, shown.Line > 0 ? $"Line {shown.Line}" : string.Empty, text, shown.Line, false, null));
    }

    private static unsafe int OnTextCallback(ImGuiInputTextCallbackData* rawData)
    {
        var data = new ImGuiInputTextCallbackDataPtr(rawData);
        var buffer = (byte*)data.Buf;
        if (_pendingCursorLine > 0)
        {
            var offset = 0;
            for (var line = 1; offset < data.BufTextLen && line < _pendingCursorLine; offset++)
            {
                if (buffer[offset] == '\n')
                    line++;
            }

            data.CursorPos = offset;
            data.SelectionStart = offset;
            data.SelectionEnd = offset;
            _pendingCursorLine = 0;
        }

        var cursorLine = 0;
        var end = Math.Min(data.CursorPos, data.BufTextLen);
        for (var index = 0; index < end; index++)
        {
            if (buffer[index] == '\n')
                cursorLine++;
        }

        _cursorLine = cursorLine;
        return 0;
    }

    private static int CountLines(string text)
    {
        var count = 1;
        foreach (var c in text)
        {
            if (c == '\n')
                count++;
        }

        return count;
    }

    /** Cached, so drawing the gutter doesn't format numbers every frame. */
    private static string GetNumberLabel(int number)
    {
        while (_numberLabels.Count <= number)
            _numberLabels.Add(_numberLabels.Count.ToString());

        return _numberLabels[number];
    }

    /// <param name="TargetLine">The line to jump to, counted in the code or in the compiled source; 0 when it can't be shown.</param>
    private readonly record struct Problem(bool IsError, string Label, string Text, int TargetLine, bool IsInCompiledSource, string? Note);

    private const string PopupId = "##codeEditor";
    private const uint MaxCodeLength = 32000;
    private const int MaxVisibleProblemRows = 4;

    /** Unscaled size of the code area. */
    private static readonly Vector2 MinSize = new(280, 120);

    private static Vector2 _size = new(560, 320);
    private static bool _isResizing;
    private static bool _isMoving;
    private static bool _showCompiledSource;
    private static Guid _instanceId;
    private static Guid _inputId;
    private static bool _hasUnfinishedEdit;

    private static int _pendingCursorLine;
    private static int _scrollToLine;
    private static int _cursorLine;
    private static int _followedCursorLine = -1;

    private static readonly List<Problem> _problems = [];
    private static IReadOnlyList<ShaderCompileFailures.Diagnostic>? _problemsDiagnostics;
    private static string? _problemsStatusMessage;
    private static int _problemsFirstLine;
    private static int _problemsSnippetLineCount;

    private static readonly List<string> _numberLabels = [];
    private static readonly unsafe ImGuiInputTextCallback _textCallback = OnTextCallback;
}
