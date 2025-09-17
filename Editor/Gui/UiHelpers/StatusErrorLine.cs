using ImGuiNET;
using T3.Core.Resource;
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
        lock (_logEntries)
        {
            if (_logEntries.Count == 0)
            {
                ImGui.TextUnformatted("Log empty");
                return;
            }

            var lastEntry = _logEntries[^1];
            var color = ConsoleLogWindow.GetColorForLogLevel(lastEntry.Level)
                                        .Fade(((float)lastEntry.SecondsAgo).RemapAndClamp(0, 1.5f, 1, 0.4f));

            ImGui.PushFont(Fonts.FontBold);

            var firstLine = lastEntry.Message.AsSpan();
            var newlineIndex = firstLine.IndexOf('\n');
            
            if (newlineIndex >= 0)
                firstLine = firstLine[..newlineIndex];

            const int maxLength = 100;
            if (firstLine.Length > maxLength)
                firstLine = firstLine[..maxLength];

            if (firstLine.Length == 0)
                return;
            
            var width = ImGui.CalcTextSize(firstLine).X;
            
            var logMessage = lastEntry.Message;
            if (lastEntry.Level == ILogEntry.EntryLevel.Error)
            {
                logMessage = DX11ShaderCompiler.ExtractMeaningfulShaderErrorMessage(logMessage);
            }
            
            var availableSpace = ImGui.GetWindowContentRegionMax().X;
            ImGui.SetCursorPosX(availableSpace - width);

            ImGui.TextColored(color, firstLine);
            if (ImGui.IsItemClicked())
            {
                _logEntries.Clear();
            }
        }

        if (ImGui.IsItemHovered())
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

        ImGui.PopFont();
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