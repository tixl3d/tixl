using ImGuiNET;
using T3.Core.Model;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;

// ReSharper disable AccessToDisposedClosure

namespace T3.Editor.Gui.Graph.Dialogs;

internal static class TypeSelector
{
    internal static void Draw(ref Type selectedType)
    {
        if (!_initialized)
        {
            _typeList = TypeNameRegistry.Entries.ToArray();
            _initialized = true;
        }

        if (selectedType != SelectedType)
        {
            for (var index = 0; index < _typeList.Length; index++)
            {
                if (_typeList[index].Key == selectedType)
                {
                    _selectedTypeIndex = index;
                    break;
                }
            }
        }
        
        using var enumerator = TypeNameRegistry.Entries.GetEnumerator();

        var typeChanged = SearchableDropDown.Draw(ref _selectedTypeIndex,
                                                  SelectedTypeName,
                                                  FilterAndDrawItem);

        if (typeChanged)
        {
            selectedType = SelectedType;
        }

        return;

        SearchableDropDown.ItemResults FilterAndDrawItem(string filter, bool isSelected)
        {
            if (!enumerator.MoveNext()) return SearchableDropDown.ItemResults.Completed;

            var (type, typeName) = enumerator.Current;
            var isMatch = false;
            isMatch |= typeName.Contains(filter, StringComparison.InvariantCultureIgnoreCase);
            if (_synonyms.TryGetValue(typeName, out var synonymsForType))
            {
                foreach (var s in synonymsForType)
                {
                    isMatch |= s.Contains(filter, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            if (!isMatch) return SearchableDropDown.ItemResults.FilteredOut;

            if (!TypeUiRegistry.TryGetPropertiesForType(type, out var properties))
            {
                properties = UiProperties.Default;
            }

            var typeColor = ColorVariations.OperatorLabel.Apply(properties.Color).Rgba;

            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, UiColors.BackgroundActive.Fade(0.3f).Rgba);
            var rowStart = ImGui.GetCursorPos();
            ImGui.Selectable("##" + typeName, isSelected, ImGuiSelectableFlags.None);
            var rowMin = ImGui.GetItemRectMin();
            var rowMax = ImGui.GetItemRectMax();
            var nextRow = ImGui.GetCursorPos();

            // The dropdown lives inside a modal; ImGui blocks hover/click within
            // the modal rect for non-descendant windows, so Selectable's natural
            // hover and IsItemHovered() return false for items overlapping the
            // modal. Hit-test manually so both click and hover visuals work for
            // every row regardless of where the popup falls.
            var mousePos = ImGui.GetMousePos();
            var isMouseOverRow = mousePos.X >= rowMin.X && mousePos.X <= rowMax.X && mousePos.Y >= rowMin.Y && mousePos.Y <= rowMax.Y;
            var clicked = isMouseOverRow && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

            if (isMouseOverRow && !isSelected && !ImGui.IsItemHovered()) 
                ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, UiColors.BackgroundActive.Fade(0.3f));

            ImGui.SetCursorPos(rowStart + new Vector2(4, 0));
            
            ImGui.PushFont(isSelected ? Fonts.FontBold : Fonts.FontNormal);
            
            ImGui.PushStyleColor(ImGuiCol.Text, typeColor);
            ImGui.TextUnformatted(typeName);
            ImGui.PopStyleColor();
            
            
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Fade(0.4f).Rgba);
            ImGui.TextUnformatted(" - " + type.Namespace);
            ImGui.PopStyleColor();
            CustomComponents.DrawSearchMatchUnderline(filter, typeName, rowMin + new Vector2(4, 2));
            ImGui.PopFont();

            ImGui.SetCursorPos(nextRow);
            ImGui.PopStyleColor();

            return clicked
                ? SearchableDropDown.ItemResults.Activated
                : SearchableDropDown.ItemResults.Visible;
        }
    }
    
    private static int _selectedTypeIndex;
    private static readonly Dictionary<string, string[]> _synonyms = new()
                                                                         {
                                                                             { "float", new[] { "Single", } },
                                                                             { "Texture2d", new[] { "Image", } },
                                                                             { "Vector4", new[] { "Color", "Quaternion" } },
                                                                         };
    private static Type SelectedType => IsSelectedTypeIndexValid ? _typeList[_selectedTypeIndex].Key : null;
    private static string SelectedTypeName => IsSelectedTypeIndexValid ? _typeList[_selectedTypeIndex].Value : "Unknown";
    private static bool IsSelectedTypeIndexValid => _selectedTypeIndex >= 0 && _selectedTypeIndex < _typeList.Length;

    private static KeyValuePair<Type, string>[] _typeList;
    private static bool _initialized;

}