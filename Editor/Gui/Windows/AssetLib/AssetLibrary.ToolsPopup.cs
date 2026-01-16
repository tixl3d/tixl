using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    private static void DrawAssetToolsPopup()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3 * T3Ui.UiScaleFactor));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6) * T3Ui.UiScaleFactor);
        if (ImGui.BeginPopupContextWindow(SettingsPopUpId))
        {
            CustomComponents.DrawMenuItem(_deleteFileButtonId, "Delete file", null, false, false);
            CustomComponents.TooltipForLastItem("Not implemented yet :-(");

            if (CustomComponents.DrawMenuItem(_openExternallyId, "Open externally",
                                              null,
                                              false,
                                              !string.IsNullOrEmpty(_state.ActiveAbsolutePath)))
            {
                if (!string.IsNullOrEmpty(_state.ActiveAbsolutePath))
                    CoreUi.Instance.OpenWithDefaultApplication(_state.ActiveAbsolutePath);
            }

            if (CustomComponents.DrawMenuItem(_revealInExplorerId, "Reveal in Explorer",
                                              null,
                                              false,
                                              !string.IsNullOrEmpty(_state.ActiveAbsolutePath)))
            {
                if (!string.IsNullOrEmpty(_state.ActiveAbsolutePath))
                {
                    try
                    {
                        var folder = Path.GetDirectoryName(_state.ActiveAbsolutePath);
                        if (!string.IsNullOrEmpty(folder))
                            CoreUi.Instance.OpenWithDefaultApplication(folder);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to get directory for {_state.ActiveAbsolutePath} {e.Message}");
                    }
                }
            }

            CustomComponents.SeparatorLine();

            var showAllTypes = _state.ActiveTypeFilters.Count == 0;
            if (DrawAssetFilterOption(_allAssetId, Icon.Stack, UiColors.Text, "All", AssetTypeRegistry.TotalAssetCount, ref showAllTypes))
            {
                _state.ActiveTypeFilters.Clear();
                _state.CompatibleExtensionIds.Clear();
                _state.FilteringNeedsUpdate = true;
            }

            Input.FormInputs.AddVerticalSpace();

            for (var index = 0; index < AssetTypeRegistry.AssetTypes.Count; index++)
            {
                var assetType = AssetTypeRegistry.AssetTypes[index];
                var count = assetType.MatchingFileCount;
                var xIcon = assetType.Icon;
                var readOnlySpan = assetType.Name;
                var iconColor = ColorVariations.OperatorLabel.Apply(assetType.Color);

                var isActive = _state.ActiveTypeFilters.Contains(assetType);
                if (DrawAssetFilterOption(_allAssetId + 1 + index, xIcon, iconColor, readOnlySpan, count, ref isActive))
                {
                    if (isActive)
                    {
                        if (!ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
                        {
                            _state.ActiveTypeFilters.Clear();
                            _state.CompatibleExtensionIds.Clear();
                        }

                        _state.ActiveTypeFilters.Add(assetType);
                        foreach (var extId in assetType.ExtensionIds)
                        {
                            _state.CompatibleExtensionIds.Add(extId);
                        }
                    }
                    else
                    {
                        _state.ActiveTypeFilters.Remove(assetType);
                    }

                    _state.FilteringNeedsUpdate = true;
                }
            }

            CustomComponents.SeparatorLine();
            CustomComponents.DrawMenuItem(_syncToSelectionId, "Sync with Selection", ref UserSettings.Config.SyncWithOperatorSelection);
            ImGui.End();
        }

        ImGui.PopStyleVar(2);
    }



    /// <summary>
    /// This is very similar to <see cref="CustomComponents.DrawMenuItem()"/>
    /// </summary>
    private static bool DrawAssetFilterOption(int id, Icon xIcon, Color iconColor, string label, int count, ref bool isChecked)
    {
        var h = ImGui.GetFrameHeight();
        var imguiPadding = ImGui.GetStyle().ItemSpacing;

        var keyboardShortCut = string.Empty;
        if (count > 0)
        {
            keyboardShortCut = $"{count}";
        }

        var shortCutWidth = string.IsNullOrEmpty(keyboardShortCut) ? 0 : ImGui.CalcTextSize(keyboardShortCut).X;
        var labelWidth = ImGui.CalcTextSize(label).X;

        var paddingFactor = 1.4f;
        var leftPaddingIcon = imguiPadding.X + Icons.FontSize * paddingFactor;
        var leftPaddingText = leftPaddingIcon + Icons.FontSize * paddingFactor;

        var width = leftPaddingIcon + labelWidth + imguiPadding.X * 2;
        if (shortCutWidth > 0)
        {
            width += shortCutWidth + h;
        }

        var windowWidth = ImGui.GetColumnWidth();

        if (width < windowWidth)
            width = windowWidth;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton(string.Empty, new Vector2(width, h));
        ImGui.PopID();
        ImGui.PopStyleVar();

        var fade = isChecked ? 1 : 0.7f;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.25f), 4);
        }

        var w = Icons.FontSize * paddingFactor;

        if (isChecked)
        {
            Icons.DrawIconAtScreenPosition(Icon.Checkmark,
                                           (min + new Vector2(imguiPadding.X,
                                                              h / 2 - Icons.FontSize / 2)).Floor(),
                                           drawList, UiColors.Text);
        }

        Icons.DrawIconAtScreenPosition(xIcon,
                                       (min + new Vector2(leftPaddingIcon,
                                                          h / 2 - Icons.FontSize / 2)).Floor(),
                                       drawList, iconColor.Fade(fade));

        var textHeight = ImGui.GetFontSize();
        drawList.AddText(min + new Vector2(leftPaddingText,
                                           h / 2 - textHeight / 2),
                         UiColors.Text.Fade(fade),
                         label);

        if (!string.IsNullOrEmpty(keyboardShortCut))
        {
            drawList.AddText(min
                             + new Vector2(windowWidth - shortCutWidth - imguiPadding.X,
                                           h / 2 - textHeight / 2),
                             UiColors.TextMuted.Fade(fade),
                             keyboardShortCut);
        }

        if (clicked)
        {
            isChecked = !isChecked;
            ImGui.CloseCurrentPopup();
        }

        return clicked;
    }
    
    private static readonly int _deleteFileButtonId = nameof(_deleteFileButtonId).GetHashCode();
    private static readonly int _openExternallyId = nameof(_openExternallyId).GetHashCode();
    private static readonly int _syncToSelectionId = nameof(_syncToSelectionId).GetHashCode();
    private static readonly int _revealInExplorerId = nameof(_revealInExplorerId).GetHashCode();
    private static readonly int _allAssetId = nameof(_allAssetId).GetHashCode();
    private const string SettingsPopUpId = "_AssetTools";
}