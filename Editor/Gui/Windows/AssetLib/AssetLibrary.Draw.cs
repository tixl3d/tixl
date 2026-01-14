#nullable enable
using System.IO;
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    private void DrawLibContent()
    {
        var iconCount = 2;
        _state.TreeHandler.Update();

        CustomComponents.DrawInputFieldWithPlaceholder("Search Assets...",
                                                       ref _state.Filter.SearchString,
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
        if (folder.Name == AssetFolder.RootNodeId)
        {
            DrawFolderContent(folder);
        }
        else
        {
            ImGui.SetNextItemWidth(10);
            if (folder.Name == "Lib" && !_state.OpenedLibFolderOnce)
            {
                ImGui.SetNextItemOpen(true);
                _state.OpenedLibFolderOnce = true;
            }

            _state.TreeHandler.UpdateForNode(folder.HashCode);

            if (_expandToFileTriggered && ContainsTargetFile(folder))
            {
                ImGui.SetNextItemOpen(true);
            }

            var isOpen = ImGui.TreeNodeEx(folder.Name);
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
                                                            Log.Warning($"Failed to get path for {_folderForMenu.AliasPath}");
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

    private bool ContainsTargetFile(AssetFolder folder)
    {
        var containsTargetFile = _state.ActivePathInput != null
                                 && !string.IsNullOrEmpty(folder.AbsolutePath)
                                 && !string.IsNullOrEmpty(_state.ActiveAbsolutePath)
                                 && _state.ActiveAbsolutePath.StartsWith(folder.AbsolutePath);
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
            DrawAssetItem(folder.FolderAssets[index]);
        }
    }

    private void DrawAssetItem(AssetItem asset)
    {
        var isSelected = asset.AbsolutePath == _state.ActiveAbsolutePath;

        var fileConsumerOpSelected = _state.CompatibleExtensionIds.Count > 0;
        var fileConsumerOpIsCompatible = fileConsumerOpSelected
                                         && _state.CompatibleExtensionIds.Contains(asset.FileExtensionId);

        // Skip not matching asset
        if (fileConsumerOpSelected && !fileConsumerOpIsCompatible)
            return;

        ImGui.PushID(RuntimeHelpers.GetHashCode(asset));
        {
            var fade = !fileConsumerOpSelected
                           ? 0.8f
                           : fileConsumerOpIsCompatible
                               ? 1f
                               : 0.2f;

            var iconColor = ColorVariations.OperatorLabel.Apply(asset.AssetType?.Color ?? UiColors.Text);
            var icon = asset.AssetType?.Icon ?? Icon.FileImage;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);
            if (ButtonWithIcon(string.Empty,
                               asset.FileInfo.Name,
                               icon,
                               iconColor.Fade(fade),
                               UiColors.Text.Fade(fade),
                               isSelected
                              ))
            {
                var stringInput = _state.ActivePathInput;
                if (stringInput != null && !isSelected && fileConsumerOpIsCompatible)
                {
                    _state.ActiveAbsolutePath = asset.AbsolutePath;

                    ApplyResourcePath(asset, stringInput);
                }
            }

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
                                                                       CoreUi.Instance.OpenWithDefaultApplication(asset.FileInfo.FullName);
                                                                       Log.Debug("Not implemented yet");
                                                                   }
                                                               },
                                                title: asset.FileInfo.Name,
                                                id: "##symbolTreeSymbolContextMenu");

            DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.FileAsset, asset.FileAliasPath, "Move or use asset");

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll); // Indicator for drag

                // Tooltip
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
                    ImGui.TextUnformatted($"""
                                           Filesize: {asset.FileInfo.Length}
                                           Path: {asset.FileInfo.Directory}
                                           Time: {asset.FileInfo.LastWriteTime}
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

    private static void ApplyResourcePath(AssetItem asset, InputSlot<string> inputSlot)
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
        inputSlot.TypedInputValue.Value = asset.FileAliasPath;

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