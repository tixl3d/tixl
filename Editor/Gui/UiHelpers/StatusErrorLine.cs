using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Renders the <see cref="ConsoleLogWindow"/>
/// </summary>
internal sealed class StatusErrorLine : ILogWriter
{
    internal void Draw()
    {
        var hasEntries = false;
        var isMessageHovered = false;

        lock (_logEntries)
        {
            if (_logEntries.Count == 0)
            {
                ImGui.TextUnformatted("Log empty");
            }
            else
            {
                var lastEntry = _logEntries[^1];
                var color = ConsoleLogWindow.GetColorForLogLevel(lastEntry.Level)
                                            .Fade(((float)lastEntry.SecondsAgo).RemapAndClamp(0, 1.5f, 1, 0.4f));

                var firstLine = lastEntry.Message.AsSpan();
                var newlineIndex = firstLine.IndexOf('\n');

                if (newlineIndex >= 0)
                    firstLine = firstLine[..newlineIndex];

                const int maxLength = 100;
                if (firstLine.Length > maxLength)
                    firstLine = firstLine[..maxLength];

                hasEntries = firstLine.Length > 0;
                if (hasEntries)
                {
                    ImGui.PushFont(Fonts.FontBold);
                    var width = ImGui.CalcTextSize(firstLine).X;

                    // Keep room for the clear button (and the debug-server indicator) at the right edge.
                    var reservedSlots = App.DebugProtocol.DebugServer.IsRunning ? 2f : 1f;
                    var availableSpace = ImGui.GetWindowSize().X;
                    ImGui.SetCursorPosX(availableSpace - width - ImGui.GetFrameHeight() * reservedSlots);

                    ImGui.TextColored(color, firstLine);
                    ImGui.PopFont();

                    // Clicking a message must not destroy it: reveal it in the console instead.
                    if (ImGui.IsItemClicked())
                    {
                        Program.ConsoleLogWindow.RevealLatestEntries();
                    }

                    isMessageHovered = ImGui.IsItemHovered();
                }
            }
        }

        if (isMessageHovered)
        {
            ImGui.BeginTooltip();
            {
                lock (_logEntries)
                {
                    foreach (var entry in _logEntries)
                    {
                        ConsoleLogWindow.DrawEntry(entry);
                    }
                }
            }
            ImGui.EndTooltip();
        }

        if (!hasEntries)
            return;

        ImGui.SameLine(0, 0);
        if (CustomComponents.AttentionIconButton(Icon.ClearLog, Vector2.Zero))
        {
            lock (_logEntries)
            {
                _logEntries.Clear();
            }

            Program.ConsoleLogWindow.ClearLog();
        }

        CustomComponents.TooltipForLastItem("Clear log");
    }

    public void Dispose()
    {
    }

    public ILogEntry.EntryLevel Filter { get; set; }

    public void ProcessEntry(ILogEntry entry)
    {
        lock (_logEntries)
        {
            if (_logEntries.Count > 20)
            {
                _logEntries.RemoveAt(0);
            }

            _logEntries.Add(entry);
        }
    }

    private readonly List<ILogEntry> _logEntries = [];
}