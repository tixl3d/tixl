using ImGuiNET;
using T3.Core.Resource.Assets;
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
internal static class AssetInputWithTypeAheadSearch
{
    internal static bool Draw(bool hasWarning,
                              string fileFilter,
                              ref string searchString,
                              bool pickFolder)
    {
        searchString ??= string.Empty; // ImGui will crash if null is passed

        var inputId = ImGui.GetID("Input");

        var shouldUpdateScroll = false;
        var wasSelected = false;

        var isSearchResultWindowOpen = inputId == _activeInputId;
        if (isSearchResultWindowOpen)
        {
            if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
            {
                if (_lastTypeAheadResults.Count > 0)
                {
                    _selectedResultIndex = (_selectedResultIndex + 1).Clamp(0, _lastTypeAheadResults.Count - 1);
                    shouldUpdateScroll = true;
                    searchString = _lastTypeAheadResults[_selectedResultIndex].Address;
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
                    searchString = _lastTypeAheadResults[_selectedResultIndex].Address;
                    wasSelected = true;
                }
            }

            if (ImGui.IsKeyPressed((ImGuiKey)Key.Return, false))
            {
                if (_selectedResultIndex >= 0 && _selectedResultIndex < _lastTypeAheadResults.Count)
                {
                    searchString = _lastTypeAheadResults[_selectedResultIndex].Address;
                    _activeInputId = 0;
                    return true;
                }
            }

            if (ImGui.IsKeyPressed((ImGuiKey)Key.Esc, false))
            {
                _activeInputId = 0;
                return false;
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, hasWarning ? UiColors.StatusWarning.Rgba : UiColors.Text.Rgba);

        var filterInputChanged = ImGui.InputText("##input", ref searchString, 256, ImGuiInputTextFlags.AutoSelectAll);

        // Sadly, ImGui will revert the searchSearch to its internal state if cursor is moved up or down.
        // To apply is as a new result we need to revert that...
        if (wasSelected)
        {
            //searchString = selected; // TODO: Test this.
        }

        ImGui.PopStyleColor();

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

        if (ImGui.IsItemActive() || isSearchResultWindowOpen)
        {
            var filterNeedsUpdate = justOpened || filterInputChanged;
            if (filterNeedsUpdate)
                FilterItems(fileFilter, searchString, pickFolder, ref _lastTypeAheadResults);

            _activeInputId = inputId;
            wasSelected = DrawResultsList(ref searchString, wasSelected, isSearchResultWindowOpen, justOpened, shouldUpdateScroll);
        }

        if (lostFocus)
        {
            DrawUtils.RestoreImGuiKeyboardNavigation();
        }

        return wasSelected;
    }

    private static bool DrawResultsList(ref string searchString,
                                        bool wasSelected, bool isSearchResultWindowOpen, bool justOpened, bool shouldUpdateScroll)
    {
        var lastPosition = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y);
        var size = new Vector2(ImGui.GetItemRectSize().X, 350 * T3Ui.UiScaleFactor);
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

        ImGui.SetNextWindowSize(new Vector2(750, 300));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
        if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen, flags))
        {
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Gray.Rgba);

            var index = 0;
            var lastPackageId = Guid.Empty;

            if (_lastTypeAheadResults.Count == 0)
            {
                ImGui.TextUnformatted("No results found");
            }

            int separatorIndex = 0;

            foreach (var asset in _lastTypeAheadResults)
            {
                var isSelected = index == _selectedResultIndex;
                if (_selectedResultIndex == -1 && asset.Address == searchString)
                {
                    _selectedResultIndex = index;
                    isSelected = true;
                    shouldUpdateScroll = true;
                }

                if (isSelected && shouldUpdateScroll)
                {
                    ImGui.SetScrollHereY();
                }

                // We can't use IsItemHovered because we need to use Tooltip hack 
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);

                var address = asset.Address.AsSpan();

                if (lastPackageId != asset.PackageId)
                {
                    separatorIndex = asset.Address.IndexOf(AssetRegistry.PackageSeparator);
                    var packageName = separatorIndex != -1
                                          ? address[..(separatorIndex + 1)]
                                          : "?";

                    // Add padding except for first
                    if (lastPackageId != Guid.Empty)
                        FormInputs.AddVerticalSpace(8);

                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(packageName);
                    CustomComponents.DrawSearchMatchUnderline(searchString, packageName, ImGui.GetItemRectMin());
                    ImGui.PopStyleColor();
                    ImGui.PopFont();
                    
                    lastPackageId = asset.PackageId;
                }

                var lastPos = ImGui.GetCursorPos();
                ImGui.Selectable($"##{asset}", isSelected, ImGuiSelectableFlags.None);
                
                var lastMin = ImGui.GetItemRectMin();
                
                var isItemHovered = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()).Contains(ImGui.GetMousePos())
                    && ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize() ).Contains(ImGui.GetMousePos());
                
                var keepNextPos = ImGui.GetCursorPos();

                isSelected = asset.Address == searchString;
                ImGui.PushFont(isSelected ? Fonts.FontBold : Fonts.FontNormal);

                var localPath = address[(separatorIndex + 1)..];
                var lastSlash = localPath.LastIndexOf('/');

                ImGui.SetCursorPos(lastPos);
                
                var hasPath = lastSlash != -1;
                if (hasPath)
                {
                    var pathInProject = localPath[..(lastSlash + 1)];
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(pathInProject);
                    ImGui.PopStyleColor();
    
                    ImGui.SameLine(0, 0); // Use 0 spacing to keep text glued together
                    ImGui.TextUnformatted(localPath[(lastSlash + 1)..]);
                }
                else
                {
                    // No slash? Just draw the whole thing normally
                    ImGui.TextUnformatted(localPath);
                }
                
                CustomComponents.DrawSearchMatchUnderline(searchString, localPath, lastMin);

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
                    (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && isItemHovered
                     || isSelected && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
                {
                    searchString = asset.Address;
                    wasSelected = true;
                    _activeInputId = 0;
                }

                if (++index > MaxItemCount)
                    break;
            }

            var isPopupHovered = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize())
                                       .Contains(ImGui.GetMousePos());

            if (!isPopupHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _activeInputId = 0;
            }

            ImGui.PopStyleColor();
        }

        ImGui.End();
        ImGui.PopStyleColor();
        return wasSelected;
    }

    private static void FilterItems(string fileFilter, string searchFilter, bool pickFolder, ref List<Asset> filteredItems)
    {
        var requiredExtensionIds = FileExtensionRegistry.GetExtensionIdsFromExtensionSetString(fileFilter);

        var allItems = AssetRegistry.AllAssets
                                    .Where(a => a.IsDirectory == pickFolder &&
                                                (requiredExtensionIds.Count == 0 || requiredExtensionIds.Contains(a.ExtensionId)))
                                    .OrderBy(a => a.Address)
                                    .ToList();

        var matches = new List<ResultWithRelevancy>();
        foreach (var asset in allItems)
        {
            if (asset.Address.StartsWith(searchFilter, StringComparison.InvariantCulture))
            {
                matches.Add(new ResultWithRelevancy(asset, 1));
            }
            else if (asset.Address.Contains(searchFilter, StringComparison.InvariantCultureIgnoreCase))
            {
                matches.Add(new ResultWithRelevancy(asset, 2));
            }
        }

        filteredItems.Clear();
        switch (matches.Count)
        {
            case 0:
                return;

            case 1 when matches[0].Asset.Address == searchFilter:
                filteredItems.AddRange(allItems);
                return;

            default:
                filteredItems.AddRange(matches
                                      .OrderBy(r => r.Relevancy)
                                      .Select(r => r.Asset));
                break;
        }
    }

    private sealed record ResultWithRelevancy(Asset Asset, float Relevancy);

    private static List<Asset> _lastTypeAheadResults = [];
    private static int _selectedResultIndex;
    private static uint _activeInputId;

    private const int MaxItemCount = 500;
}