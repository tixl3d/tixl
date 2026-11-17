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

    public static Dictionary<Type, string[]> TypeComponents = new()
                                                                  {
                                                                      { typeof(Vector2), new[] { ".x", ".y" } },
                                                                      { typeof(Vector3), new[] { ".x", ".y", ".z" } },
                                                                      { typeof(Vector4), new[] { ".R", ".G", ".B", ".A" } },
                                                                      { typeof(Quaternion), new[] { ".x", ".y", ".z", ".w" } },
                                                                  };

    public static bool Draw(StructuredList list, Vector2 size, StructuredList? previousList = null)
    {
        var canCompare = previousList != null && previousList != list && previousList.Type == list.Type;
        ImGui.BeginChild("child", size);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
        var valueColumnWidth = 50 * T3Ui.UiScaleFactor;
        var lineNumberWidth = 40 * T3Ui.UiScaleFactor;
        var headerHeight = 30 * T3Ui.UiScaleFactor;
        var listModified = false;
        
        ImGui.PushFont(Fonts.FontSmall);
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
            for (var objectIndex = 0; objectIndex < Math.Min(list.NumElements, 9999); objectIndex++)
            {
                var cursorScreenPos = ImGui.GetCursorScreenPos();

                var isLineVisible = ImGui.IsRectVisible(cursorScreenPos,
                                                        cursorScreenPos + new Vector2(1000, ImGui.GetFrameHeight()));

                if (!isLineVisible)
                {
                    ImGui.Dummy(new Vector2(1, ImGui.GetFrameHeight()));
                    continue;
                }

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
        ImGui.PopFont();
        ImGui.PopStyleVar(); // FramePadding
        ImGui.EndChild();
            
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
}