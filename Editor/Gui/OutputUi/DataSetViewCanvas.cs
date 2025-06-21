using System.Text;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine.Raster;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Provides a simple canvas to visualize a <see cref="DataSet"/>.
/// </summary>
public sealed class DataSetViewCanvas
{
    public void Draw(DataSet dataSet)
    {
        if (dataSet == null)
            return;
        
        // Very ugly hack to prevent scaling the output above window size
        var keepScale = T3Ui.UiScaleFactor;
        T3Ui.UiScaleFactor = 1;
        DrawCanvas();
        T3Ui.UiScaleFactor = keepScale;
        return;
        
        void DrawCanvas()
        {
            var currentTime = Playback.RunTimeInSecs;
            ImGui.BeginChild("Scrollable", Vector2.Zero, false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
            
            var isRangeSelected = Math.Abs(_selectRangeStart - _selectRangeEnd) > 0.001;
            var areChannelsSelected = _selectedChannels.Count > 0;
            
            if (ShowInteraction)
            {
                CustomComponents.ToggleButton("Only Recent", ref _onlyRecentEvents, Vector2.Zero);
                ImGui.SameLine();
                CustomComponents.ToggleButton("Scroll", ref _scroll, Vector2.Zero);
                
                //ImGui.Checkbox("Filter Recent ", ref _onlyRecentEvents);
                // ImGui.Checkbox("Scroll ", ref _scroll);
                
                // Show stats
                {
                    ImGui.SameLine(0, 20);
                    
                    ImGui.PushStyleColor(ImGuiCol.Text, areChannelsSelected || isRangeSelected ? UiColors.ForegroundFull.Rgba : UiColors.TextMuted.Rgba);
                    
                    var channelCount = areChannelsSelected ? _selectedChannels.Count : dataSet.Channels.Count;
                    var selectedDuration = Math.Abs(_selectRangeStart - _selectRangeEnd);
                    if (selectedDuration < 0.001)
                    {
                        selectedDuration = _lastEventTime - _firstEventTime;
                    }
                    
                    var rangeLabel = selectedDuration switch
                                         {
                                             < 0.001 => string.Empty,
                                             < 1     => $"{(selectedDuration * 1000):0.0}ms",
                                             _       => $"{selectedDuration:0.0}s"
                                         };
                    
                    if (ImGui.Button($"{channelCount} Channels / {_activeEventCount} Events / {rangeLabel}###stats"))
                    {
                        Log.Debug(" clear");
                        _selectedChannels.Clear();
                        _selectRangeStart = 0;
                        _selectRangeEnd = 0;
                    }
                    
                    ImGui.SameLine(0, 1);
                    ImGui.PopStyleColor();
                    
                    if (ImGui.Button("Remove"))
                    {
                        var list = areChannelsSelected ? _selectedChannels.ToList() : dataSet.Channels;
                        foreach (var channel in list)
                        {
                            if (isRangeSelected)
                            {
                                var sortedMin = _selectRangeStart;
                                var sortedMax = _selectRangeEnd;
                                if (sortedMin > sortedMax)
                                {
                                    (sortedMin, sortedMax) = (sortedMax, sortedMin);
                                }
                                
                                var eventsInRange = channel.Events.Where(e => e.Time >= sortedMin && e.Time <= sortedMax).ToList();
                                foreach (var e in eventsInRange)
                                {
                                    channel.Events.Remove(e);
                                }
                            }
                            else
                            {
                                channel.Events.Clear();
                            }
                        }
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Copy as CSV"))
                    {
                        try
                        {
                            var sb = new StringBuilder();
                            var separator = "\t";
                            var channelList = areChannelsSelected ? _selectedChannels.ToList() : dataSet.Channels;
                            
                            sb.Append("Time");
                            sb.Append(separator);
                            
                            double sortedMin;
                            double sortedMax;
                            
                            if (isRangeSelected)
                            {
                                sortedMin = _selectRangeStart;
                                sortedMax = _selectRangeEnd;
                                if (sortedMin > sortedMax)
                                {
                                    (sortedMin, sortedMax) = (sortedMax, sortedMin);
                                }
                            }
                            else
                            {
                                sortedMin = _firstEventTime;
                                sortedMax = _lastEventTime;
                            }
                            
                            const float timeResolution = 1 / 60f;
                            var maxTimeSlotCount = (int)((sortedMax - sortedMin) / timeResolution);
                            if (maxTimeSlotCount > 0 && channelList.Count > 0)
                            {
                                var timeSlots = new DataEvent[maxTimeSlotCount, channelList.Count];
                                
                                for (var channelIndex = 0; channelIndex < channelList.Count; channelIndex++)
                                {
                                    var channel = channelList[channelIndex];
                                    sb.Append(channel.Path.Last());
                                    sb.Append(separator);
                                    
                                    var minIndex = channel.FindIndexForTime(sortedMin);
                                    var maxIndex = channel.FindIndexForTime(sortedMax);
                                    for (var i = minIndex; i < maxIndex; i++)
                                    {
                                        var e = channel.Events[i];
                                        var timeSlotIndex = (int)((e.Time - sortedMin) / timeResolution);
                                        if (timeSlotIndex >= 0 && timeSlotIndex < maxTimeSlotCount)
                                            timeSlots[timeSlotIndex, channelIndex] = e;
                                    }
                                }
                                
                                sb.Append("\n");
                                
                                for (var rowIndex = 0; rowIndex < maxTimeSlotCount; rowIndex++)
                                {
                                    var isAnyNonEmpty = false;
                                    for (var channelIndex = 0; channelIndex < channelList.Count; channelIndex++)
                                    {
                                        if (timeSlots[rowIndex, channelIndex] != null)
                                        {
                                            isAnyNonEmpty = true;
                                            break;
                                        }
                                    }
                                    
                                    if (!isAnyNonEmpty)
                                        continue;
                                    
                                    sb.Append($"{sortedMin + rowIndex * timeResolution:0.000}");
                                    sb.Append(separator);
                                    for (var channelIndex = 0; channelIndex < channelList.Count; channelIndex++)
                                    {
                                        var e = timeSlots[rowIndex, channelIndex];
                                        if (e == null)
                                        {
                                            sb.Append(separator);
                                        }
                                        else
                                        {
                                            sb.Append(e.Value);
                                        }
                                        
                                        sb.Append(separator);
                                    }
                                    
                                    sb.AppendLine();
                                }
                            }
                            
                            ImGui.SetClipboardText(sb.ToString());
                        }
                        catch (Exception e)
                        {
                            Log.Warning("Failed to copy as CSV: " + e.Message);
                        }
                    }
                }
                
                // This is too unstable to expose it to users
                // ImGui.SameLine(0, 20);
                // if (ImGui.Button("Export as JSON"))
                // {
                //     dataSet.WriteToFile();
                // }
            }
            
            _standardRaster.Draw(_canvas);
            
            _canvas.UpdateCanvas(out var interactionState);
            
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _scrollPosStart = ImGui.GetScrollY();
                _isDraggingCanvas = true;
            }
            
            if (_isDraggingCanvas && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                _isDraggingCanvas = false;
            }
            
            if (_isDraggingCanvas)
            {
                var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Right);
                var newScrollY = _scrollPosStart - dragDelta.Y;
                ImGui.SetScrollY((int)newScrollY);
            }
            
            if (_scroll)
            {
                var windowWidth = ImGui.GetWindowWidth();
                var width = _canvas.InverseTransformDirection(ImGui.GetWindowSize()).X;
                var scale = windowWidth / width;
                
                _canvas.SetViewInstant(new Vector2(scale, -1),
                                            new Vector2((float)currentTime - (windowWidth - 50 * T3Ui.UiScaleFactor) / scale, 0));                
            }
            
            var dl = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var max = ImGui.GetContentRegionAvail() + min;
            
            var visibleMinTime = _canvas.InverseTransformX(min.X+200);
            var visibleMaxTime = _canvas.InverseTransformX(max.X);
            
            const int maxVisibleEvents = 500;
            const float layerHeight = 20f;
            var plotCurvePoints = new Vector2[maxVisibleEvents];
            
            _pathTreeDrawer.Reset();
            
            // Draw hovered time
            var mouseMouse = ImGui.GetMousePos();
            if (ImGui.IsWindowHovered())
            {
                var mousePos = mouseMouse;
                dl.AddRectFilled(new Vector2(mousePos.X, min.Y), new Vector2(mousePos.X + 1, max.Y), UiColors.WidgetActiveLine.Fade(0.1f));
            }
            
            // Draw Selection range...
            if (isRangeSelected)
            {
                var start = _canvas.TransformX((float)_selectRangeStart);
                var end = _canvas.TransformX((float)_selectRangeEnd);
                if (start > end)
                {
                    (start, end) = (end, start);
                }
                
                var min1 = new Vector2(start, ImGui.GetWindowPos().Y);
                var max1 = new Vector2(end, ImGui.GetWindowPos().Y + ImGui.GetWindowHeight());
                
                dl.AddRectFilled(min1, max1, UiColors.ForegroundFull.Fade(0.03f));
            }
            
            const int filterRecentEventDuration = 10;
            var dataSetChannels = dataSet.Channels.OrderBy(c => string.Join(".", c.Path));
            
            _visibleChannelCount = 0;
            _activeEventCount = 0;
            _firstEventTime = double.PositiveInfinity;
            _lastEventTime = double.NegativeInfinity;
            
            foreach (var channel in dataSetChannels)
            {
                var newVisibleRange = new ValueRange() { Min = float.PositiveInfinity, Max = float.NegativeInfinity };
                var pathString = string.Join(" / ", channel.Path); // This could be more efficient...
                var channelHash = pathString.GetHashCode();
                
                // Compute random unique color
                var randomChannelColor = DrawUtils.RandomColorForHash(channelHash);
                
                _channelValueRanges.TryGetValue(channelHash, out var valueRange);

                var lastEvent1 = channel.GetLastEvent();
                if (lastEvent1 == null)
                    continue;
                
                if (_onlyRecentEvents)
                {
                    var lastEvent = lastEvent1;
                    var tooOld = (lastEvent == null || lastEvent.Time < currentTime - filterRecentEventDuration);
                    if (tooOld)
                        continue;
                }
                
                var isVisible = _pathTreeDrawer.DrawEntry(channel.Path, MaxTreeLevel);
                if (!isVisible)
                    continue;
                
                var isActive = _selectedChannels.Count == 0 || _selectedChannels.Contains(channel);
                
                _visibleChannelCount++;
                
                if (isActive)
                {
                    if (isRangeSelected)
                    {
                        var indexMin = channel.FindIndexForTime(_selectRangeStart);
                        var indexMax = channel.FindIndexForTime(_selectRangeEnd);
                        _activeEventCount += Math.Abs(indexMax - indexMin);
                    }
                    else
                    {
                        _activeEventCount += channel.Events.Count;
                    }
                }
                
                var layerLabelMin = ImGui.GetCursorScreenPos();
                var layerMin = new Vector2(ImGui.GetWindowPos().X, layerLabelMin.Y);
                var layerMax = new Vector2(max.X, layerMin.Y + layerHeight);
                var layerHovered = ImGui.IsMouseHoveringRect(layerMin, layerMax);
                
                if (layerHovered)
                {
                    dl.AddRectFilled(layerMin, layerMax, UiColors.BackgroundFull.Fade(0.1f));
                }
                
                var visibleEventCount = 0;
                var visiblePlotPointCount = 0;
                
                var lastX = float.PositiveInfinity;
                
                // binary search index with highest visible time
                var latestVisibleIndex = channel.FindIndexForTime(visibleMaxTime);
                var oldestVisibleIndex = channel.FindIndexForTime(visibleMinTime, false);
                var visibleEventCountTotal = latestVisibleIndex - oldestVisibleIndex + 1;
                
                var stepSize = MathF.Max((float)visibleEventCountTotal / maxVisibleEvents, 1);
                
                if (channel.Events.Count > 0)
                {
                    lock (channel.Events)
                    {
                        _firstEventTime = Math.Min(_firstEventTime, channel.Events[0].Time);
                        _lastEventTime = Math.Max(_lastEventTime, channel.Events[^1].Time);
                    }
                }
                
                var keepSubWindowPos = ImGui.GetCursorScreenPos();
                if (ImGui.IsRectVisible(layerMin, layerMax))
                {
                    // Draw label flashing with recent events
                    if (channel.Events.Count > 0)
                    {
                        // Shade background
                        dl.AddRectFilled(layerMin + new Vector2(0, 1),
                                         layerMin + new Vector2(200, layerHeight),
                                         UiColors.WindowBackground.Fade(0.7f)
                                        );
                        
                        dl.AddRectFilledMultiColor(layerMin + new Vector2(200, 1),
                                                   layerMin + new Vector2(400, layerHeight),
                                                   UiColors.WindowBackground.Fade(0.7f),
                                                   UiColors.WindowBackground.Fade(0.0f),
                                                   UiColors.WindowBackground.Fade(0.0f),
                                                   UiColors.WindowBackground.Fade(0.7f)
                                                  );
                        
                        var lastEventAgeFactor = MathF.Pow((float)(currentTime - lastEvent1.Time).Clamp(0, 1) / 1, 0.2f);
                        var label = channel.Path.Last();
                        
                        if (!string.IsNullOrEmpty(label))
                        {
                            var isChannelSelected = _selectedChannels.Contains(channel);
                            var labelColor = Color.Mix(UiColors.ForegroundFull, randomChannelColor.Fade(0.5f), lastEventAgeFactor);
                            var keepPos = ImGui.GetCursorScreenPos();
                            ImGui.SetCursorScreenPos(layerLabelMin + new Vector2(0, 1));
                            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 0));
                            if (isChannelSelected)
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.BackgroundFull.Rgba);
                                ImGui.PushStyleColor(ImGuiCol.Button, labelColor.Rgba);
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, labelColor.Rgba);
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, labelColor.Rgba);
                            }
                            else
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
                                ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.ForegroundFull.Fade(0.1f).Rgba);
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.ForegroundFull.Fade(0.2f).Rgba);
                            }
                            
                            if (ImGui.Button(label))
                            {
                                if (isChannelSelected)
                                {
                                    _selectedChannels.Remove(channel);
                                }
                                else
                                {
                                    if (!ImGui.GetIO().KeyCtrl)
                                    {
                                        _selectedChannels.Clear();
                                    }
                                    
                                    _selectedChannels.Add(channel);
                                }
                            }
                            
                            ImGui.PopStyleColor(4);
                            ImGui.PopStyleVar();
                            ImGui.SetCursorScreenPos(keepPos);
                            
                            //ImGui.SameLine(0,0);
                            // dl.AddText(Fonts.FontNormal,
                            //            Fonts.FontNormal.FontSize,
                            //            layerLabelMin + new Vector2(10, 2),
                            //            labelColor,
                            //            label);
                            
                            // dl.AddText(Fonts.FontNormal,
                            //            Fonts.FontNormal.FontSize,
                            //            layerLabelMin + new Vector2(10, 2),
                            //            Color.Mix(UiColors.ForegroundFull, randomChannelColor.Fade(0.5f), lastEventAgeFactor),
                            //            label);
                        }
                    }
                    //ImGui.SameLine(0,100);
                    ImGui.SetCursorPosX(200);
                    using (new ChildWindowScope(channel.Path.Last(),
                                                new Vector2(ImGui.GetWindowSize().X-200, layerHeight),
                                                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav |
                                                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                                ImGuiWindowFlags.AlwaysAutoResize, Color.Transparent))
                    {
                        var dl2 = ImGui.GetWindowDrawList();
                        
                        // Draw events starting from the end (left to right)
                        for (float fIndex = latestVisibleIndex;
                             fIndex >= 0
                             //&& fIndex >= latestVisibleIndex - maxVisibleEvents
                             && fIndex >= oldestVisibleIndex
                             && visibleEventCount < maxVisibleEvents;
                             fIndex -= stepSize)
                        {
                            var index = (int)fIndex;
                            
                            var dataEvent = channel.Events[index];
                            var msg = dataEvent.Value == null ? "NULL" : dataEvent.Value.ToString();
                            
                            float markerYInLayer;
                            var value = float.NaN;
                            
                            if (dataEvent.TryGetNumericValue(out var numericValue))
                            {
                                value = (float)numericValue;
                                var fNormalized = ((value - valueRange.Min) / (valueRange.Max - valueRange.Min)).Clamp(0, 1);
                                markerYInLayer = (1 - fNormalized) * (layerHeight - 5) + 3;
                                
                                //string msg;
                                if (Math.Abs(value) < 0.0001)
                                    msg = "0";
                                else if (Math.Abs(value) < 10000)
                                {
                                    msg = $"{value:G5}";
                                }
                                else if (Math.Abs(value) < 1000000)
                                {
                                    msg = $"{value / 1000:0.0}K";
                                }
                                else if (Math.Abs(value) < 100000000)
                                {
                                    msg = $"{value / 1000000:0.0}M";
                                }
                            }
                            else
                            {
                                markerYInLayer = layerHeight - 3;
                            }
                            
                            float xStart;
                            
                            // Draw interval events
                            if (dataEvent is DataIntervalEvent intervalEvent)
                            {
                                if (!(intervalEvent.EndTime > visibleMinTime) || !(intervalEvent.Time < visibleMaxTime))
                                {
                                    continue;
                                }
                                
                                xStart = _canvas.TransformX((float)intervalEvent.Time);
                                
                                var endTime = intervalEvent.IsUnfinished ? currentTime : intervalEvent.EndTime;
                                var xEnd = MathF.Max(_canvas.TransformX((float)endTime), xStart + 1);
                                
                                dl2.AddRectFilled(new Vector2(xStart, layerMin.Y + markerYInLayer),
                                                  new Vector2(xEnd, layerMax.Y),
                                                  randomChannelColor.Fade(0.3f));
                                
                                if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(xStart, layerMin.Y), new Vector2(xEnd, layerMax.Y)))
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"{msg}\n{dataEvent.Time:0.000s} ... {endTime:0.000s}");
                                    ImGui.EndTooltip();
                                }
                            }
                            // Draw value events
                            else
                            {
                                if (!(dataEvent.Time > visibleMinTime) || !(dataEvent.Time < visibleMaxTime))
                                    continue;
                                
                                xStart = _canvas.TransformX((float)dataEvent.Time);
                                var y = layerMin.Y + markerYInLayer;
                                dl2.AddTriangleFilled(new Vector2(xStart, y - 3),
                                                      new Vector2(xStart + 2.5f, y + 2),
                                                      new Vector2(xStart - 2.5f, y + 2),
                                                      randomChannelColor);
                                
                                plotCurvePoints[visiblePlotPointCount++] = new Vector2(xStart, y);
                                
                                if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(xStart - 1, layerMin.Y), new Vector2(lastX, layerMax.Y)))
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"{msg}\n{dataEvent.Time:0.000s}");
                                    ImGui.EndTooltip();
                                }
                            }
                            
                            // Draw label if enough space
                            {
                                var shortMsg = msg.Length > 20 ? msg.Substring(0, 20) : msg;
                                var gapSize = lastX - xStart;
                                var estimatedWidth = shortMsg.Length * Fonts.FontSmall.FontSize * 0.6f;
                                
                                if (!string.IsNullOrEmpty(shortMsg) && gapSize > 20)
                                {
                                    var fade = MathUtils.RemapAndClamp(gapSize, estimatedWidth - 30, estimatedWidth + 60, 0, 1);
                                    dl2.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize,
                                                new Vector2(xStart + 8, layerMin.Y + 3),
                                                randomChannelColor.Fade(0.6f * fade), shortMsg);
                                }
                                
                                if (!float.IsNaN(value))
                                {
                                    newVisibleRange.Min = MathF.Min(newVisibleRange.Min, value);
                                    newVisibleRange.Max = MathF.Max(newVisibleRange.Max, value);
                                }
                            }
                            
                            lastX = xStart;
                            visibleEventCount++;
                        }
                        
                        // Adjust auto height of plot line
                        var newRange = new ValueRange(MathUtils.Lerp(valueRange.Min, newVisibleRange.Min, 0.1f),
                                                      MathUtils.Lerp(valueRange.Max, newVisibleRange.Max, 0.1f));
                        if (float.IsNaN(newRange.Min) || float.IsNaN(newRange.Max))
                            newRange = new ValueRange();
                        
                        _channelValueRanges[channelHash] = newRange;
                        
                        // Draw plot line
                        if (visiblePlotPointCount > 1)
                        {
                            dl.AddPolyline(ref plotCurvePoints[0], visiblePlotPointCount, randomChannelColor.Fade(0.2f), ImDrawFlags.None, 1);
                        }
                    }
                    
                    
                    //ImGui.EndChild();
                    ImGui.SetCursorScreenPos(keepSubWindowPos);
                    
                    // Line below layer
                    dl.AddLine(new Vector2(layerMin.X, layerMax.Y), layerMax, UiColors.GridLines.Fade(0.2f));
                }
                
                layerMin.Y += layerHeight;
                layerMax.Y += layerHeight;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + layerHeight);
            }
            
            // Handle selection range
            {
                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _isDraggingRange = true;
                    _selectRangeStart = _canvas.InverseTransformX(mouseMouse.X);
                }
                
                if (_isDraggingRange)
                {
                    _selectRangeEnd = _canvas.InverseTransformX(mouseMouse.X);
                }
                
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _isDraggingRange = false;
                }
            }
            
            // Log.Debug(" TotalVisibleEvents:" + totalVisibleEventCount);
            ImGui.Dummy(Vector2.One);
            _pathTreeDrawer.Complete();
            
            // Draw current time
            var xTime = _canvas.TransformX((float)currentTime);
            dl.AddRectFilled(new Vector2(xTime, min.Y), new Vector2(xTime + 1, max.Y), UiColors.WidgetActiveLine);
            dl.PopClipRect();
            ImGui.EndChild();
            ImGui.SetCursorPos(Vector2.Zero);
        }
    }
    
    private struct ValueRange
    {
        public ValueRange(float min, float max)
        {
            Min = min;
            Max = max;
        }
        
        public float Min;
        public float Max;
    }
    
    private readonly HashSet<DataChannel> _selectedChannels = new();
    private readonly Dictionary<int, ValueRange> _channelValueRanges = new();
    
    private int _visibleChannelCount;
    private int _activeEventCount;
    
    private bool _onlyRecentEvents = true;
    private bool _scroll = true;
    public bool ShowInteraction = true;
    public int MaxTreeLevel = 3;
    private double _selectRangeStart;
    private double _selectRangeEnd;
    private double _firstEventTime;
    private double _lastEventTime;
    
    private bool _isDraggingRange;
    
    private readonly PathTreeDrawer _pathTreeDrawer = new();
    private readonly StandardValueRaster _standardRaster = new() { EnableSnapping = true };
    
    private readonly ScalableCanvas _canvas = new CurrentGraphSubCanvas(initialScale: new Vector2(50, -50))
                                                  {
                                                      FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion,
                                                  };
    
    private float _scrollPosStart;
    private bool _isDraggingCanvas;
}