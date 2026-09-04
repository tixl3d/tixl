using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.App;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Input;

/// <summary>
/// A searchable dropdown driven by an <see cref="ImGui.InputText"/> with a tooltip-style
/// popup below. Mirrors the structure of <see cref="AssetInputWithTypeAheadSearch"/> so the
/// popup escapes any parent clip rect (e.g. modal dialogs); per-item drawing is delegated
/// to the caller's <c>filterAndDrawItem</c> callback so callers can colour items as needed.
/// </summary>
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

    public static bool Draw(ref int selectedIndex, string currentValue, Func<string, bool, ItemResults> filterAndDrawItem)
    {
        var inputId = ImGui.GetID("Input");
        var isActive = inputId == _activeInputId;
        var wasChanged = false;
        var shouldUpdateScroll = false;
        var upDownKeysPressed = false;

        // Handle keyboard navigation before InputText so the field doesn't swallow the keys.
        if (isActive)
        {
            // Auto-close if we weren't drawn for a couple of frames (parent UI changed, etc.)
            if (ImGui.GetFrameCount() - _lastActiveFrame > 2)
            {
                Reset();
                return false;
            }
            _lastActiveFrame = ImGui.GetFrameCount();

            if (ImGui.IsKeyPressed(Key.CursorDown.ToImGuiKey(), true))
            {
                if (_filteredCount > 0)
                {
                    _selectedFilteredResultIndex = (_selectedFilteredResultIndex + 1).Clamp(0, _filteredCount - 1);
                    shouldUpdateScroll = true;
                    upDownKeysPressed = true;
                }
            }
            else if (ImGui.IsKeyPressed(Key.CursorUp.ToImGuiKey(), true))
            {
                if (_filteredCount > 0)
                {
                    _selectedFilteredResultIndex--;
                    if (_selectedFilteredResultIndex < 0)
                        _selectedFilteredResultIndex = 0;
                    shouldUpdateScroll = true;
                    upDownKeysPressed = true;
                }
            }

            if (ImGui.IsKeyPressed(Key.Esc.ToImGuiKey(), false))
            {
                Reset();
                return false;
            }
        }

        // Show the search string while active, otherwise the current selection.
        var inputString = isActive && !string.IsNullOrEmpty(_searchString)
                              ? _searchString
                              : currentValue ?? string.Empty;

        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        var filterChanged = ImGui.InputText("##search", ref inputString, 256, ImGuiInputTextFlags.AutoSelectAll);
        ImGui.PopStyleVar();
        
        if (filterChanged)
            _searchString = inputString;

        var justOpened = ImGui.IsItemActivated();
        if (justOpened)
        {
            _activeInputId = inputId;
            _searchString = string.Empty;
            _selectedFilteredResultIndex = -1;
            _filteredCount = 0;
            _lastActiveFrame = ImGui.GetFrameCount();
            DrawUtils.DisableImGuiKeyboardNavigation();
        }

        var inputFieldDeactivated = ImGui.IsItemDeactivated();
        var isPopupHovered = false;

        if (ImGui.IsItemActive() || isActive)
        {
            _activeInputId = inputId;

            // Tooltip flag is internal but is the only practical way to escape a parent modal's
            // clip rect (matches the pattern used in AssetInputWithTypeAheadSearch). Popup is
            // avoided because it conflicts with manual _activeInputId tracking.
            var popupPos = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y);
            var popupSize = new Vector2(ImGui.GetItemRectSize().X, 320 * T3Ui.UiScaleFactor);
            ImGui.SetNextWindowPos(popupPos);
            ImGui.SetNextWindowSize(popupSize);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Rgba);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            var dummyOpen = true;
            var clickedOutside = false;
            if (ImGui.Begin("##typeAheadSearchPopup", ref dummyOpen,
                            ImGuiWindowFlags.NoTitleBar
                            | ImGuiWindowFlags.NoMove
                            | ImGuiWindowFlags.Tooltip
                            | ImGuiWindowFlags.NoFocusOnAppearing))
            {
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, UiColors.BackgroundActive.Fade(0.3f).Rgba);

                // Inside a modal, ImGui blocks mouse-wheel routing to the tooltip-flagged
                // popup. A BeginChild inside the popup registers a proper scrollable region
                // whose wheel/scrollbar works regardless. AlwaysVerticalScrollbar so the
                // bar is reliably present even when the user expects to scroll just below
                // the threshold.
                if (ImGui.BeginChild("##scroll", Vector2.Zero, ImGuiChildFlags.None,
                                     ImGuiWindowFlags.AlwaysVerticalScrollbar 
                                     | ImGuiWindowFlags.NoSavedSettings ))
                {
                    var visibleIndex = 0;
                    var processedIndex = -1;
                    while (true)
                    {
                        processedIndex++;

                        if (_selectedFilteredResultIndex == -1 && processedIndex == selectedIndex)
                            _selectedFilteredResultIndex = visibleIndex;

                        var isCurrentIndex = _selectedFilteredResultIndex == visibleIndex;
                        if (isCurrentIndex)
                            selectedIndex = processedIndex;

                        if (isCurrentIndex && shouldUpdateScroll)
                            ImGui.SetScrollHereY();

                        ImGui.PushID(processedIndex);
                        var result = filterAndDrawItem(_searchString, isCurrentIndex);
                        ImGui.PopID();

                        if (result == ItemResults.Completed)
                            break;

                        if (result == ItemResults.FilteredOut)
                            continue;

                        if (result is ItemResults.Visible or ItemResults.Activated)
                            visibleIndex++;

                        if (result == ItemResults.Activated
                            || (isCurrentIndex && !upDownKeysPressed && ImGui.IsKeyPressed(Key.Return.ToImGuiKey())))
                        {
                            wasChanged = true;
                            selectedIndex = processedIndex;
                            Reset();
                            break;
                        }
                    }
                    _filteredCount = visibleIndex;
                    ImGuiUtils.ResetCursorForExtentCheck();
                }
                ImGui.EndChild();

                isPopupHovered = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize())
                                       .Contains(ImGui.GetMousePos());
                
                clickedOutside = !justOpened && !isPopupHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

                ImGui.PopStyleColor();
            }
            ImGui.End();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(3);

            if (clickedOutside)
            {
                Reset();
                return wasChanged;
            }
        }

        if (inputFieldDeactivated && !isPopupHovered)
            Reset();

        return wasChanged;
    }

    private static void Reset()
    {
        DrawUtils.RestoreImGuiKeyboardNavigation();
        _searchString = string.Empty;
        _activeInputId = 0;
        _selectedFilteredResultIndex = -1;
        _filteredCount = 0;
    }
    
    private static string _searchString = string.Empty;
    private static int _selectedFilteredResultIndex;
    private static uint _activeInputId;
    private static int _filteredCount;
    private static int _lastActiveFrame;
}