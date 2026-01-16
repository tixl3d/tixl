#nullable enable
using System.Text;
using ImGuiNET;
using Microsoft.VisualBasic.ApplicationServices;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// Renders the <see cref="ConsoleLogWindow"/>
/// </summary>
internal sealed class ConsoleLogWindow : Window, ILogWriter
{
    internal ConsoleLogWindow()
    {
        Config.Title = "Console";
        Config.Visible = true;
    }

    private readonly List<ILogEntry> _filteredEntries = new(1000);

    protected override void DrawContent()
    {
        if (FrameStats.Last.UiColorsChanged)
            _colorForLogLevel = UpdateLogLevelColors();

        CustomComponents.ToggleIconButton(ref _shouldScrollToBottom, Icon.ScrollLog, Vector2.Zero);
        //CustomComponents.ToggleButton("Scroll", ref _shouldScrollToBottom, Vector2.Zero);
        ImGui.SameLine();

        //ImGui.SetNextWindowSize(new Vector2(500, 400), ImGuiCond.FirstUseEver);
        if(CustomComponents.IconButton(Icon.ClearLog, Vector2.Zero))
        //if (ImGui.Button("Clear"))
        {
            lock (_logEntries)
            {
                _logEntries.Clear();
            }

            _shouldScrollToBottom = true;

            Log.Info("Console cleared!");
        }

        ImGui.SameLine();

        {
            var highlightFactor = (float)(ImGui.GetTime() - _lastCopyTime).Clamp(0, 1);
            ImGui.PushStyleColor(ImGuiCol.Button, Color.Mix( UiColors.StatusActivated, UiColors.BackgroundButton, highlightFactor).Rgba  );
            if(CustomComponents.IconButton(Icon.CopyToClipboard, Vector2.Zero))
            //if (ImGui.Button("Copy"))
            {
                lock (_logEntries)
                {
                    var sb = new StringBuilder();
                    foreach (var entry in _logEntries)
                    {
                        sb.Append($"{(entry.TimeStamp - _startTime).Ticks / 10000000f:  0.000}");
                        sb.Append('\t');
                        sb.Append(entry.Level);
                        sb.Append('\t');
                        sb.Append(entry.Message);
                        sb.Append('\n');
                    }

                    EditorUi.Instance.SetClipboardText(sb.ToString());
                }
            }
            ImGui.PopStyleColor();

            CustomComponents.TooltipForLastItem("Tip: You can also click on lines to copy them to the clipboard.");
        }

        ImGui.SameLine();
        CustomComponents.DrawInputFieldWithPlaceholder("Filter", ref _filterString);

        ImGui.Separator();
        var itemIndex = 0;
        ImGui.BeginChild("scrolling");
        {
            lock (_logEntries)
            {
                var items = _logEntries;
                if (FilterIsActive)
                {
                    _filteredEntries.Clear();
                    foreach (var e in _logEntries)
                    {
                        if (!e.Message.Contains(_filterString))
                            continue;

                        _filteredEntries.Add(e);
                    }

                    items = _filteredEntries;
                }

                if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel != 0)
                {
                    _shouldScrollToBottom = false;
                }

                unsafe
                {
                    var clipperData = new ImGuiListClipper();
                    var listClipperPtr = new ImGuiListClipperPtr(&clipperData);

                    listClipperPtr.Begin(items.Count, ImGui.GetTextLineHeightWithSpacing());
                    while (listClipperPtr.Step())
                    {
                        for (itemIndex = listClipperPtr.DisplayStart; itemIndex < listClipperPtr.DisplayEnd; ++itemIndex)
                        {
                            if (itemIndex < 0 || itemIndex >= items.Count)
                                continue;

                            DrawEntry(items[itemIndex]);
                        }
                    }

                    listClipperPtr.End();
                }
            }

            ImGui.TextColored(UiColors.Gray, "---"); // Indicator for end
            if (_shouldScrollToBottom)
            {
                ImGui.SetScrollY(ImGui.GetScrollMaxY() + ImGui.GetFrameHeight());
                _isAtBottom = true;
            }
            else
            {
                var mouse = ImGui.GetIO().MouseWheel;
                if (MathF.Abs(mouse) > 0)
                {
                    if ( ImGui.IsRectVisible(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
                    {
                        _shouldScrollToBottom = true;
                    }
                }
                
                _isAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - ImGui.GetWindowHeight();
            }

            if (itemIndex < _logEntries.Count)
            {
                var dl = ImGui.GetWindowDrawList();
                var bottomCenter = ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() * 0.5f, ImGui.GetWindowHeight());
                var lineHeight = ImGui.GetFrameHeight() * 1.4f;
                var min = ImGui.GetWindowPos() + new Vector2(0, ImGui.GetWindowHeight() - lineHeight);
                var max = ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight());
                dl.AddRectFilledMultiColor(min, max,
                                           UiColors.WindowBackground.Fade(0.0f),
                                           UiColors.WindowBackground.Fade(0.0f),
                                           UiColors.WindowBackground.Fade(1.0f),
                                           UiColors.WindowBackground.Fade(1.0f));

                var label = $"{_logEntries.Count - itemIndex} more lines...";
                var labelSize = ImGui.CalcTextSize(label);
                dl.AddText(bottomCenter - new Vector2(labelSize.X * 0.5f, ImGui.GetFrameHeight()), UiColors.Text, label);
            }
        }

        ImGui.Dummy(Vector2.One);

