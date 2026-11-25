#nullable enable
using System.Reflection;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.TableView;

public static class TableList
{
    /// <param name="previousList">The list shown in the previous frame. Values that differ from it are tinted, see <see cref="TableValueStyle"/>.</param>
    public static bool Draw(StructuredList list, StructuredList? previousList = null)
    {
        return Draw(list, Vector2.Zero, previousList);
    }

    /// <summary>The element under the mouse in the list drawn last, or -1; e.g. to mark it on a preview.</summary>
    public static int LastHoveredRow { get; private set; } = -1;

    public static Dictionary<Type, string[]> TypeComponents = new()
                                                                  {
                                                                      { typeof(Vector2), new[] { ".x", ".y" } },
                                                                      { typeof(Vector3), new[] { ".x", ".y", ".z" } },
                                                                      { typeof(Vector4), new[] { ".R", ".G", ".B", ".A" } },
                                                                      { typeof(Quaternion), new[] { ".x", ".y", ".z", ".w" } },
                                                                  };

    public static bool Draw(StructuredList list, Vector2 size, StructuredList? previousList = null)
    {
        return DrawTable(list, size, 0, MaxDrawnRows, false, previousList);
    }

    /// <summary>
    /// Draws a range of rows for a list embedded in a layout like the parameter window: the start index and
    /// row count fields go on the current line, the table is only as tall as its rows, and a footer offers more rows.
    /// The range is kept per ImGui id, so push a distinct id for each list drawn in the same window.
    /// </summary>
    public static bool DrawCompact(StructuredList list)
    {
        var startIndexKey = ImGui.GetID("tableStartIndex");
        var rowCountKey = ImGui.GetID("tableRowCount");
        var storage = ImGui.GetStateStorage();

        var elementCount = list.NumElements;
        var startIndex = storage.GetInt(startIndexKey, 0);
        var rowCount = storage.GetInt(rowCountKey, DefaultCompactRowCount);

        DrawRowRangeFields(elementCount, ref startIndex, ref rowCount);
        startIndex = Math.Clamp(startIndex, 0, Math.Max(elementCount - 1, 0));
        var shownRowCount = Math.Clamp(rowCount, 0, elementCount - startIndex);

        var modified = DrawTable(list, Vector2.Zero, startIndex, shownRowCount, true, null);

        if (elementCount > DefaultCompactRowCount)
            DrawShowMoreFooter(startIndex, shownRowCount, elementCount, ref rowCount);

        storage = ImGui.GetStateStorage();
        storage.SetInt(startIndexKey, startIndex);
        storage.SetInt(rowCountKey, rowCount);
        return modified;
    }

