using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.OutputUi;

internal sealed class FloatDictOutputUi : OutputUi<Dict<float>>
{
    public override IOutputUi Clone()
    {
        return new FloatDictOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    private static bool _autoFit = true;
    private static bool _clear = false;
    private static float _minFit = -1;
    private static float _maxFit = 1;

    private static List<List<float>> _previousChannelValues;

    private static readonly List<string> _keysForDrawing = new(10);
    private static readonly List<float> _valuesForDrawing = new(10);

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<Dict<float>> typedSlot)
        {

            // We copy this to avoid the dictionary being modified while we iterate over it
            _valuesForDrawing.Clear();
            _keysForDrawing.Clear();
                
            {
                var floatDict = typedSlot.Value;
                if (floatDict == null)
                    return;

                lock (floatDict)
                {
                    _valuesForDrawing.AddRange(floatDict.Values);
                    _keysForDrawing.AddRange(floatDict.Keys);
                }
            }

            ImGui.BeginChild("##scrolling");

            var drawList = ImGui.GetWindowDrawList();

            var count = Math.Min(1024, _valuesForDrawing.Count);
            const int horizontalLength = 512;

            if (_clear)
                _previousChannelValues = null;

            if (_clear || _previousChannelValues == null)
                _previousChannelValues = new List<List<float>>();

            // Expand list of previous values if necessary
            if (count > _previousChannelValues.Count)
            {
                while (_previousChannelValues.Count < count)
                {
                    // And new list and initialize to zeroes
                    var newList = Enumerable.Repeat(0f, horizontalLength).ToList();
                    _previousChannelValues.Add(newList);
                }
            }

            for (var i = 0; i < count; i++)
            {
                _previousChannelValues[i].Add(_valuesForDrawing[i]);

                // Keep only a certain number of samples
                while (_previousChannelValues[i].Count > horizontalLength)
                    _previousChannelValues[i].RemoveAt(0);
            }

            // Print general user interface
            if (_previousChannelValues.Count > 0)
            {
                ImGui.Checkbox("Auto Fit", ref _autoFit);
                ImGui.SameLine();
                _clear = ImGui.Button("Clear");
                if (!_autoFit)
                {
                    ImGui.DragFloat("Max", ref _maxFit);
                    ImGui.DragFloat("Min", ref _minFit);
                }
            }
                
            FormInputs.AddVerticalSpace(5 * T3Ui.UiScaleFactor);

            for (var channelIndex = 0; channelIndex < _previousChannelValues.Count && channelIndex < _valuesForDrawing.Count; channelIndex++)
            {
                var currentValue = _valuesForDrawing[channelIndex];
                var currentName = _keysForDrawing[channelIndex];

                // Set plot color, repeating every 10 colors
                var hue = ((float)channelIndex * 360.0f / 10.0f) / 360.0f;
                hue -= (float)Math.Floor(hue);
                const float saturation = 0.5f;
                const float value = 0.75f;
                var plotColor = new Vector4(1f);

                ImGui.ColorConvertHSVtoRGB(hue, saturation, value,
                                           out plotColor.X, out plotColor.Y, out plotColor.Z);
                ImGui.PushStyleColor(ImGuiCol.PlotLines, plotColor);

                var floatArray = _previousChannelValues[channelIndex].ToArray();
                if (_autoFit)
                {
                    ImGui.PlotLines("##values",
                                    ref floatArray[0],
                                    _previousChannelValues[channelIndex].Count,
                                    0,
                                    "" 
                                   );
                }
                else
                {
                    ImGui.PlotLines("##values", ref floatArray[0],
                                    _previousChannelValues[channelIndex].Count,
                                    0,
                                    "", 
                                    _minFit,
                                    _maxFit);
                }

                var isLineHovered = ImGui.IsItemHovered();
                ImGui.PushFont(isLineHovered ? Fonts.FontBold : Fonts.FontNormal);
                var itemRectMin = ImGui.GetItemRectMin();
                var itemRectMax = ImGui.GetItemRectMax();
                drawList.AddRectFilledMultiColor(itemRectMin, itemRectMax,
                                                 UiColors.WindowBackground.Fade(0.8f),
                                                 UiColors.WindowBackground.Fade(0.0f),
                                                 UiColors.WindowBackground.Fade(0.0f),
                                                 UiColors.WindowBackground.Fade(0.8f)
                                                );

                drawList.AddText(itemRectMin + new Vector2(10, 2) * T3Ui.UiScaleFactor, 
                                 isLineHovered ? UiColors.ForegroundFull: UiColors.Text, $"{currentName}");

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    Log.Debug($"Copied {currentName} to clipboard");
                        
                    EditorUi.Instance.SetClipboardText(currentName);
                }
                    
                ImGui.PopFont();

                ImGui.SameLine();
                ImGui.TextUnformatted($"{currentValue:0.000}");

                var textColor = new Vector4(UiColors.TextMuted.R,
                                            UiColors.TextMuted.G,
                                            UiColors.TextMuted.B,
                                            UiColors.TextMuted.A);
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);

                if (_autoFit && _previousChannelValues[channelIndex].Count > 0)
                {
                    var min = float.PositiveInfinity;
                    var max = float.NegativeInfinity;
                    var sum = 0f;
                    foreach (var number in floatArray)
                    {
                        sum += number;
                        min = Math.Min(min, number);
                        max = Math.Max(max, number);
                    }

                    ImGui.SameLine();
                    ImGui.TextUnformatted($"  [{min:G3} .. {max:G3}]" +
                                          $"  avg {(sum / _previousChannelValues[channelIndex].Count):0.000}");
                }

                ImGui.PopStyleColor();
                ImGui.PopStyleColor();
            }

            ImGui.EndChild();
        }
        else
        {
            Debug.Assert(false);
        }
    }
}