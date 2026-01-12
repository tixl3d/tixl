using System.IO;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.UiHelpers;

public static class FileOperations
{
    public static string PickResourceFilePath(string initialPath = "", string filter = null)
    {
        using (var openFileDialog = EditorUi.Instance.CreateFilePicker())
        {
            openFileDialog.InitialDirectory = String.IsNullOrEmpty(initialPath)
                                                  ? GetAbsoluteResourcePath()
                                                  : GetAbsoluteDirectory(initialPath);
                
            openFileDialog.Filter = string.IsNullOrEmpty(filter) 
                                        ? "jpg files (*.jpg)|*.jpg|All files (*.*)|*.*"
                                        : filter;
                
            //openFileDialog.Filter = "Font files (*.fnt)|*.fnt";
            openFileDialog.FilterIndex = 2;

            try
            {
                if (!openFileDialog.ChooseFile())
                    return null;
            }
            catch (Exception e)
            {
                Log.Warning("Couldn't open file picker:" +e.Message);
                return null;
            }

            var absolutePath = openFileDialog.FileName;
            return ConvertToRelativeFilepath(absolutePath);
        }
    }

    public static string PickResourceDirectory()
    {
        using (var folderBrowser = EditorUi.Instance.CreateFilePicker())
        {
            folderBrowser.InitialDirectory = GetAbsoluteResourcePath();
            folderBrowser.ValidateNames = false;
            folderBrowser.CheckFileExists = false;
            folderBrowser.CheckPathExists = true;
            folderBrowser.FileName = "Folder Selection.";
            if (!folderBrowser.ChooseFile())
                return null;

            var absoluteFolderPath = System.IO.Path.GetDirectoryName(folderBrowser.FileName);
            return ConvertToRelativeFilepath(absoluteFolderPath);
        }
    }

    public enum FilePickerTypes
    {
        None,
        File,
        Folder,
    }
        

    public static bool DrawFileSelector(FilePickerTypes type, ref string value, string filter=null)
    {
        var modified = false;
        ImGui.SameLine();
        if (ImGui.Button("...", new Vector2(30, 0) * T3Ui.UiScaleFactor))
        {
            string newPath = type == FilePickerTypes.File
                                 ? PickResourceFilePath(value, filter)
                                 : PickResourceDirectory();
            if (!string.IsNullOrEmpty(newPath))
            {
                value = newPath;
                modified = true;
            }
        }

        if (type == FilePickerTypes.File)
        {
            ImGui.SameLine();
            if (ImGui.Button("Edit", new Vector2(40, 0) * T3Ui.UiScaleFactor))
            {
                if (!File.Exists(value))
                {
                    Log.Error("Can't open non-existing file " + value);
                }
                else
                {
                    CoreUi.Instance.OpenWithDefaultApplication(value);
                }
            }
        }

        return modified;
    }

    private static string ConvertToRelativeFilepath(string absoluteFilePath)
    {
        var currentApplicationPath = Path.GetFullPath(".");
        var firstCharUppercase = currentApplicationPath.Substring(0, 1).ToUpper();
        currentApplicationPath = firstCharUppercase + currentApplicationPath.Substring(1, currentApplicationPath.Length - 1) + "\\";
        var relativeFilePath = absoluteFilePath.Replace(currentApplicationPath, "");
        return relativeFilePath;
    }

    private static string GetAbsoluteResourcePath()
    {
        return Path.Combine(Path.GetFullPath("."), FileLocations.ResourcesSubfolder);
    }

    private static string GetAbsoluteDirectory(string relativeFilepath)
    {
        var absolutePath = GetAbsoluteResourcePath();
        return Path.GetDirectoryName(Path.Combine(absolutePath, relativeFilepath.Replace(FileLocations.ResourcesSubfolder + "\\", "")));
    }
}