    private static void DrawRowRangeFields(int elementCount, ref int startIndex, ref int rowCount)
    {
        // Matches the start/count controls of the buffer table in the output window.
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Start", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine(0, 4 * scale);
        ImGui.SetNextItemWidth(70 * scale);
        ImGui.DragInt("##startIndex", ref startIndex, 1, 0, Math.Max(elementCount - 1, 0));

        ImGui.SameLine(0, 8 * scale);
        CustomComponents.StylizedText("Count", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine(0, 4 * scale);
        ImGui.SetNextItemWidth(60 * scale);
        var shownCount = Math.Min(rowCount, elementCount);
        if (ImGui.DragInt("##rowCount", ref shownCount, 1, 1, Math.Max(elementCount, 1)))
            rowCount = shownCount;

        ImGui.PopStyleColor();
    }

    private static void DrawShowMoreFooter(int startIndex, int shownRowCount, int elementCount, ref int rowCount)
    {
        var hasHiddenRows = shownRowCount < elementCount;
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(GetRowRangeLabel(startIndex, shownRowCount, elementCount), Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine();

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.SmallButton(hasHiddenRows ? "Show More" : "Show Fewer");
        ImGui.PopFont();

        _rowCountInMenu = rowCount;
        _rowCountPickedInMenu = 0;
        CustomComponents.ContextMenuForItem(DrawRowCountMenuItems, id: "##rowCountMenu", flags: ImGuiPopupFlags.MouseButtonLeft);
        if (_rowCountPickedInMenu > 0)
            rowCount = _rowCountPickedInMenu;
    }

    private static void DrawRowCountMenuItems()
    {
        for (var optionIndex = 0; optionIndex < _rowCountOptions.Length; optionIndex++)
        {
            var option = _rowCountOptions[optionIndex];
            if (CustomComponents.DrawMenuItem(_rowCountMenuItemIds[optionIndex], _rowCountOptionLabels[optionIndex], isChecked: _rowCountInMenu == option))
                _rowCountPickedInMenu = option;
        }
    }

    /** Cached because the label is drawn every frame but rarely changes. */
    private static string GetRowRangeLabel(int startIndex, int shownRowCount, int elementCount)
    {
        if (startIndex != _labelStartIndex || shownRowCount != _labelShownRowCount || elementCount != _labelElementCount)
        {
            _labelStartIndex = startIndex;
            _labelShownRowCount = shownRowCount;
            _labelElementCount = elementCount;
            _rowRangeLabel = shownRowCount == 0
                                 ? $"0 of {elementCount}"
                                 : $"{startIndex}-{startIndex + shownRowCount - 1} of {elementCount}";
        }

        return _rowRangeLabel;
    }

    private static bool DrawTable(StructuredList list, Vector2 size, int firstRow, int rowCount, bool fitHeightToRows, StructuredList? previousList)
    {
        LastHoveredRow = -1;
        var canCompare = previousList != null && previousList != list && previousList.Type == list.Type;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
        ImGui.PushFont(Fonts.FontSmall);
        var valueColumnWidth = 50 * T3Ui.UiScaleFactor;
        var lineNumberWidth = 40 * T3Ui.UiScaleFactor;
        var headerHeight = 30 * T3Ui.UiScaleFactor;
        var listModified = false;

        var windowFlags = ImGuiWindowFlags.None;
        var hasHorizontalScrollKey = 0u;
        if (fitHeightToRows)
        {
            // Sized from the previous frame's scrollbar state, so the rows are never covered by it.
            hasHorizontalScrollKey = ImGui.GetID("tableHasHorizontalScroll");
            var hadHorizontalScroll = ImGui.GetStateStorage().GetBool(hasHorizontalScrollKey);
            var style = ImGui.GetStyle();
            size.Y = MathF.Ceiling(Math.Max(headerHeight, ImGui.GetFrameHeight())
                                   + rowCount * (ImGui.GetFrameHeight() + style.ItemSpacing.Y)
                                   + (hadHorizontalScroll ? style.ScrollbarSize : 0)) + 1;
            windowFlags = ImGuiWindowFlags.HorizontalScrollbar;
        }

        ImGui.BeginChild("child", size, ImGuiChildFlags.None, windowFlags);
        {
            FieldInfo[] members = list.Type.GetFields();

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"[{list.NumElements}]");
            ImGui.SameLine(lineNumberWidth);

            // List Header 

            for (var fieldIndex = 0; fieldIndex < members.Length; fieldIndex++)
            {
                var fi = members[fieldIndex];
                if (TypeComponents.TryGetValue(fi.FieldType, out var components))
                {
                    bool isFirst = true;
                    
                    foreach (var c in components)
                    {
                        ImGui.PushID(fi.Name + c);
                        ImGui.Selectable((isFirst ? " " + fi.Name : "_") + "\n" + c, 
                                         false, 
                                         ImGuiSelectableFlags.None,
                                         new Vector2(valueColumnWidth, headerHeight));
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(fieldIndex + ": " +fi.Name + c );
                        }
                        ImGui.PopID();    
                        ImGui.SameLine();
                        isFirst = false;
                        
                    }
                }
                else
                {
                    ImGui.Selectable(" " + fi.Name, 
                                     false, ImGuiSelectableFlags.None, 
                                     new Vector2(valueColumnWidth, 
                                                 headerHeight));
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(fieldIndex + ": " +fi.Name);
                    }
                }

                ImGui.SameLine();
            }

            ImGui.NewLine();
            
            // Values
            var endRow = Math.Min(list.NumElements, firstRow + Math.Min(rowCount, MaxDrawnRows));
            for (var objectIndex = firstRow; objectIndex < endRow; objectIndex++)
            {
                var cursorScreenPos = ImGui.GetCursorScreenPos();

                var isLineVisible = ImGui.IsRectVisible(cursorScreenPos,
                                                        cursorScreenPos + new Vector2(1000, ImGui.GetFrameHeight()));

                if (!isLineVisible)
                {
                    ImGui.Dummy(new Vector2(1, ImGui.GetFrameHeight()));
                    continue;
                }

                if (ImGui.IsWindowHovered()
                    && ImGui.IsMouseHoveringRect(cursorScreenPos, cursorScreenPos + new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight())))
                    LastHoveredRow = objectIndex;

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(objectIndex + ".");
                ImGui.SameLine(lineNumberWidth);

                ImGui.PushID(objectIndex);
                var obj = list[objectIndex];
                var previousObj = canCompare && objectIndex < previousList!.NumElements ? previousList[objectIndex] : null;

                var objModified = false;
                for (var fieldIndex = 0; fieldIndex < members.Length; fieldIndex++)
                {
                    FieldInfo fi = members[fieldIndex];
                    var o = fi.GetValue(obj);
                    var p = previousObj != null ? fi.GetValue(previousObj) : o;
                    if (o is float f)
                    {
                        if (DrawFloatManipulation(ref f, p is float pf ? pf : f, objectIndex* 1000+ fieldIndex))
                        {
                            fi.SetValue(obj, f);
                            objModified = true;
                        }
                    }
                    else if (fi.FieldType == typeof(string))
                    {
                        if (!(o is string s))
                            s = string.Empty;

                        ImGui.PushID(fi.Name);
                        ImGui.SetNextItemWidth(valueColumnWidth);
                        if (ImGui.InputText("##sdf", ref s, 256))
                        {
                            fi.SetValue(obj, s);
                            objModified = true;
                        }

                        ImGui.SameLine();
                        ImGui.PopID();
                    }
                    else if (o is Vector2 vector2)
                    {
                        var previous2 = p is Vector2 pv2 ? pv2 : vector2;
                        if (DrawFloatManipulation(ref vector2.X, previous2.X, fieldIndex, 0)
                            | DrawFloatManipulation(ref vector2.Y, previous2.Y, fieldIndex, 1))
                        {
                            fi.SetValue(obj, vector2);
                            objModified = true;
                        }
                    }
                    else if (o is Vector3 vector3)
                    {
                        var previous3 = p is Vector3 pv3 ? pv3 : vector3;
                        if (DrawFloatManipulation(ref vector3.X, previous3.X, fieldIndex, 0)
                            | DrawFloatManipulation(ref vector3.Y, previous3.Y, fieldIndex, 1)
                            | DrawFloatManipulation(ref vector3.Z, previous3.Z, fieldIndex, 2))
                        {
                            fi.SetValue(obj, vector3);
                            objModified = true;
                        }
                    }
                    else if (o is Vector4 vector4)
                    {
                        var previous4 = p is Vector4 pv4 ? pv4 : vector4;
                        var firstCellPos = ImGui.GetCursorScreenPos();
                        if (DrawFloatManipulation(ref vector4.X, previous4.X, fieldIndex, 0)
                            | DrawFloatManipulation(ref vector4.Y, previous4.Y, fieldIndex, 1)
                            | DrawFloatManipulation(ref vector4.Z, previous4.Z, fieldIndex, 2)
                            | DrawFloatManipulation(ref vector4.W, previous4.W, fieldIndex, 3))
                        {
                            fi.SetValue(obj, vector4);
                            objModified = true;
                        }

                        if (TableValueStyle.IsColorField(fi))
                            TableValueStyle.DrawColorSwatch(firstCellPos, ImGui.GetFrameHeight(), vector4);
                    }
                    else if (o is Quaternion q)
                    {
                        var previousQ = p is Quaternion pq ? pq : q;
                        if (DrawFloatManipulation(ref q.X, previousQ.X, fieldIndex, 0)
                            | DrawFloatManipulation(ref q.Y, previousQ.Y, fieldIndex, 1)
                            | DrawFloatManipulation(ref q.Z, previousQ.Z, fieldIndex, 2)
                            | DrawFloatManipulation(ref q.W, previousQ.W, fieldIndex, 3))
                        {
                            fi.SetValue(obj, q);
                            objModified = true;
                        }
                    }

                    else
                    {
                        ImGui.SetNextItemWidth(valueColumnWidth);
                        ImGui.TextUnformatted("?");
                        ImGui.SameLine();
                    }
                }

                if (objModified)
                {
                    list[objectIndex] = obj;
                    listModified = true;
                }

                if (ImGui.Button("+"))
                {
                    list.Insert(objectIndex, obj);
                }

                ImGui.SameLine();

                if (ImGui.Button("-"))
                {
                    list.Remove(objectIndex);
                }

                ImGui.PopID();
            }
        }
        var hasHorizontalScroll = ImGui.GetScrollMaxX() > 0;
        ImGui.EndChild();
        ImGui.PopFont();
        ImGui.PopStyleVar(); // FramePadding

        if (fitHeightToRows)
            ImGui.GetStateStorage().SetBool(hasHorizontalScrollKey, hasHorizontalScroll);

        return listModified;

        bool DrawFloatManipulation(ref float f, float previous, int fieldIndex, int componentIndex = 0)
        {
            ImGui.PushID(fieldIndex * 1000 + componentIndex);
            ImGui.SetNextItemWidth(valueColumnWidth);
            ImGui.PushStyleColor(ImGuiCol.Text, TableValueStyle.GetColor(f, previous).Rgba);
            var fieldModified = ImGui.DragFloat("##sdf", ref f, 0.01f);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PopID();
            return fieldModified;
        }
    }

    private const int DefaultCompactRowCount = 10;
    private const int MaxDrawnRows = 9999;
    private const int ShowAllRows = int.MaxValue;

    private static readonly int[] _rowCountOptions = { DefaultCompactRowCount, 100, ShowAllRows };
    private static readonly string[] _rowCountOptionLabels = { "10 Rows", "100 Rows", "All Rows" };
    private static readonly int[] _rowCountMenuItemIds = { "tableRows10".GetHashCode(), "tableRows100".GetHashCode(), "tableRowsAll".GetHashCode() };

    /** Hands the row count in and out of the menu callback, which must not capture locals. */
    private static int _rowCountInMenu;
    private static int _rowCountPickedInMenu;

    private static string _rowRangeLabel = string.Empty;
    private static int _labelStartIndex = -1;
    private static int _labelShownRowCount = -1;
    private static int _labelElementCount = -1;
}