        ImGui.EndChild();
    }

    private static double _lastLineTime;

    public static void DrawEntry(ILogEntry entry)
    {
        var entryLevel = entry.Level;

        var time = entry.SecondsSinceStart;
        var dt = time - _lastLineTime;

        var fade = 1f;
        var frameFraction = (float)dt / (1.5f / 60f);
        if (frameFraction < 1)
        {
            fade = frameFraction.RemapAndClamp(0, 1, 0.2f, 0.8f);
        }

        // Timestamp
        var timeColor = UiColors.Text.Fade(fade);
        var timeLabel = $" {time:0.000}";
        var timeLabelSize = ImGui.CalcTextSize(timeLabel);
        ImGui.SetCursorPosX(80 - timeLabelSize.X);
        ImGui.TextColored(timeColor, timeLabel);
        _lastLineTime = time;
        ImGui.SameLine(90);

        float opacity = 1f;
        var nodeSelection = ProjectView.Focused?.NodeSelection;
        if (nodeSelection != null)
        {
            if (FrameStats.IsIdHovered(entry.SourceId))
                opacity = 0.8f;
        }

        var color = GetColorForLogLevel(entryLevel);

        var lineBreakIndex = entry.Message.IndexOf('\n');
        var hasLineBreaks = lineBreakIndex != -1;
        var firstLine = hasLineBreaks ? entry.Message[..lineBreakIndex] : entry.Message;

        var lineHovered = IsLineHovered(out var lineArea);

        if (lineHovered)
        {
            ImGui.GetWindowDrawList().AddRectFilled(lineArea.Min, lineArea.Max, Color.Mix(color, UiColors.BackgroundFull, 0.95f).Fade(0.3f));
        }
        
        // Try to get instance path...
        var entrySourceIdPath = entry.SourceIdPath;
        var hasInstancePath = Structure.TryGetInstanceFromPath(entrySourceIdPath, out var hoveredSourceInstance, out var readableInstancePath);

        // Instance
        if (readableInstancePath.Count > 0)
        {
            ImGui.TextColored(UiColors.TextMuted.Fade(0.4f),  readableInstancePath[0]);
            ImGui.SameLine(0, 10);
        }

        // Actual message
        ImGui.TextColored(color.Fade(opacity), firstLine);
        
        // Multiple Line indicator
        if (hasLineBreaks)
        {
            ImGui.SameLine();

            var lineCount = 1;
            foreach (var c in entry.Message)
            {
                if (c == '\n')
                    lineCount++;
            }
            ImGui.TextColored(UiColors.TextMuted.Fade(0.4f),  $"  {lineCount} lines...");
        }
        
        if (!lineHovered)
            return;
        

        var isMouseClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (isMouseClicked)
        {
            if (!string.IsNullOrEmpty(entry.Message))
            {
                EditorUi.Instance.SetClipboardText(entry.Message);
                _lastCopyTime = ImGui.GetTime();
            }
        }

        var hasLongMessages = entry.Message.Length > 100;
        if (!hasInstancePath && !hasLongMessages)
            return;

        FrameStats.AddHoveredId(entry.SourceId);

        CustomComponents.BeginTooltip(800);
        {
            // Show instance details
            if (hoveredSourceInstance != null)
            {
                ImGui.TextColored(UiColors.TextMuted, "select ");


                foreach (var p in readableInstancePath)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(UiColors.TextMuted, " / ");

                    ImGui.SameLine();
                    ImGui.TextUnformatted(p);
                }
            }

            if (hasLineBreaks || hasLongMessages)
            {
                ImGui.TextWrapped(entry.Message);
            }
        }
        CustomComponents.EndTooltip();

        if (isMouseClicked)
        {
            if (hoveredSourceInstance != null)
                ProjectView.Focused!.GraphView.OpenAndFocusInstance(entrySourceIdPath.ToList());
        }
    }

    public static Color GetColorForLogLevel(ILogEntry.EntryLevel entryLevel)
    {
        return _colorForLogLevel.TryGetValue(entryLevel, out var color)
                   ? color
                   : UiColors.TextMuted;
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }

    private static bool IsLineHovered(out ImRect areaOnScreen)
    {
        if (!ImGui.IsWindowHovered())
        {
            areaOnScreen = default;
            return false;
        }

        var min = new Vector2(ImGui.GetWindowPos().X, ImGui.GetItemRectMin().Y);
        var size = new Vector2(ImGui.GetWindowWidth() - 10, ImGui.GetItemRectSize().Y + LinePadding - 2);
        areaOnScreen = new ImRect(min, min + size);

        return areaOnScreen.Contains(ImGui.GetMousePos());
    }

    private static Dictionary<ILogEntry.EntryLevel, Color> _colorForLogLevel
        = UpdateLogLevelColors();

    private static Dictionary<ILogEntry.EntryLevel, Color> UpdateLogLevelColors()
    {
        return new Dictionary<ILogEntry.EntryLevel, Color>
                   {
                       { ILogEntry.EntryLevel.Debug, UiColors.TextMuted },
                       { ILogEntry.EntryLevel.Info, UiColors.Text },
                       { ILogEntry.EntryLevel.Warning, UiColors.StatusWarning },
                       { ILogEntry.EntryLevel.Error, UiColors.StatusError },
                   };
    }

    public void ProcessEntry(ILogEntry entry)
    {
        lock (_logEntries)
        {
            _logEntries.Add(entry);
        }

        if (_isAtBottom)
        {
            //_shouldScrollToBottom = true;
        }
    }

    public void Dispose()
    {
        _logEntries.Clear();
    }

    public ILogEntry.EntryLevel Filter { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    private bool FilterIsActive => !string.IsNullOrEmpty(_filterString);
    private const float LinePadding = 3;
    private readonly List<ILogEntry> _logEntries = [];
    private bool _shouldScrollToBottom = true;
    private string _filterString = "";
    private bool _isAtBottom = true;
    private readonly DateTime _startTime = DateTime.Now;
    private static double _lastCopyTime = double.NegativeInfinity;
}