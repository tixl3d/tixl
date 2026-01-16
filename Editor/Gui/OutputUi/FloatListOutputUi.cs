#nullable enable
using ImGuiNET;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.OutputUi;

internal sealed class FloatListOutputUi : OutputUi<List<float>>
{
    public override IOutputUi Clone()
    {
        return new FloatListOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    private enum ViewStyles
    {
        List,
        Grid,
        Plot,
        PlotAutoScale,
    }

    private sealed class ViewSettings
    {
        public ViewStyles ViewStyle = ViewStyles.Plot;

        public float MinFit = -1;
        public float MaxFit = 1;

        public float LastMax;
    }

    private static readonly Dictionary<string, ViewSettings> _viewSettingsForId = [];

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<List<float>> valueListSlot)
        {
            ImGui.TextUnformatted("Not a value list?");
            return;
        }

        var valueList = valueListSlot.Value;
        if (valueList == null)
        {
            ImGui.TextUnformatted("List is undefined (null)");
            return;
        }

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        viewId ??= string.Empty;
        var viewSettings = _viewSettingsForId.TryGetValue(viewId, out var settings)
                               ? settings
                               : _viewSettingsForId[viewId] = new ViewSettings();

        FormInputs.SegmentedButton(ref viewSettings.ViewStyle);

        switch (viewSettings.ViewStyle)
        {
            case ViewStyles.List:
                ImGui.TextUnformatted("not implemented");
                break;

            case ViewStyles.Grid:
            {
                ImGui.SameLine(0, 20);
                ImGui.TextUnformatted("Columns:");
                ImGui.SameLine();
                if (SingleValueEdit.Draw(ref UserSettings.Config.GridOutputColumnCount,
                                         new Vector2(ImGui.GetFrameHeight() * 2, ImGui.GetFrameHeight()),
                                         1,
                                         100,
                                         true,
                                         false, 0.1f) == InputEditStateFlags.Modified)
                {
                    UserSettings.Config.GridOutputColumnCount = UserSettings.Config.GridOutputColumnCount.Clamp(1, 100);
                }

                var indexColumnWidth = 40 * T3Ui.UiScaleFactor;
                var columnWidth = 60;
                ImGui.NewLine();
                FormInputs.AddVerticalSpace();

                // Draw Header
                for (var index = 0; index < UserSettings.Config.GridOutputColumnCount; index++)
                {
                    // ImGui.SameLine(columnWidth * T3Ui.UiScaleFactor * index + indexColumnWidth);
                    // CustomComponents.StylizedText($"{index}", Fonts.FontSmall, UiColors.TextMuted);
                    ImGui.SameLine(columnWidth * T3Ui.UiScaleFactor * index + indexColumnWidth);
                    var text = $"{index}";
                    var textSize = ImGui.CalcTextSize(text);
                    ImGui.SameLine(columnWidth * (index + 1) * T3Ui.UiScaleFactor - textSize.X + indexColumnWidth);
                    CustomComponents.StylizedText(text, Fonts.FontSmall, UiColors.TextMuted);
                }

                ImGui.BeginChild("ScrollableList", new Vector2(0, 0), false,
                                 ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar);
                {
                    int columnIndex = 0;
                    var max = 0f;
                    var drawList = ImGui.GetWindowDrawList();

                    for (var index = 0; index < valueList.Count; index++)
                    {

                        if (columnIndex == 0)
                        {
                            CustomComponents.StylizedText($"#{index}", Fonts.FontSmall, UiColors.TextMuted);
                        }

                        var v = valueList[index];
                        var text = $"{v:0.00}";
                        var textSize = ImGui.CalcTextSize(text);

                        ImGui.SameLine(columnWidth * T3Ui.UiScaleFactor * columnIndex + indexColumnWidth);

                        max = float.Max(max, MathF.Abs(v));

                        // Draw Bar overlay
                        var normalizedHeight = MathF.Abs(v / viewSettings.LastMax);
                        if (!normalizedHeight._IsFinite())
                            normalizedHeight = 0;

                        if (MathF.Abs(normalizedHeight) > 0.00001)
                        {
                            if (v > 0)
                            {
                                var height = (int)(normalizedHeight * Fonts.FontSmall.FontSize - 0.5f);
                                var pos = ImGui.GetCursorScreenPos() + new Vector2(4, Fonts.FontSmall.FontSize - height);
                                var size = new Vector2(columnWidth - 2,
                                                       height);
                                drawList.AddRectFilled(pos,
                                                       pos + size,
                                                       UiColors.BackgroundActive.Fade(0.2f));
                            }
                            else
                            {
                                var height = (int)(normalizedHeight * Fonts.FontSmall.FontSize + 0.5f);
                                var pos = ImGui.GetCursorScreenPos() + new Vector2(4, 2);
                                var size = new Vector2(columnWidth - 2, height);
                                drawList.AddRectFilled(pos, pos + size, UiColors.StatusAttention.Fade(0.2f));
                            }
                        }

                        var opacity = (MathF.Pow(normalizedHeight, 0.3f) + 0.3f).Clamp(0, 1) * 0.8f;
                        var color = v < 0 ? UiColors.StatusAttention : UiColors.Text;
                        ImGui.PushStyleColor(ImGuiCol.Text, color.Fade(opacity).Rgba);
                        ImGui.SameLine(columnWidth * T3Ui.UiScaleFactor * (columnIndex + 1) - textSize.X + indexColumnWidth);
                        ImGui.TextUnformatted(text);
                        ImGui.PopStyleColor();

                        columnIndex++;

                        if (columnIndex == UserSettings.Config.GridOutputColumnCount)
                        {
                            columnIndex = 0;
                        }
                    }

                    viewSettings.LastMax = max;
                }
                ImGui.EndChild();

                break;
            }
            case ViewStyles.Plot:
            {
                if (valueList.Count > 1)
                {
                    var plotLength = Math.Min(MaxPlotValueCount, valueList.Count);
                    valueList.CopyTo(0, _plotArray, 0, plotLength);

                    ImGui.PlotLines("##values", ref _plotArray[0],
                                    plotLength,
                                    0,
                                    "",
                                    viewSettings.MinFit,
                                    viewSettings.MaxFit,
                                    new Vector2(ImGui.GetContentRegionAvail().X, 200));

                    FormInputs.AddFloat("Max", ref viewSettings.MaxFit);
                    FormInputs.AddFloat("Min", ref viewSettings.MinFit);
                }

                if (valueList.Count > 0)
                {
                    var min = float.PositiveInfinity;
                    var max = float.NegativeInfinity;
                    var sum = 0f;
                    foreach (var number in valueList)
                    {
                        sum += number;
                        min = Math.Min(min, number);
                        max = Math.Max(max, number);
                    }

                    ImGui.TextUnformatted($"{valueList.Count}  between {min:G5} .. {max:G5}  avg {sum / valueList.Count:G5}");
                }

                break;
            }

            case ViewStyles.PlotAutoScale:
            {
                var plotLength = Math.Min(MaxPlotValueCount, valueList.Count);
                valueList.CopyTo(0, _plotArray, 0, plotLength);

                ImGui.PlotLines("##values", ref _plotArray[0], plotLength);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private const int MaxPlotValueCount = 1024;
    private static readonly float[] _plotArray = new float[MaxPlotValueCount];
}