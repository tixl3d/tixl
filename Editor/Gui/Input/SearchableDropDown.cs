using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Input;

public static class SearchableDropDown
{

    [Flags]
    public enum ItemResults
    {
        FilteredOut,
        Visible,
        Activated,
        Completed,
    }

    private static bool _justOpened = false;
    private static bool _scrollNeedsUpdate;
    
    public static bool Draw(ref int selectedIndex, string currentValue,  Func<string, bool, ItemResults> filterAndDrawItem)
    {
        var inputId = ImGui.GetID("");
        var isSearchResultWindowOpen = inputId == _activeInputId;

        if (!isSearchResultWindowOpen)
        {
            _searchString = currentValue;
            if (ImGui.Button(currentValue))
            {
                _activeInputId = inputId;
                _searchString = string.Empty;
                
                _activeInputId = inputId;
                _filtedCount = 0;
                _scrollNeedsUpdate = false;
                _selectedFilteredResultIndex = -1;

                _justOpened = true;
                DrawUtils.DisableImGuiKeyboardNavigation();
            }
            return false;
        }

        if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
        {
            if (_filtedCount > 0)
            {
                _selectedFilteredResultIndex++;
                _selectedFilteredResultIndex %= _filtedCount;
                _scrollNeedsUpdate = true;
            }
        }
        else if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorUp, true))
        {
            if (_filtedCount > 0)
            {
                _selectedFilteredResultIndex--;
                if (_selectedFilteredResultIndex < 0)
                    _selectedFilteredResultIndex = _filtedCount - 1;
                
                _scrollNeedsUpdate = true;
            }
        }

        if (_justOpened || ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            ImGui.SetKeyboardFocusHere();
        }

        var wasChanged = false;
        var filterChanged = ImGui.InputText("##search", ref _searchString, 256);
        _scrollNeedsUpdate |= filterChanged;

        if (ImGui.IsItemActivated())
        {
            _justOpened = false;
        }

        // We defer exit to get clicks on opened popup list
        var lostFocus =  ImGui.IsKeyDown((ImGuiKey)Key.Esc);
        
        ImGui.SetNextWindowPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));
        ImGui.SetNextWindowSize(new Vector2(ImGui.GetItemRectSize().X, 320));

        if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen,
                        ImGuiWindowFlags.NoTitleBar
                        | ImGuiWindowFlags.NoMove
                        | ImGuiWindowFlags.Popup
                        | ImGuiWindowFlags.ChildWindow
                       ))
        {
            //_lastTypeAheadResults.Clear();
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Gray.Rgba);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RectOnly))
            {
                _activeInputId = 0;
                ImGui.CloseCurrentPopup();
            }

            var visibleIndex = 0;
            var processedIndex = -1;
            while (true)
            {
                processedIndex++;

                if (_selectedFilteredResultIndex == -1 && processedIndex == selectedIndex)
                {
                    _selectedFilteredResultIndex = visibleIndex;
                }
                
                var isCurrentIndex = _selectedFilteredResultIndex == visibleIndex;
                if (isCurrentIndex)
                    selectedIndex = processedIndex;

                ImGui.PushID(processedIndex);
                var result = filterAndDrawItem(_searchString, isCurrentIndex);
                ImGui.PopID();
                
                if (isCurrentIndex)
                {
                    if (_scrollNeedsUpdate)
                    {
                        UiListHelpers.ScrollToMakeItemVisible();
                        _scrollNeedsUpdate = false;
                    }
                }
                
                if (result == ItemResults.Completed)
                    break;

                if (result == ItemResults.FilteredOut)
                    continue;

                if (result is ItemResults.Visible or ItemResults.Activated)
                    visibleIndex++;

                if (result == ItemResults.Activated || isCurrentIndex && ImGui.IsKeyPressed((ImGuiKey)Key.Return))
                {
                    wasChanged = true;
                    selectedIndex = processedIndex;
                    _activeInputId = 0;
                    break;
                }
            }

            _filtedCount = visibleIndex;
            ImGui.PopStyleColor();
        }

        ImGui.End();

        if (lostFocus)
        {
            _activeInputId = 0;
            DrawUtils.RestoreImGuiKeyboardNavigation();
        }

        return wasChanged;
    }
    
    // public static bool Draw<T>(ref T selectedItem,  IEnumerable<T> items, Func<T, bool, string,  ItemResults> filterAndDrawItem) where T: IEquatable<T>
    // {
    //     var inputId = ImGui.GetID(string.Empty);
    //     var isSearchResultWindowOpen = inputId == _activeInputId;
    //         
    //     if (isSearchResultWindowOpen)
    //     {
    //         if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
    //         {
    //             if (_filtedCount > 0)
    //             {
    //                 _selectedResultIndex++;
    //                 _selectedResultIndex %= _filtedCount;
    //             }
    //         }
    //         else if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorUp, true))
    //         {
    //             if (_filtedCount > 0)
    //             {
    //                 _selectedResultIndex--;
    //                 if (_selectedResultIndex < 0)
    //                     _selectedResultIndex = _filtedCount - 1;
    //             }
    //         }
    //     }
    //     else
    //     {
    //         _searchString = string.Empty;
    //     }
    //         
    //     ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
    //     var wasChanged = ImGui.InputText("##search", ref _searchString, 256);
    //     ImGui.PopStyleColor();
    //
    //     if (ImGui.IsItemActivated())
    //     {
    //         _filtedCount = 0;
    //         //_lastTypeAheadResults.Clear();
    //         _selectedResultIndex = -1;
    //         THelpers.DisableImGuiKeyboardNavigation();
    //     }
    //
    //     var isItemDeactivated = ImGui.IsItemDeactivated();
    //         
    //     // We defer exit to get clicks on opened popup list
    //     var lostFocus = isItemDeactivated || ImGui.IsKeyDown((ImGuiKey)Key.Esc);
    //         
    //     if ( ImGui.IsItemActive() || isSearchResultWindowOpen)
    //     {
    //         _activeInputId = inputId;
    //
    //         ImGui.SetNextWindowPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));
    //         ImGui.SetNextWindowSize(new Vector2(ImGui.GetItemRectSize().X, 320));
    //             
    //         ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(7, 7));
    //         if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen,
    //                         ImGuiWindowFlags.NoTitleBar 
    //                         | ImGuiWindowFlags.NoMove 
    //                         | ImGuiWindowFlags.Popup 
    //                         | ImGuiWindowFlags.ChildWindow
    //                        ))
    //         {
    //             //_lastTypeAheadResults.Clear();
    //             var index = 0;
    //             ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Gray.Rgba);
    //
    //             if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RectOnly))
    //             {
    //                 _activeInputId = 0;
    //                 ImGui.CloseCurrentPopup();
    //             }
    //
    //             foreach (var item in items)
    //             {
    //                 var isCurrent = item.Equals(selectedItem) || _selectedResultIndex == index;
    //                 switch (filterAndDrawItem(item, isCurrent, _searchString))
    //                 {
    //                     case ItemResults.FilteredOut:
    //                         break;
    //                         
    //                     case ItemResults.Visible:
    //                         index++;
    //                         break;
    //                         
    //                     case ItemResults.Activated:
    //                         //filter = item;
    //                         wasChanged = true;
    //                         _activeInputId = 0;
    //                         break;
    //                 }
    //                 // {
    //                 //     
    //                 // }
    //                 // if (item == null ||  !item.Contains(text, StringComparison.InvariantCultureIgnoreCase))
    //                 //     continue;
    //                 //
    //                 // var isSelected = index == _selectedResultIndex;
    //                 // ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
    //                 // ImGui.Selectable(item, isSelected);
    //                 // ImGui.PopStyleColor();
    //                 //     
    //                 // if (ImGui.IsItemClicked() || (isSelected && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
    //                 // {
    //                 //     text = item;
    //                 //     wasChanged = true;
    //                 //     _activeInputId = 0;
    //                 // }
    //                     
    //                 //_lastTypeAheadResults.Add(item);
    //                 if (++index > 100)
    //                     break;
    //             }
    //
    //             _filtedCount = index;
    //             ImGui.PopStyleColor();
    //         }
    //
    //         ImGui.End();
    //         ImGui.PopStyleVar();
    //     }
    //
    //     if (lostFocus)
    //     {
    //         THelpers.RestoreImGuiKeyboardNavigation();
    //     }
    //
    //     return wasChanged;
    // }
    
    private static string _searchString= string.Empty;

    private static int _selectedFilteredResultIndex;
    private static uint _activeInputId;
    private static int _filtedCount;
}