#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    /// <summary>
    /// Collects the delete targets (the clicked asset, or the whole selection when it is part of one),
    /// computes file count and total size (folders recursively), and requests the confirmation dialog.
    /// </summary>
    private static void RequestDeleteAssets(Asset clickedAsset)
    {
        _deleteTargets.Clear();
        if (_state.Selection.IsSelected(clickedAsset.Id) && _state.Selection.SelectedKeys.Count > 1)
        {
            foreach (var asset in _state.AllAssets)
            {
                if (_state.Selection.IsSelected(asset.Id))
                    _deleteTargets.Add(asset);
            }
        }
        else
        {
            _deleteTargets.Add(clickedAsset);
        }

        _deleteFileCount = 0;
        _deleteFolderCount = 0;
        _deleteTotalBytes = 0;
        foreach (var asset in _deleteTargets)
        {
            try
            {
                if (asset.IsDirectory)
                {
                    _deleteFolderCount++;
                    if (asset.FullPath is { } folderPath && Directory.Exists(folderPath))
                    {
                        foreach (var file in new DirectoryInfo(folderPath).EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            _deleteFileCount++;
                            _deleteTotalBytes += file.Length;
                        }
                    }
                }
                else if (asset.FileSystemInfo is FileInfo { Exists: true } fileInfo)
                {
                    _deleteFileCount++;
                    _deleteTotalBytes += fileInfo.Length;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Can't measure {asset.Address}: {e.Message}");
            }
        }

        _deleteDialogRequested = true;
    }

    /// <summary>Modal confirmation for deleting assets. Must be drawn at window scope every frame.</summary>
    private static void DrawDeleteConfirmationDialog()
    {
        if (_deleteDialogRequested)
        {
            _deleteDialogRequested = false;
            _deleteDialogOpen = true;
            ImGui.OpenPopup(DeleteDialogId);
        }

        if (!_deleteDialogOpen)
            return;

        if (!ModalDialog.BeginStaticDialog(DeleteDialogId, ref _deleteDialogOpen))
            return;

        ImGui.TextUnformatted(BuildDeleteSummary());

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(_canRecycle
                                  ? "Files will be moved to the Windows Recycle Bin."
                                  : "This cannot be undone.");
        ImGui.PopStyleColor();

        FormInputs.AddVerticalSpace();

        if (ImGui.Button(_canRecycle ? "Move to Recycle Bin" : "Delete permanently"))
        {
            ExecuteDelete();
            _deleteDialogOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _deleteDialogOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ModalDialog.EndStaticDialog();
    }

    private static string BuildDeleteSummary()
    {
        var files = _deleteFileCount == 1 ? "1 file" : $"{_deleteFileCount} files";
        var folders = _deleteFolderCount switch
                          {
                              0 => string.Empty,
                              1 => " and 1 folder",
                              _ => $" and {_deleteFolderCount} folders",
                          };
        return $"Delete {files}{folders} ({FormatByteSize(_deleteTotalBytes)})?";
    }

    private static void ExecuteDelete()
    {
        foreach (var asset in _deleteTargets)
        {
            var path = asset.FullPath;
            if (string.IsNullOrEmpty(path))
                continue;

            try
            {
                if (_canRecycle)
                {
                    MoveToRecycleBin(path);
                }
                else if (asset.IsDirectory)
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }

                AssetRegistry.RemoveObsoleteAsset(asset);
            }
            catch (Exception e)
            {
                Log.Warning($"Can't delete {path}: {e.Message}");
            }
        }

        _deleteTargets.Clear();
        _state.Selection.Clear();
    }

    private static string FormatByteSize(long bytes)
    {
        return bytes switch
                   {
                       >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
                       >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
                       >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
                       _           => $"{bytes} bytes",
                   };
    }

    #region Recycle bin
    // .NET has no cross-platform recycle-bin API, so this uses the Win32 shell operation with
    // FOF_ALLOWUNDO. On other platforms deletion falls back to a permanent delete (see _canRecycle).
    private static void MoveToRecycleBin(string absolutePath)
    {
        var op = new ShFileOpStruct
                     {
                         Func = FoDelete,
                         From = absolutePath + "\0\0", // double-null-terminated list
                         Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
                     };
        var result = SHFileOperation(ref op);
        if (result != 0 || op.AnyOperationsAborted)
            throw new IOException($"Moving to recycle bin failed (0x{result:X})");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    private const uint FoDelete = 3;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoErrorUi = 0x400;
    #endregion

    private static readonly bool _canRecycle = OperatingSystem.IsWindows();

    private static readonly List<Asset> _deleteTargets = [];
    private static int _deleteFileCount;
    private static int _deleteFolderCount;
    private static long _deleteTotalBytes;
    private static bool _deleteDialogRequested;
    private static bool _deleteDialogOpen;
    private const string DeleteDialogId = "Delete assets?";
}
