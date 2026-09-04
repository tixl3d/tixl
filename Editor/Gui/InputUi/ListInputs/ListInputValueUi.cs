using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.ListInputs;

/// <summary>
/// Provides a generic UI for editing lists of base value types like int, float and string.
/// </summary>
internal abstract class ListInputValueUi<T> : InputValueUi<T>
{
    protected override void DrawReadOnlyControl(string name, ref T list)
    {
        string outputString;
        if (list == null)
        {
            outputString = "NULL";
        }
        else
        {
            lock (list)
            {
                outputString = string.Join(", ", list);
            }
        }

        ImGui.TextUnformatted($"{outputString}");
    }

    protected InputEditStateFlags DrawListInputControl<TT>(Symbol.Child.Input input, ref List<TT> list)
    {
        // Handle missing or empty list
        if (list == null)
        {
            if (ImGui.Button("Create"))
            {
                list = [];
                return InputEditStateFlags.Modified | InputEditStateFlags.Finished;
            }

            return InputEditStateFlags.Nothing;
        }

        if (list.Count == 0)
        {
            if (ImGui.Button("+"))
            {
                if (input.IsDefault)
                {
                    list = [];
                    input.IsDefault = false;
                }

                if (typeof(TT) == typeof(string))
                {
                    list.Add((TT)(object)string.Empty); // ensures it's a valid string
                }
                else
                {
                    list.Add(default);
                }
                return InputEditStateFlags.ModifiedAndFinished;
            }

            return InputEditStateFlags.Nothing;
        }

        if (ImGui.Button("Clear all"))
        {
            if (input.IsDefault)
            {
                list = [];
                input.IsDefault = false;
            }
            else
            {
                list.Clear();
            }

            return InputEditStateFlags.ModifiedAndFinished;
        }

        // List...
        if (!_isDragging && _listOrderWhileDragging.Count != list.Count)
        {
            _listOrderWhileDragging.Clear();
            for (var index = 0; index < list.Count; index++)
            {
                _listOrderWhileDragging.Add(index);
            }
        }

        var cloneIfModified = input.IsDefault;

        var modified = InputEditStateFlags.Nothing;
        var completedDragging = false;
        for (var index = 0; index < list.Count; index++)
        {
            var dragIndex = _isDragging
                                ? _listOrderWhileDragging[index]
                                : index;
            ImGui.PushID(dragIndex);
            ImGui.AlignTextToFramePadding();

            ImGui.Button($"{dragIndex}.");

            if (ImGui.IsItemActive())
            {
                _isDragging = true;
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var mouseY = ImGui.GetMousePos().Y;
                var halfHeight = ImGui.GetItemRectSize().Y / 2;
                var indexDelta = 0;
                if (mouseY < itemMin.Y - halfHeight && index > 0)
                {
                    indexDelta = -1;
                }
                else if (mouseY > itemMax.Y + halfHeight && index < list.Count - 1)
                {
                    indexDelta = 1;
                }

                if (indexDelta != 0)
                {
                    if (cloneIfModified)
                    {
                        list = [..list];
                        cloneIfModified = false;
                        input.IsDefault = false;
                    }

                    var newIndex = index + indexDelta;
                    if (newIndex >= 0 && index < list.Count && newIndex < list.Count)
                    {
                        (list[newIndex], list[index]) = (list[index], list[newIndex]);
                        (_listOrderWhileDragging[newIndex], _listOrderWhileDragging[index]) =
                            (_listOrderWhileDragging[index], _listOrderWhileDragging[newIndex]);
                    }
                }
            }

            if (ImGui.IsItemDeactivated())
            {
                completedDragging = true;
            }

            ImGui.SameLine(30 * T3Ui.UiScaleFactor);

            var f = list[index];
            var ff = f;
            
            var paddingForEditAndInsert = ImGui.GetFrameHeight() * 2;
            var additionalPadding = 10;
            var width = ImGui.GetContentRegionAvail().X - ImGui.GetCursorPosX() - paddingForEditAndInsert - additionalPadding;

            var size = new Vector2(width, 0);

            var r = InputEditStateFlags.Nothing;
            //var r = SingleValueEdit.Draw(ref ff, new Vector2(300 * T3Ui.UiScaleFactor,0));

            switch (ff)
            {
                case float floatValue:
                {
                    r = SingleValueEdit.Draw(ref floatValue, size);
                    if (r != InputEditStateFlags.Nothing)
                    {
                        ff = (TT)(object)floatValue;
                    }

                    break;
                }
                case int intValue:
                {
                    r = SingleValueEdit.Draw(ref intValue, size);
                    if (r != InputEditStateFlags.Nothing)
                        ff = (TT)(object)intValue;
                    break;
                }
                case Vector4 colorValue:
                {
                    var rightPadding = ImGui.GetFrameHeight() * 3;
                    r = Vector4InputUi.DrawColorInput(ref colorValue, false, rightPadding);
                    if (r != InputEditStateFlags.Nothing)
                        ff = (TT)(object)colorValue;
                    
                    break;
                }
                case string stringValue:
                {
                    
                    if (ImGui.InputText("##textEdit", ref stringValue, 1024))
                    {
                        r |= InputEditStateFlags.Modified;
                        ff = (TT)(object)stringValue;
                    }
                    break;
                }
            }

            ImGui.SameLine();
            var didChangeOrder = false;
            if (ImGui.Button("×"))
            {
                r |= InputEditStateFlags.ModifiedAndFinished;
                if (cloneIfModified)
                {
                    list = [..list];
                    cloneIfModified = false;
                    input.IsDefault = false;
                }

                list.RemoveAt(index);
                didChangeOrder = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("+"))
            {
                r |= InputEditStateFlags.ModifiedAndFinished;
                if (cloneIfModified)
                {
                    list = [..list];
                    cloneIfModified = false;
                    input.IsDefault = false;
                }

                list.Insert(index, ff);
                didChangeOrder = true;
            }

            if (r != InputEditStateFlags.Nothing)
            {
                if (!didChangeOrder)
                {
                    if (cloneIfModified)
                    {
                        list = [..list];
                        cloneIfModified = false;
                        input.IsDefault = false;
                    }
                    list[index] = ff;
                }

                modified |= r;
            }

            ImGui.PopID();
        }

        if (completedDragging)
        {
            _isDragging = false;
            _listOrderWhileDragging.Clear();
            modified |= InputEditStateFlags.Modified;
        }

        return modified;
    }

    
    
    private readonly List<int> _listOrderWhileDragging = [];
    private bool _isDragging;
}