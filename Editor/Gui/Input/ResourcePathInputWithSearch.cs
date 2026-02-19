using ImGuiNET;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows.AssetLib;
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
                              string fileExtensions,
                              ref string selectedValue,
                              bool pickFolder)
    {
        var inputId = ImGui.GetID("Input");

        var shouldUpdateScroll = false;
        var wasSelected = false;
        var upDownKeysPressed = false;
        
        
        var isActive = inputId == State.ActiveInputId;

        // Handle keyboard shortcuts before input field 
        if (isActive)
        {
            // If no search popup was active but not drawn, we should close.
            // This can happen if the component with the input wasn't rendered (because another item
            // was selected). In this case we should start from scratch.
            if (ImGui.GetFrameCount() - State.LastActiveFrame > 2)
            {
                State.Reset();
                return false;
            }

            State.LastActiveFrame = ImGui.GetFrameCount();

            if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorDown, true))
            {
                if (State.Matches.Count > 0)
                {
                    State.SelectedMatchIndex = (State.SelectedMatchIndex + 1).Clamp(0, State.Matches.Count - 1);
                    shouldUpdateScroll = true;
                    selectedValue = State.Matches[State.SelectedMatchIndex].Address;
                    wasSelected = true;
                    upDownKeysPressed = true;
                }
            }
            else if (ImGui.IsKeyPressed((ImGuiKey)Key.CursorUp, true))
            {
                if (State.Matches.Count > 0)
                {
                    State.SelectedMatchIndex--;
                    if (State.SelectedMatchIndex < 0)
                        State.SelectedMatchIndex = 0;
                    shouldUpdateScroll = true;
                    selectedValue = State.Matches[State.SelectedMatchIndex].Address;
                    wasSelected = true;
                    upDownKeysPressed = true;
                }
            }

            if (ImGui.IsKeyPressed((ImGuiKey)Key.Return, false))
            {
                if (State.SelectedMatchIndex >= 0 && State.SelectedMatchIndex < State.Matches.Count)
                {
                    selectedValue = State.Matches[State.SelectedMatchIndex].Address;
                    State.Reset();
                    return true;
                }

                if (!string.IsNullOrEmpty(State.SearchString))
                {
                    selectedValue = State.SearchString;
                    State.Reset();
                    return true;
                }
            }

            if (ImGui.IsKeyPressed((ImGuiKey)Key.Esc, false))
            {
                Log.Debug($"ESC revert {selectedValue} -> {State.ValueWhenOpened}");
                selectedValue = State.ValueWhenOpened;
                State.Reset();
                return true;
            }
        }

        // Draw input field...
        ImGui.PushStyleColor(ImGuiCol.Text, (hasWarning && string.IsNullOrEmpty(State.SearchString)) ? UiColors.StatusWarning.Rgba : UiColors.Text.Rgba);
        var inputString = selectedValue;
        if (isActive)
        {
            inputString = !string.IsNullOrEmpty(State.SearchString)
                              ? State.SearchString
                              : selectedValue;
        }

        inputString ??= string.Empty; // ImGui will crash if null is passed

        var filterInputChanged = ImGui.InputText("##input", ref inputString, 1024, ImGuiInputTextFlags.AutoSelectAll);
        if (filterInputChanged)
        {
            State.SearchString = inputString;
        }
        ImGui.PopStyleColor();
        
        // Handle activation
        var justOpened = ImGui.IsItemActivated();
        if (justOpened)
        {
            State.ValueWhenOpened = selectedValue;
            DrawUtils.DisableImGuiKeyboardNavigation();
            State.ActiveInputId = inputId;
            State.LastActiveFrame = ImGui.GetFrameCount();
        }

        // We defer exit to get clicks on opened popup list
        var inputFieldDeactivated = ImGui.IsItemDeactivated();
        var isPopupHovered = false;

        // Filter and draw matches
        if (ImGui.IsItemActive() || isActive)
        {
            State.ActiveInputId = inputId;
            var filterNeedsUpdate = (justOpened || filterInputChanged) && !upDownKeysPressed;
            if (filterNeedsUpdate)
            {
                FilterItems(fileExtensions, pickFolder);
                State.SelectedMatchIndex = -1;
                
                // Select active
                for (var index = 0; index < State.Matches.Count; index++)
                {
                    var a = State.Matches[index];
                    if (a.Address != selectedValue)
                        continue;

                    State.SelectedMatchIndex = index;
                    shouldUpdateScroll = true;
                    break;
                }

                if (State.SelectedMatchIndex == -1 && State.Matches.Count >0)
                {
                    State.SelectedMatchIndex = 0;
                    shouldUpdateScroll = true;
                }
            }

            shouldUpdateScroll |= justOpened;

            if (DrawMatches(State.SearchString,
                            isActive,
                            justOpened,
                            shouldUpdateScroll,
                            out var selectedAddress,
                            out var clickedOutside,
                            out isPopupHovered))
            {
                wasSelected = true;
                selectedValue = selectedAddress;
                State.Reset();
            }

            if (!justOpened && clickedOutside)
            {
                State.Reset();
                return false;
            }
        }

        // Ignore deactivations when interacting with popup
        if (inputFieldDeactivated && !isPopupHovered)
        {
            State.Reset();
        }

        return wasSelected;
    }

    private static bool DrawMatches(string searchString, bool isSearchResultWindowOpen, bool justOpened, bool shouldUpdateScroll,
                                    out string selectedPath,
                                    out bool clickedOutside,
                                    out bool isPopupHovered)
    {
        isPopupHovered = false;
        clickedOutside = false;
        selectedPath = string.Empty;
        var wasSelected = false;

        var lastPosition = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y);
        var size = new Vector2(ImGui.GetItemRectSize().X, 350 * T3Ui.UiScaleFactor);
        ImGui.SetNextWindowPos(lastPosition);
        ImGui.SetNextWindowSize(size);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar
                                       | ImGuiWindowFlags.NoMove
                                       | ImGuiWindowFlags.Tooltip // ugly as f**k. Sadly .PopUp will lead to random crashes.
                                       | ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.SetNextWindowSize(new Vector2(750, 300));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
        if (ImGui.Begin("##typeAheadSearchPopup", ref isSearchResultWindowOpen, flags))
        {
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, UiColors.BackgroundActive.Fade(0.3f).Rgba);

            var index = 0;
            var lastPackageId = Guid.Empty;

            if (State.Matches.Count == 0)
            {
                ImGui.TextUnformatted("No results found");
            }

            int separatorIndex = 0;

            foreach (var asset in State.Matches)
            {
                var isSelected = index == State.SelectedMatchIndex;

                if (State.SelectedMatchIndex == -1 && asset.Address == searchString)
                {
                    State.SelectedMatchIndex = index;
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
                                    && ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize()).Contains(ImGui.GetMousePos());

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

                // Nested tooltips don't work. So we use foreground drawlist to draw thumbnail
                if (isItemHovered)
                {
                    if (!string.IsNullOrEmpty(asset.Address) && asset.AssetType == AssetHandling.Images)
                    {
                        var package = ResourcePackageManager.SharedResourcePackages.FirstOrDefault(p => p.Id == asset.PackageId);
                        var slot= ThumbnailManager.GetThumbnail(asset, package);
                        if (slot.IsReady)
                        {
                            var dl = ImGui.GetForegroundDrawList();
                            var min = ImGui.GetMousePos() + new Vector2(16, 16);
                            var max = min + new Vector2(177, 133);
                            dl.AddImage(ThumbnailManager.AtlasSrv.NativePointer, min, max, slot.UvMin, slot.UvMax);
                        }
                    } 
                }
                
                ImGui.PopStyleColor();

                if (!justOpened && isItemHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    selectedPath = asset.Address;
                    wasSelected = true;
                }

                if (++index > MaxItemCount)
                    break;
            }

            isPopupHovered = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize())
                                   .Contains(ImGui.GetMousePos());

            clickedOutside = !isPopupHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            ImGui.PopStyleColor();
        }

        ImGui.End();
        ImGui.PopStyleColor();
        return wasSelected;
    }

    private static void FilterItems(string fileExtensions, bool pickFolder)
    {
        var requiredExtensionIds = FileExtensionRegistry.GetExtensionIdsFromExtensionSetString(fileExtensions);

        var allItems = AssetRegistry.AllAssets
                                    .Where(a => a.IsDirectory == pickFolder &&
                                                (requiredExtensionIds.Count == 0 || requiredExtensionIds.Contains(a.ExtensionId)))
                                    .Where(a => string.IsNullOrEmpty(State.SearchString) ||
                                                a.Address.Contains(State.SearchString, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(a => a.Address);

        State.Matches.Clear();
        State.Matches.AddRange(allItems);
    }

    private static class State
    {
        public static List<Asset> Matches = [];
        public static int SelectedMatchIndex;
        public static string SearchString = string.Empty;
        public static uint ActiveInputId;
        public static int LastActiveFrame;
        public static string ValueWhenOpened;

        public static void Reset()
        {
            Log.Debug("Reset");
            DrawUtils.RestoreImGuiKeyboardNavigation();
            Matches.Clear();
            SelectedMatchIndex = -1;
            SearchString = string.Empty;
            ActiveInputId = 0;
        }
    }

    private const int MaxItemCount = 500;
}