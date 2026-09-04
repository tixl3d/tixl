#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    private void DrawLibContent()
    {
        var iconCount = 2;
        _state.TreeHandler.Update();

        _state.SearchStringChanged |= CustomComponents.DrawInputFieldWithPlaceholder("Search Assets...",
                                                                                     ref _state.SearchString,
                                                                                     -ImGui.GetFrameHeight() * iconCount + 18 * T3Ui.UiScaleFactor);

        // Collapse icon
        {
            ImGui.SameLine();
            var collapseIconState = _state.TreeHandler.NoFolderOpen
                                        ? CustomComponents.ButtonStates.Default
                                        : CustomComponents.ButtonStates.Emphasized;

            if (CustomComponents.IconButton(Icon.TreeCollapse, Vector2.Zero, collapseIconState))
            {
                _state.TreeHandler.CollapseAll();
            }
        }

        // Tools and settings
        {
            ImGui.SameLine();
            var toolItemState = _state.ActiveTypeFilters.Count > 0
                                    ? CustomComponents.ButtonStates.NeedsAttention
                                    : CustomComponents.ButtonStates.Emphasized;

            if (CustomComponents.IconButton(Icon.Settings2, Vector2.Zero, toolItemState))
            {
                ImGui.OpenPopup(SettingsPopUpId);
            }

            DrawAssetToolsPopup();
        }

        DrawDeleteConfirmationDialog();
        DrawRelinkFolderDialog();

        ImGui.BeginChild("scrolling", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 10);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0));
            DrawFolder(_state.RootFolder);
            ImGui.PopStyleVar(3);
        }

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();
    }

    private bool _expandToFileTriggered;
    private static AssetFolder? _folderForMenu;

    private void DrawFolder(AssetFolder folder)
    {
        if (folder.IsHidden)
            return;

        var folderName = folder.Name.AsSpan();
        if (folderName == AssetFolder.RootNodeId)
        {
            DrawFolderContent(folder);
        }
        else
        {
            var hasMatches = folder.MatchingAssetCount > 0;
            var isSearching = !string.IsNullOrEmpty(_state.SearchString);
            var isFiltering = _state.CompatibleExtensionIds.Count > 0 || isSearching;
            var isCurrentCompositionPackage = _state.Composition?.Symbol.SymbolPackage.Name == folderName;



            if (isSearching && !hasMatches)
                return;

            if (isFiltering && !hasMatches)
                return;

            // Open main folders automatically
            if (!_state.OpenedExamplesFolderOnce
                && folderName.Equals(FileLocations.ExamplesPackageName, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.SetNextItemOpen(true);
                _state.OpenedExamplesFolderOnce = true;
            }

            if (!_state.OpenedProjectsFolderOnce
                && folderName.Equals(ProjectView.Focused?.RootInstance.Symbol.SymbolPackage.Name ?? string.Empty, StringComparison.InvariantCultureIgnoreCase))
            {
                ImGui.SetNextItemOpen(true);
                _state.OpenedProjectsFolderOnce = true;
            }


            ImGui.PushID(folder.HashCode);

            // Prepare drawing
            ImGui.SetNextItemWidth(10);

            var isLinkMountRoot = folder.Asset is { IsLinkMountRoot: true };
            var linkTargetMissing = isLinkMountRoot && folder.Asset!.LinkTargetMissing;

            var textMutedRgba = (isFiltering && !hasMatches) ? UiColors.TextMuted : UiColors.Text;
            textMutedRgba = textMutedRgba.Fade(isCurrentCompositionPackage ? 1 : 0.8f);
            if (linkTargetMissing)
                textMutedRgba = textMutedRgba.Fade(0.4f);

            ImGui.PushStyleColor(ImGuiCol.Text, textMutedRgba.Rgba);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, Color.Transparent.Rgba);

            var containsTargetFile = ContainsTargetFile(folder);
            if (_expandToFileTriggered && containsTargetFile)
            {
                ImGui.SetNextItemOpen(true);
            }

            _state.TreeHandler.UpdateForNode(folder.HashCode);

            var lastPos = ImGui.GetCursorScreenPos();

            // Draw the actual folder item
            ImGui.PushFont(isCurrentCompositionPackage ? Fonts.FontBold : Fonts.FontNormal);
            var isOpen = ImGui.TreeNodeEx(folderName);
            ImGui.PopFont();
            ImGui.PopStyleColor(3);

            CustomComponents.DrawHoverHighlightOnLastItem();

            if (isLinkMountRoot)
            {
                if (ImGui.IsItemHovered())
                {
                    CustomComponents.TooltipForLastItem(linkTargetMissing
                                                            ? $"Linked folder not found:\n{folder.AbsolutePath}\n\nRight-click to relink it."
                                                            : $"Linked to {folder.AbsolutePath}");
                }

                // Draw-list only: an ImGui item here would steal the context menu and
                // drop handling from the tree node row.
                var itemMin = ImGui.GetItemRectMin();
                var iconPos = new Vector2(itemMin.X
                                          + ImGui.GetFontSize()
                                          + ImGui.CalcTextSize(folder.Name).X
                                          + 6 * T3Ui.UiScaleFactor,
                                          itemMin.Y + (ImGui.GetItemRectSize().Y - Icons.FontSize) * 0.5f);
                Icons.DrawIconAtScreenPosition(Icon.Link,
                                               iconPos,
                                               ImGui.GetWindowDrawList(),
                                               linkTargetMissing
                                                   ? UiColors.StatusAttention
                                                   : UiColors.StatusAutomated.Fade(0.8f));
            }

            CustomComponents.DrawSearchMatchUnderline(_state.SearchString, folderName,
                                                      ImGui.GetItemRectMin()
                                                      + new Vector2(ImGui.GetFontSize(), 0));

            HandleDropFilesIntoFolder(folder);
            HandleDropAssetsIntoFolder(folder);

            _folderForMenu = folder;
            CustomComponents.ContextMenuForItem(() =>
            {
                CustomComponents.StylizedText(folder.Name, Fonts.FontSmall, UiColors.TextMuted);
                if (CustomComponents.DrawMenuItem(_openFolderInExplorerId, "Open in Explorer", reserveIconColumn: false))
                {
                    if (!string.IsNullOrEmpty(_folderForMenu.AbsolutePath))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(_folderForMenu.AbsolutePath);
                    }
                    else
                    {
                        Log.Warning($"Failed to get path for {_folderForMenu.Address}");
                    }
                }

                if (CustomComponents.DrawMenuItem(_createSubFolderId, "Create Sub Folder", reserveIconColumn: false))
                {
                    CreateSubFolder(folder);
                }

                if (CustomComponents.DrawMenuItem(_renameFolderId, "Rename", reserveIconColumn: false))
                {
                    _state.RenamingInProcessId = folder.Asset?.Id ?? Guid.Empty;
                    _state.RenameBuffer = folder.Name;
                }

                if (folder.Asset is { IsLinkMountRoot: true })
                {
                    if (CustomComponents.DrawMenuItem(_relinkFolderId, "Relink...", reserveIconColumn: false))
                    {
                        RequestRelinkFolder(folder.Asset);
                    }

                    // Only the .tixlLink marker is deleted - the external folder stays untouched
                    if (CustomComponents.DrawMenuItem(_removeFolderLinkId, "Remove Link", reserveIconColumn: false))
                    {
                        RemoveFolderLink(folder.Asset);
                    }
                }
                else if (CustomComponents.DrawMenuItem(_deleteFolderId, "Delete Folder...", reserveIconColumn: false) && folder.Asset != null)
                {
                    RequestDeleteAssets(folder.Asset);
                }
            });

            // Show filter count
            if (isFiltering && hasMatches)
            {
                ShowMatchCount(folder, containsTargetFile, isOpen);
            }

            _state.TreeHandler.NoFolderOpen = false;

            ImGui.PopID();

            HandleRenameFolder(folder, lastPos);

            if (isOpen)
            {
                DrawFolderContent(folder);
                _state.TreeHandler.FlagLastItemWasVisible();
                ImGui.TreePop();
            }
            else
            {
                if (ContainsTargetFile(folder))
                {
                    var h = ImGui.GetFontSize();
                    CustomComponents.RightAlign(h);

                    var clicked = ImGui.InvisibleButton("Reveal", new Vector2(h));
                    if (ImGui.IsItemHovered())
                    {
                        CustomComponents.TooltipForLastItem("Reveal selected asset");
                    }

                    if (_state.HasActiveInstanceChanged && !IsLastItemFullyVisible())
                    {
                        ImGui.SetScrollHereY(0.5f);
                    }

                    var timeSinceChange = (float)(ImGui.GetTime() - _state.TimeActiveInstanceChanged);
                    var fadeProgress = (timeSinceChange / 0.7f).Clamp(0, 1);
                    var blinkFade = MathUtils.Lerp(-MathF.Cos(timeSinceChange * 15f) * 0.8f + 0.2f, 1, fadeProgress);
                    var color = UiColors.StatusActivated.Fade(blinkFade);
                    Icons.DrawIconOnLastItem(Icon.Aim, color);

                    if (clicked)
                    //if (CustomComponents.IconButton(Icon.Aim, new Vector2(h)))
                    {
                        _expandToFileTriggered = true;
                    }
                }
            }
        }
    }

    /** Extracted to separate method to limit hot code reloading block from stack alloc **/
    private static void ShowMatchCount(AssetFolder folder, bool containsTargetFile, bool isOpen)
    {
        Span<char> buffer = stackalloc char[32];
        var countLabel = buffer.Format($"{folder.MatchingAssetCount}");

        var labelSize = ImGui.CalcTextSize(countLabel); // skip null byte
        CustomComponents.RightAlign(labelSize.X + 4 + ((containsTargetFile && !isOpen) ? Icons.FontSize : 0));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Fade(0.3f).Rgba);
        ImGui.TextUnformatted(countLabel);
        ImGui.PopStyleColor();
    }

    private static bool IsLastItemFullyVisible()
    {
        var windowTop = ImGui.GetWindowPos().Y;
        var windowBottom = windowTop + ImGui.GetWindowSize().Y;
        return ImGui.GetItemRectMin().Y >= windowTop && ImGui.GetItemRectMax().Y <= windowBottom;
    }

    private static bool ContainsTargetFile(AssetFolder folder)
    {
        var containsTargetFile = _state.ActivePathInput != null
                                 && !string.IsNullOrEmpty(folder.Address)
                                 && !string.IsNullOrEmpty(_state.ActiveAssetAddress)
                                 && _state.ActiveAssetAddress.StartsWith(folder.Address);
        return containsTargetFile;
    }

    private void DrawFolderContent(AssetFolder folder)
    {
        // Using a for loop to prevent modification during iteration exception
        for (var index = 0; index < folder.SubFolders.Count; index++)
        {
            var subspace = folder.SubFolders[index];
            DrawFolder(subspace);
        }

        for (var index = 0; index < folder.FolderAssets.Count; index++)
        {
            var asset = folder.FolderAssets[index];
            if (asset.IsDirectory || asset.Package == AssetRegistry.ExternalAssetsPackage)
                continue;

            DrawAssetItem(asset);
        }
    }

    private void DrawAssetItem(Asset asset)
    {
        var isActive = asset.Address == _state.ActiveAssetAddress;

        var fileConsumerOpSelected = _state.CompatibleExtensionIds.Count > 0;
        var fileConsumerOpIsCompatible = fileConsumerOpSelected
                                         && _state.CompatibleExtensionIds.Contains(asset.ExtensionId);

        // Skip not matching asset
        if (fileConsumerOpSelected && !fileConsumerOpIsCompatible)
            return;

        _state.KeepVisibleTreeItemIds.Add(asset.Id);

        ImGui.PushID(asset.Id.GetHashCode());
        {
            var fade = !fileConsumerOpSelected
                           ? 1.0f
                           : fileConsumerOpIsCompatible
                               ? 1f
                               : 0.2f;

            var knownType = asset.AssetType != AssetType.Unknown;
            var iconColor = ColorVariations.OperatorLabel.Apply(knownType ? asset.AssetType.Color : UiColors.Text);
            var icon = knownType
                           ? (Icon)asset.AssetType.IconId
                           : Icon.FileImage;

            var isSelected = _state.Selection.IsSelected(asset.Id);

            // Draw Item
            var cursorScreenPos = ImGui.GetCursorScreenPos();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 6);
            if (ButtonWithIcon(string.Empty,
                               asset.FileSystemInfo?.Name ?? string.Empty,
                               icon,
                               iconColor.Fade(fade),
                               isSelected ? UiColors.StatusActivated : UiColors.Text.Fade(fade),
                               isActive
                              ))
            {
                var stringInput = _state.ActivePathInput;
                if (stringInput != null && !isActive && fileConsumerOpIsCompatible)
                {
                    _state.ActiveAssetAddress = asset.Address;

                    ApplyResourcePath(asset, stringInput);
                }

                var io = ImGui.GetIO();
                bool ctrl = io.KeyCtrl;
                bool shift = io.KeyShift;

                if (shift && _state.AnchorSelectionKey != default)
                {
                    // TODO: This needs to be fixed for tree. 
                    var range = GetRange(_state.LastVisibleTreeItemIds, _state.AnchorSelectionKey, asset.Id);
                    if (!ctrl) _state.Selection.Clear();
                    _state.Selection.AddSelection(range);
                }
                else if (ctrl)
                {
                    if (isActive) _state.Selection.Deselect(asset.Id);
                    else _state.Selection.Select(asset.Id);
                    _state.AnchorSelectionKey = asset.Id;
                }
                else
                {
                    _state.Selection.Clear();
                    _state.Selection.Select(asset.Id);
                    _state.AnchorSelectionKey = asset.Id;
                }
            }

            CustomComponents.DrawSearchMatchUnderline(_state.SearchString, asset.FileSystemInfo?.Name,
                                                      ImGui.GetItemRectMin()
                                                      + new Vector2(ImGui.GetFontSize() + 5, 3));

            // IsItemVisible() would be true for partially visible items, so check the full rect
            if (isActive && _state.HasActiveInstanceChanged && !IsLastItemFullyVisible())
            {
                ImGui.SetScrollHereY(0.5f);
            }

            // Stop expanding if item becomes visible
            if (isActive && _expandToFileTriggered)
            {
                _expandToFileTriggered = false;
                ImGui.SetScrollHereY(0.5f);
            }

            CustomComponents.ContextMenuForItem(drawMenuItems: () =>
            {
                if (CustomComponents.DrawMenuItem(_editExternallyId, "Edit Externally", reserveIconColumn: false))
                {
                    var absolutePath = asset.FullPath;
                    if (!string.IsNullOrEmpty(absolutePath))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(absolutePath);
                    }
                }

                if (CustomComponents.DrawMenuItem(_revealAssetInExplorerId, "Reveal in Explorer", reserveIconColumn: false))
                {
                    var absolutePath = asset.FullPath;

                    var folder = Path.GetDirectoryName(absolutePath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        try
                        {
                            CoreUi.Instance.OpenWithDefaultApplication(folder);
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"Failed to get directory for {folder} {e.Message}");
                        }
                    }
                }

                CustomComponents.SeparatorLine();
                var selectedCount = _state.Selection.IsSelected(asset.Id) ? _state.Selection.SelectedKeys.Count : 1;
                var deleteLabel = selectedCount > 1 ? $"Delete {selectedCount} Selected..." : "Delete File...";
                if (CustomComponents.DrawMenuItem(_deleteAssetsId, deleteLabel, reserveIconColumn: false))
                {
                    RequestDeleteAssets(asset);
                }
            },
                                                title: asset.FileSystemInfo?.Name,
                                                id: "##symbolTreeSymbolContextMenu");

            var draggingStarted = DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.FileAsset, asset.Address);
            if (draggingStarted && !isSelected)
            {
                _state.Selection.Clear();
                _state.Selection.Select(asset.Id);
                _state.AnchorSelectionKey = asset.Id;
            }

            var hasUses = AssetRegistry.ReferencesForAssetId.TryGetValue(asset.Id, out var uses);
            if (!hasUses)
            {
                var pos = new Vector2(ImGui.GetWindowPos().X, cursorScreenPos.Y + (ImGui.GetFrameHeight() - 16 + 10) / 2);
                Icons.DrawIconAtScreenPosition(Icon.Sleeping, pos, ImGui.GetWindowDrawList(), UiColors.Text.Fade(0.4f));
            }

            DrawAssetTooltip(asset, hasUses, uses);

        }

        ImGui.PopID();
    }

    private static void DrawAssetTooltip(Asset asset, bool hasUses, List<AssetReference>? uses)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll); // Indicator for drag (actually not part of tooltip)

        if (CustomComponents.BeginTooltip(600))
        {
            ImGui.BeginGroup();
            {
                var absolutePath = asset.FullPath;
                var fileName = asset.FileSystemInfo?.Name ?? "Unknown";
                var path = absolutePath != null && absolutePath.EndsWith(fileName)
                               ? absolutePath[..^fileName.Length]
                               : absolutePath;

                // FormInputs.AddVerticalSpace(2);
                // CustomComponents.StylizedText($"in {path}", Fonts.FontSmall, UiColors.TextMuted);

                FormInputs.AddVerticalSpace();
            }
            ImGui.EndGroup();
            //ImGui.SameLine(0,10);

            ImGui.BeginGroup();
            {
                var package = ResourcePackageManager.SharedResourcePackages.FirstOrDefault(p => p.Id == asset.PackageId);
                ThumbnailManager.GetThumbnail(asset, package).AsImguiImage();
                FormInputs.AddVerticalSpace();
            }

            CustomComponents.StylizedText($"File size : {StringUtils.GetReadableFileSize(asset.FileSize)}", Fonts.FontSmall, UiColors.TextMuted);
            CustomComponents.StylizedText($"Last modified :{asset.FileSystemInfo?.LastWriteTime}",
                              Fonts.FontSmall, UiColors.TextMuted);

            int useCount = uses != null ? uses.Count : 0;

            string usedString = useCount > 0 ? $"Used by : {useCount} operators" : "Not used by any operator";

            CustomComponents.StylizedText(usedString, Fonts.FontSmall, UiColors.TextMuted);

            if (ImGui.GetIO().KeyShift)
            {
                ImGui.NewLine();
                if (hasUses && uses != null)
                {
                    CustomComponents.StylizedText("Symbols using this...", Fonts.FontSmall, UiColors.TextMuted);
                    foreach (var reference in uses)
                    {
                        DrawAssetReference(reference);
                    }
                }
            }

            ImGui.EndGroup();

        }

        CustomComponents.EndTooltip();
    }

    private static void DrawAssetReference(AssetReference reference)
    {
        if (!SymbolRegistry.TryGetSymbol(reference.SymbolId, out var symbol))
        {
            Log.Debug("Symbol for asset reference not found? " + reference.SymbolId);
            return;
        }

        if (!reference.IsDefaultValueReference)
        {
            if (!symbol.Children.TryGetValue(reference.SymbolChildId, out var symbolChild))
            {
                ImGui.TextUnformatted("??? child not found");
                return;
            }

            ImGui.TextColored(UiColors.TextMuted, $"{symbol.Namespace}.");
            ImGui.SameLine();
            ImGui.TextUnformatted($"{symbol.Name}");
            ImGui.SameLine();
            ImGui.TextColored(UiColors.TextMuted, $" » {symbolChild.Symbol.Name}");
            return;
        }

        var inputDefinition = symbol.InputDefinitions.FirstOrDefault(i => i.Id == reference.InputId);
        var inputName = inputDefinition?.Name ?? "???";
        ImGui.TextColored(UiColors.TextMuted, $"{symbol.Namespace}.");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{symbol.Name}");
        ImGui.SameLine();

        ImGui.TextColored(UiColors.TextMuted, $".{inputName} (Default)");
    }

    private static bool ButtonWithIcon(string id, string label, Icon icon, Color iconColor, Color textColor, bool isActive)
    {
        var cursorPos = ImGui.GetCursorScreenPos();
        var frameHeight = ImGui.GetFrameHeight();

        var dummyDim = new Vector2(frameHeight);
        if (!ImGui.IsRectVisible(cursorPos, cursorPos + dummyDim))
        {
            ImGui.Dummy(dummyDim); // maintain layout spacing
            return false;
        }

        var iconSize = Icons.FontSize;
        var padding = 4f;
        Vector2 iconDim = new(iconSize);

        var textSize = ImGui.CalcTextSize(label);
        var buttonSize = new Vector2(iconDim.X + padding + textSize.X + padding * 2,
                                     Math.Max(iconDim.Y + padding * 2, ImGui.GetFrameHeight()));

        var pressed = ImGui.InvisibleButton(id, buttonSize);

        var drawList = ImGui.GetWindowDrawList();
        var buttonMin = ImGui.GetItemRectMin();
        var buttonMax = ImGui.GetItemRectMax();
        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(buttonMin, buttonMax, UiColors.BackgroundActive.Fade(0.2f), 5);
        }

        if (isActive)
        {
            drawList.AddRect(buttonMin, buttonMax, UiColors.StatusActivated, 5);
        }

        var iconPos = new Vector2(buttonMin.X + padding,
                                  (int)(buttonMin.Y + (buttonSize.Y - iconDim.Y) * 0.5f) + 1);

        Icons.GetGlyphDefinition(icon, out var uvRange, out _);
        drawList.AddImage(ImGui.GetIO().Fonts.TexID,
                          iconPos,
                          iconPos + iconDim,
                          uvRange.Min,
                          uvRange.Max,
                          iconColor);

        Vector2 textPos = new(iconPos.X + iconDim.X + padding,
                              buttonMin.Y + (buttonSize.Y - textSize.Y) * 0.5f);

        drawList.AddText(textPos, textColor, label);
        return pressed;
    }

    private static void ApplyResourcePath(Asset asset, InputSlot<string> inputSlot)
    {
        var instance = inputSlot.Parent;
        var composition = instance.Parent;
        if (composition == null)
        {
            Log.Warning("Can't find composition to apply resource path");
            return;
        }

        inputSlot.Input.IsDefault = false;

        var changeInputValueCommand = new ChangeInputValueCommand(composition.Symbol,
                                                                  instance.SymbolChildId,
                                                                  inputSlot.Input,
                                                                  inputSlot.Input.Value);

        // warning: we must not use Value because this will use by abstract resource to detect changes
        inputSlot.TypedInputValue.Value = asset.Address;

        inputSlot.DirtyFlag.ForceInvalidate();
        inputSlot.Parent.Parent?.Symbol.InvalidateInputInAllChildInstances(inputSlot);
        changeInputValueCommand.AssignNewValue(inputSlot.Input.Value);
        UndoRedoStack.Add(changeInputValueCommand);
    }

    // Helper to find IDs between two points
    private static IEnumerable<Guid> GetRange(List<Guid> list, Guid startId, Guid endId)
    {
        var start = list.FindIndex(id => id == startId);
        var end = list.FindIndex(id => id == endId);

        var min = Math.Min(start, end);
        var max = Math.Max(start, end);

        return list.Skip(min).Take(max - min + 1);
    }

    private static readonly int _openFolderInExplorerId = nameof(_openFolderInExplorerId).GetHashCode();
    private static readonly int _createSubFolderId = nameof(_createSubFolderId).GetHashCode();
    private static readonly int _renameFolderId = nameof(_renameFolderId).GetHashCode();
    private static readonly int _relinkFolderId = nameof(_relinkFolderId).GetHashCode();
    private static readonly int _removeFolderLinkId = nameof(_removeFolderLinkId).GetHashCode();
    private static readonly int _deleteFolderId = nameof(_deleteFolderId).GetHashCode();
    private static readonly int _editExternallyId = nameof(_editExternallyId).GetHashCode();
    private static readonly int _revealAssetInExplorerId = nameof(_revealAssetInExplorerId).GetHashCode();
    private static readonly int _deleteAssetsId = nameof(_deleteAssetsId).GetHashCode();
}