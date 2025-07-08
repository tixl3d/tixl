using System.IO;
using T3.Core.DataTypes;
using T3.Core.UserData;
using T3.Serialization;

namespace T3.Editor.Gui.UiHelpers;

internal static class GradientPresets
{
    internal static List<Gradient> Presets
    {
        get
        {
            if(_presets != null)
                return _presets;

            _presets = UserData.TryLoadOrInitializeUserData<List<Gradient>>(FileName, out var presets) 
                           ? presets 
                           : [];
            
            return _presets;
        }
    }

    internal static void Save()
    {
        JsonUtils.TrySaveJson(_presets, FilePath);    
    }
    
    private static List<Gradient> _presets;

    private static string FilePath => Path.Combine(FileLocations.SettingsDirectory, FileName);
    private const string FileName = "gradients.json";
}