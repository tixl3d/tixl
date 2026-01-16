#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Input;

/// <summary>
/// Draws a type ahead input 
/// </summary>
/// <remarks>
/// Sadly, the implementation of this component is a single horrible hack.
/// It's probably the single most ugly piece of ImGui code in the whole codebase.
/// also see:
/// https://github.com/ocornut/imgui/issues/718
/// https://github.com/ocornut/imgui/issues/3725
///
/// It should work for now, but it's likely to break with future versions of ImGui.
/// </remarks>
internal static class InputWithTypeAheadSearch
{
    public static bool Draw(string label, IEnumerable<string> items, bool warning , ref string searchString, out string selected, out bool isInList, bool outlineOnly=false)
    {
        var inputId = ImGui.GetID(label); 
        var isSearchResultWindowOpen = inputId == _activeInputId;
        var shouldUpdateScroll = false;
        var  wasSelected= false;
        selected = searchString;

        if (outlineOnly)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Color.Red.Rgba);
        }
            
        var color = warning ? UiColors.StatusWarning.Rgba : UiColors.Text.Rgba;
        ImGui.PushStyleColor(ImGuiCol.Text, color);
            
        searchString ??= string.Empty;  // ImGui will crash if null is passed
        var filterInputChanged = ImGui.InputText(label, ref searchString, 256, ImGuiInputTextFlags.AutoSelectAll);
        
        if (isSearchResultWindowOpen)
        {
            //Log.Debug("SearchString: " + searchString);
            if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
            {
                if (_lastTypeAheadResults.Count > 0)
                {
                    _selectedResultIndex = (_selectedResultIndex + 1).Clamp(0, _lastTypeAheadResults.Count-1);
                    shouldUpdateScroll = true;
                    selected = searchString;
                    wasSelected = true;
                }
            }
            else if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorUp, true))
            {
                if (_lastTypeAheadResults.Count > 0)
                {
                    _selectedResultIndex--;
                    if (_selectedResultIndex < 0)
                        _selectedResultIndex = 0;
                    shouldUpdateScroll = true;
                    selected = searchString;
                    wasSelected = true;
                }
            }
            
            if (ImGui.IsKeyPressed((ImGuiKey)Key.Return, false))
            {
                if (_selectedResultIndex >= 0 && _selectedResultIndex < _lastTypeAheadResults.Count)
                {
                    searchString = _lastTypeAheadResults[_selectedResultIndex];
                    selected = searchString;
                    _activeInputId = 0;
                    isInList = true;
                    return true;
                }
                else
                {
                    selected = searchString;
                    _activeInputId = 0;
                    isInList = false;
                    return true;
                }
            }
            if (ImGui.IsKeyPressed((ImGuiKey)Key.Esc, false))
            {
                _activeInputId = 0;
                selected = searchString;
                isInList = false;
                return false;
            }
        }

        
        //var filterInputChanged = true;
        
        // Sadly, ImGui will revert the searchSearch to its internal state if cursor is moved up or down.
        // To apply is as a new result we need to revert that...
        if (wasSelected)
        {
            searchString = selected;
        }
        
        ImGui.PopStyleColor();
            
            
        if (outlineOnly)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.BackgroundInputField, 5);
            ImGui.PopStyleColor(2);
        }
        var justOpened = ImGui.IsItemActivated();

        if (justOpened)
        {
            
            _lastTypeAheadResults.Clear();
            _selectedResultIndex = -1;
            DrawUtils.DisableImGuiKeyboardNavigation();
        }

        var isItemDeactivated = ImGui.IsItemDeactivated();

        // We defer exit to get clicks on opened popup list
        var lostFocus = isItemDeactivated || ImGui.IsKeyDown((ImGuiKey)Key.Esc);
        selected = string.Empty;
            
        if ( ImGui.IsItemActive() || isSearchResultWindowOpen)
        {
            _activeInputId = inputId;

            var lastPosition = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y);
            var size = new Vector2(ImGui.GetItemRectSize().X, 320);
            ImGui.SetNextWindowPos(lastPosition);
            ImGui.SetNextWindowSize(size);
            if (ImGui.IsItemFocused() && ImGui.IsKeyPressed((ImGuiKey)Key.Return))
            {
                wasSelected = true;
                _activeInputId = 0;
            }
                
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar
                                           | ImGuiWindowFlags.NoMove
                                           | ImGuiWindowFlags.Tooltip // ugly as f**k. Sadly .PopUp will lead to random crashes.
                                           | ImGuiWindowFlags.NoFocusOnAppearing;
                
            ImGui.SetNextWindowSize(new Vector2(450,300));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
            if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen,flags))
            {
                //_lastTypeAheadResults.Clear();
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Gray.Rgba);
                
                if(justOpened || filterInputChanged)
                     FilterItems(items, searchString, ref _lastTypeAheadResults);
                
                var index = 0;
                //var lastProjectGroup = string.Empty;
                
                if(_lastTypeAheadResults.Count == 0)
                {
                    ImGui.TextUnformatted("No results found");
                }
                
                foreach (var item in _lastTypeAheadResults)
                {
                    var isSelected = index == _selectedResultIndex;
                    if ( _selectedResultIndex == -1 && item == searchString)
                    {
                        _selectedResultIndex = index;
                        isSelected = true;
                        shouldUpdateScroll = true;
                    }
                    
                    if(isSelected && shouldUpdateScroll)
                    {
                        ImGui.SetScrollHereY();
                    }

                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                    
                    ImGui.Selectable( item , isSelected, ImGuiSelectableFlags.None);
                    var isItemHovered = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()).Contains( ImGui.GetMousePos());
                    isSelected = item == searchString;
                        
                    ImGui.PopStyleColor();
                            
                    if (!justOpened && 
                        ( ImGui.IsMouseClicked(ImGuiMouseButton.Left) && isItemHovered 
                        || isSelected && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
                    {
                        searchString = item;
                        wasSelected = true;
                        _activeInputId = 0;
                        selected = item;
                    }
                    
                    if (++index > 100)
                        break;
                }
                
                var isPopupHovered = ImRect.RectWithSize(ImGui.GetWindowPos( ), ImGui.GetWindowSize())
                                           .Contains(ImGui.GetMousePos());

                if (!isPopupHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _activeInputId = 0;
                }

                ImGui.PopStyleColor();
            }

            ImGui.End();
            ImGui.PopStyleColor();
        }

        if (lostFocus)
        {
            DrawUtils.RestoreImGuiKeyboardNavigation();
        }

        isInList = true;
        return wasSelected;
    }
    
    private static void FilterItems(IEnumerable<string?> allItems, string filter, ref List<string> filteredItems)
    {
        filteredItems.Clear();
        
        List<string> allValidItems = allItems.Where(i => i != null).ToList()!;
        if (string.IsNullOrWhiteSpace(filter))
        {
            filteredItems.AddRange(allValidItems);
            return;
        }

        var matches = new List<ResultWithRelevancy>();

        foreach (var word in allValidItems)
        {
            if (word.StartsWith(filter, StringComparison.InvariantCulture))
            {
                matches.Add(new ResultWithRelevancy(word, 1));
            }
            else if (word.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
            {
                matches.Add(new ResultWithRelevancy(word, 2));
            }
        }

        switch (matches.Count)
        {
            case 0:
                return;
            case 1 when matches[0].Word == filter:
                filteredItems.AddRange(allValidItems);
                return;
            default:
                filteredItems.AddRange( matches.OrderBy(r => r.Relevancy)
                                               .Select(m => m.Word)
                                               .ToList());
                break;
        }
    }

    private sealed record ResultWithRelevancy(string Word, float Relevancy);

    private static List<string> _lastTypeAheadResults = [];
    private static int _selectedResultIndex;
    private static uint _activeInputId;
}