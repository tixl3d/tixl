using System.Text.RegularExpressions;
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
internal static partial class ResourceInputWithTypeAheadSearch
{
    //public readonly record struct Args(string Label, IEnumerable<string> Items, bool Warning);

    internal static bool Draw(string label, IEnumerable<string> items, bool hasWarning, ref string searchString, out string selected, bool outlineOnly=false)
    {
        var inputId = ImGui.GetID(label); 
        var isSearchResultWindowOpen = inputId == _activeInputId;
        var shouldUpdateScroll = false;
        var  wasSelected= false;
        selected = searchString;


        if (isSearchResultWindowOpen)
        {
            if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
            {
                if (_lastTypeAheadResults.Count > 0)
                {
                    _selectedResultIndex = (_selectedResultIndex + 1).Clamp(0, _lastTypeAheadResults.Count-1);
                    shouldUpdateScroll = true;
                    searchString = _lastTypeAheadResults[_selectedResultIndex];
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
                    searchString = _lastTypeAheadResults[_selectedResultIndex];
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
                    return true;
                }
            }
            if (ImGui.IsKeyPressed((ImGuiKey)Key.Esc, false))
            {
                _activeInputId = 0;
                selected = searchString;
                return false;
            }
            
        }

        if (outlineOnly)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Color.Red.Rgba);
        }
            
        var color = hasWarning ? UiColors.StatusWarning.Rgba : UiColors.Text.Rgba;
        ImGui.PushStyleColor(ImGuiCol.Text, color);
            
        searchString ??= string.Empty;  // ImGui will crash if null is passed
        
        var filterInputChanged = ImGui.InputText(label, ref searchString, 256, ImGuiInputTextFlags.AutoSelectAll);
        
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
        selected = default;
            
        if ( ImGui.IsItemActive() || isSearchResultWindowOpen)
        {
            _activeInputId = inputId;

            var lastPosition = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y);
            var size = new Vector2(ImGui.GetItemRectSize().X , 350 * T3Ui.UiScaleFactor);
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
                
            ImGui.SetNextWindowSize(new Vector2(750,300));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
            if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen,flags))
            {
                //_lastTypeAheadResults.Clear();
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Gray.Rgba);
                
                if(justOpened || filterInputChanged)
                     FilterItems(items, searchString, ref _lastTypeAheadResults);
                
                var index = 0;
                var lastProjectGroup = string.Empty;
                
                if(_lastTypeAheadResults.Count == 0)
                {
                    ImGui.TextUnformatted("No results found");
                }
                
                foreach (var path in _lastTypeAheadResults)
                {
                    var isSelected = index == _selectedResultIndex;
                    if ( _selectedResultIndex == -1 && path == searchString)
                    {
                        _selectedResultIndex = index;
                        isSelected = true;
                        shouldUpdateScroll = true;
                    }
                    
                    if(isSelected && shouldUpdateScroll)
                    {
                        ImGui.SetScrollHereY();
                    }

                    // We can't use IsItemHovered because we need to use Tooltip hack 
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);

                    var match = FindProjectAndPathRegex().Match(path);
                    var project = match.Success ? match.Groups[1].Value : string.Empty;
                    var pathInProject = match.Success ? match.Groups[2].Value : path;
                    var filename = match.Success ? match.Groups[3].Value : path;
                    
                    if (project != lastProjectGroup)
                    {
                        if (lastProjectGroup != string.Empty)
                            FormInputs.AddVerticalSpace(8);
                        
                        ImGui.PushFont(Fonts.FontSmall);
                        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                        ImGui.TextUnformatted( $"/{project}/");
                        ImGui.PopStyleColor();
                        ImGui.PopFont();
                        lastProjectGroup = project;
                    }
                    
                    var lastPos = ImGui.GetCursorPos();
                    ImGui.Selectable( $"##{path}" , isSelected, ImGuiSelectableFlags.None);
                    var isItemHovered = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()).Contains( ImGui.GetMousePos());
                    var keepNextPos = ImGui.GetCursorPos();
                    
                    isSelected = path == searchString;
                    ImGui.PushFont(isSelected ? Fonts.FontBold : Fonts.FontNormal);

                    ImGui.SetCursorPos(lastPos);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(pathInProject + "/");
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    ImGui.TextUnformatted(filename);
                    ImGui.PopFont();
                    
                    ImGui.SetCursorPos(keepNextPos);

                    // Tooltips inside other tooltips are not working 
                    // if (isItemHovered && !string.IsNullOrEmpty(path))
                    // {
                    //     ImGui.BeginTooltip();
                    //     ImGui.TextUnformatted(path);
                    //     ImGui.EndTooltip();
                    // }
                        
                    ImGui.PopStyleColor();
                            
                    if (!justOpened && 
                        ( ImGui.IsMouseClicked(ImGuiMouseButton.Left) && isItemHovered 
                        || isSelected && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
                    {
                        searchString = path;
                        wasSelected = true;
                        _activeInputId = 0;
                        selected = path;
                    }

                    //_lastTypeAheadResults.Add(path);
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

        return wasSelected;
    }

    private static void FilterItems(IEnumerable<string> allItems, string filter, ref List<string> filteredItems)
    {
        //var listItems = new List<string>();
        filteredItems.Clear();
        
        var allValidItems = allItems.Where(i => i != null).ToList();
        if (string.IsNullOrWhiteSpace(filter))
        {
            filteredItems.AddRange(allValidItems);
            return;
        }

        var matches = new List<ResultWithRelevancy>();
        // var others = new List<string>();

        foreach (var word in allValidItems)
        {
            if (word == null)
                continue;

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

    // private struct ResultWithRelevancy
    // {
    //     public ResultWithRelevancy(string word, float relevancy)
    //     {
    //         Word = word;
    //         Relevancy = relevancy;
    //     }
    //
    //     public string Word;
    //     public float Relevancy;
    // }

    private static List<string> _lastTypeAheadResults = [];
    private static int _selectedResultIndex;
    private static uint _activeInputId;

    [GeneratedRegex(@"^\/(.+?)\/(.*?)\/([^\/]*)$")]
    private static partial Regex FindProjectAndPathRegex();
}