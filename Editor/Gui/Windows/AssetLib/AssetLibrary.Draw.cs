#nullable enable
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
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
                                        ? CustomComponents.ButtonStates.Dimmed
                                        : CustomComponents.ButtonStates.Normal;

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
                                    : CustomComponents.ButtonStates.Normal;

            if (CustomComponents.IconButton(Icon.Settings2, Vector2.Zero, toolItemState))
            {
                ImGui.OpenPopup(SettingsPopUpId);
            }

            DrawAssetToolsPopup();
        }

        ImGui.BeginChild("scrolling", Vector2.Zero, false, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 10);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0));
            DrawFolder(_state.RootFolder);
            ImGui.PopStyleVar(3);
        }
        ImGui.EndChild();
    }

    private bool _expandToFileTriggered;
    private static AssetFolder? _folderForMenu;

    private void DrawFolder(AssetFolder folder)
    {
        var strId = folder.Name.AsSpan();
        if (strId == AssetFolder.RootNodeId)
        {
            DrawFolderContent(folder);
        }
        else
        {
            var hasMatches = folder.MatchingAssetCount > 0;
            var isSearching = !string.IsNullOrEmpty(_state.SearchString);
            var isFiltering = _state.CompatibleExtensionIds.Count > 0 || isSearching;
            var isCurrentCompositionPackage = _state.Composition?.Symbol.SymbolPackage.Name == strId;

            if (isSearching && !hasMatches)
                return;

            // Open main folders automatically
            if (!_state.OpenedExamplesFolderOnce
                && strId == FileLocations.ExamplesPackageName)
            {
                ImGui.SetNextItemOpen(true);
                _state.OpenedExamplesFolderOnce = true;
            }

            if (!_state.OpenedProjectsFolderOnce
                && strId == ProjectView.Focused?.RootInstance.Symbol.SymbolPackage.Name)
            {
                ImGui.SetNextItemOpen(true);
                _state.OpenedProjectsFolderOnce = true;
            }

            // Draw 
            ImGui.SetNextItemWidth(10);

            var textMutedRgba = (isFiltering && !hasMatches) ? UiColors.TextMuted : UiColors.Text;
            textMutedRgba = textMutedRgba.Fade(isCurrentCompositionPackage ? 1 : 0.8f);

            ImGui.PushStyleColor(ImGuiCol.Text, textMutedRgba.Rgba);

            _state.TreeHandler.UpdateForNode(folder.HashCode);

            var containsTargetFile = ContainsTargetFile(folder);
            if (_expandToFileTriggered && containsTargetFile)
            {
                ImGui.SetNextItemOpen(true);
            }

            // Draw the actual folder item
            ImGui.PushFont(isCurrentCompositionPackage ? Fonts.FontBold : Fonts.FontNormal);
            var isOpen = ImGui.TreeNodeEx(strId);
            ImGui.PopFont();

            DrawSearchMatchUnderline(_state.SearchString, strId, Vector2.Zero);
            
            // Show filter count
            if (isFiltering && hasMatches)
            {
                ShowMatchCount(folder, containsTargetFile, isOpen);
            }

            ImGui.PopStyleColor();

            _state.TreeHandler.NoFolderOpen = false;

            _folderForMenu = folder;
            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    if (ImGui.MenuItem("Open in Explorer"))
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
                                                });

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
                    var x = ImGui.GetContentRegionMax().X - h;
                    ImGui.SameLine(x);

                    var clicked = ImGui.InvisibleButton("Reveal", new Vector2(h));
                    if (ImGui.IsItemHovered())
                    {
                        CustomComponents.TooltipForLastItem("Reveal selected asset");
                    }

                    if (_state.HasActiveInstanceChanged)
                    {
                        ImGui.SetScrollHereY();
                    }

                    var timeSinceChange = (float)(ImGui.GetTime() - _state.TimeActiveInstanceChanged);
                    var fadeProgress = (timeSinceChange / 0.5f).Clamp(0, 1);
                    var blinkFade = -MathF.Cos(timeSinceChange * 15f) * (1f - fadeProgress) * 0.7f + 0.75f;
                    var color = UiColors.StatusActivated.Fade(blinkFade);
                    Icons.DrawIconOnLastItem(Icon.Aim, color);

                    if (clicked)
                        //if (CustomComponents.IconButton(Icon.Aim, new Vector2(h)))
                    {
                        _expandToFileTriggered = true;
                    }
                }

                if (DragAndDropHandling.IsDraggingWith(DragAndDropHandling.DragTypes.FileAsset))
                {
                    ImGui.SameLine();
                    ImGui.PushID("DropButton");
                    ImGui.Button("  <-", new Vector2(50, 15));
                    //HandleDropTarget(subtree);
                    ImGui.PopID();
                }
            }
        }
    }

    private static void DrawSearchMatchUnderline(string searchString, ReadOnlySpan<char> strId, Vector2 offset)
    {
        if (string.IsNullOrEmpty(searchString)) 
            return;
        
        var start = strId.IndexOf(searchString, StringComparison.OrdinalIgnoreCase);
        if (start == -1) 
            return;
        
        var span = strId.Slice(start, searchString.Length);
        var sizeMatch = ImGui.CalcTextSize(span);

        var sizeBefore = start > 0 ? ImGui.CalcTextSize(strId[..start])
                             : Vector2.Zero;
                    
        var fontSize = ImGui.GetFontSize();
        var min = ImGui.GetItemRectMin() 
                  + offset
                  + new Vector2(fontSize + sizeBefore.X, fontSize) ;
                    
        ImGui.GetWindowDrawList().AddLine(min, min + new Vector2(sizeMatch.X,0), UiColors.StatusAttention);
    }

    /** Extracted to separate method to limit hot code reloading block from stack alloc **/
    private static void ShowMatchCount(AssetFolder folder, bool containsTargetFile, bool isOpen)
    {
        Span<char> buffer = stackalloc char[32];
        var countLabel = buffer.Format($"{folder.MatchingAssetCount}\0");

        var labelSize = ImGui.CalcTextSize(countLabel[..^1]); // skip null byte
        CustomComponents.RightAlign(labelSize.X + 4 + ((containsTargetFile && !isOpen) ? Icons.FontSize : 0));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Fade(0.3f).Rgba);
        ImGui.TextUnformatted(countLabel);
        ImGui.PopStyleColor();
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
            if (asset.IsDirectory)
                continue;

            DrawAssetItem(asset);
        }
    }

    private void DrawAssetItem(Asset asset)
    {
        var isSelected = asset.Address == _state.ActiveAssetAddress;

        var fileConsumerOpSelected = _state.CompatibleExtensionIds.Count > 0;
        var fileConsumerOpIsCompatible = fileConsumerOpSelected
                                         && _state.CompatibleExtensionIds.Contains(asset.ExtensionId);

        // Skip not matching asset
        if (fileConsumerOpSelected && !fileConsumerOpIsCompatible)
            return;

        ImGui.PushID(RuntimeHelpers.GetHashCode(asset));
        {
            var fade = !fileConsumerOpSelected
                           ? 0.7f
                           : fileConsumerOpIsCompatible
                               ? 1f
                               : 0.2f;

            var iconColor = ColorVariations.OperatorLabel.Apply(asset.AssetType != null ? asset.AssetType.Color : UiColors.Text);
            var icon = asset.AssetType != null
                           ? (Icon)asset.AssetType.IconId
                           : Icon.FileImage;

            // Draw Item
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 6);
            if (ButtonWithIcon(string.Empty,
                               asset.FileSystemInfo?.Name ?? string.Empty,
                               icon,
                               iconColor.Fade(fade),
                               UiColors.Text.Fade(fade),
                               isSelected
                              ))
            {
                var stringInput = _state.ActivePathInput;
                if (stringInput != null && !isSelected && fileConsumerOpIsCompatible)
                {
                    _state.ActiveAssetAddress = asset.Address;

                    ApplyResourcePath(asset, stringInput);
                }
            }
            
            DrawSearchMatchUnderline(_state.SearchString, asset.FileSystemInfo?.Name, new Vector2(4,3));

            if (isSelected && !ImGui.IsItemVisible() && _state.HasActiveInstanceChanged)
            {
                ImGui.SetScrollHereY();
            }

            // Stop expanding if item becomes visible
            if (isSelected && _expandToFileTriggered)
            {
                _expandToFileTriggered = false;
                ImGui.SetScrollHereY(1f);
            }

            CustomComponents.ContextMenuForItem(drawMenuItems: () =>
                                                               {
                                                                   if (ImGui.MenuItem("Edit externally"))
                                                                   {
                                                                       var absolutePath = asset.FileSystemInfo?.FullName;
                                                                       if (!string.IsNullOrEmpty(absolutePath))
                                                                       {
                                                                           CoreUi.Instance.OpenWithDefaultApplication(absolutePath);
                                                                       }

                                                                       Log.Debug("Not implemented yet");
                                                                   }
                                                               },
                                                title: asset.FileSystemInfo?.Name,
                                                id: "##symbolTreeSymbolContextMenu");

            DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.FileAsset, asset.Address, "Move or use asset");

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll); // Indicator for drag

                // Tooltip
                {
                    var absolutePath = asset.FileSystemInfo?.FullName;
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
                    ImGui.TextUnformatted($"""
                                           Filesize: {asset.FileSize}
                                           Path: {absolutePath}
                                           Time: {asset.FileSystemInfo?.LastWriteTime}
                                           """);
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleVar();
                    ImGui.EndTooltip();
                }
            }

            // // Click
            // if (ImGui.IsItemDeactivated())
            // {
            //     var wasClick = ImGui.GetMouseDragDelta().Length() < 4;
            //     if (wasClick)
            //     {
            //         // TODO: implement
            //     }
            // }
        }

        ImGui.PopID();
    }

    // TODO: Clean up and move to custom components
    private static bool ButtonWithIcon(string id, string label, Icon icon, Color iconColor, Color textColor, bool selected)
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
        if (selected)
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

    // private static void HandleDropTarget(AssetFolder subtree)
    // {
    //     if (!DragAndDropHandling.TryGetDataDroppedLastItem(DragAndDropHandling.AssetDraggingId, out var data))
    //         return;
    //
    //     // TODO: Implement dragging of files
    //
    //     // if (!Guid.TryParse(data, out var path))
    //     //     return;
    //     //
    //     // if (!MoveSymbolToNamespace(path, subtree.GetAsString(), out var reason))
    //     //     BlockingWindow.Instance.ShowMessageBox(reason, "Could not move symbol's namespace");
    // }
}