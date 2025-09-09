using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.UserData;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Serialization;

namespace T3.Editor.Gui.Windows.Output;

internal static class ResolutionHandling
{
    public static void DrawSelector(ref Resolution selectedResolution, EditResolutionDialog resolutionDialog)
    {
        if (resolutionDialog != null && resolutionDialog.Draw(_resolutionForEdit))
        {
            Save();
        }

        ImGui.SetNextItemWidth(100*T3Ui.UiScaleFactor);
        if (ImGui.BeginCombo("##ResolutionSelection", selectedResolution.Title, ImGuiComboFlags.HeightLargest))
        {
            foreach (var resolution in Resolutions.ToArray())
            {
                ImGui.PushID(resolution.Title);
                if (ImGui.Selectable(resolution.Title, resolution == selectedResolution))
                {
                    selectedResolution = resolution;
                }

                CustomComponents.ContextMenuForItem(() =>
                                                    {
                                                        if (ImGui.MenuItem("Remove"))
                                                        {
                                                            _resolutions.Remove(resolution);
                                                            Save();
                                                        }
                                                    },
                                                    "##bla");
                ImGui.PopID();
            }

            if (ImGui.Selectable("+ Add"))
            {
                _resolutionForEdit = new Resolution("untitled", 256, 256);
                _resolutions.Add(_resolutionForEdit);
                resolutionDialog?.ShowNextFrame();
            }

            ImGui.EndCombo();
        }
        else
        {
            CustomComponents.TooltipForLastItem("Adjust requested output resolution", "This can either be an aspect ratio or a fixed resolution. This is be used by all Image operators if their resolution is set to 0 or -1. Please read documentation for more details.");
        }
    }
        
    public static void Save()
    {
        JsonUtils.TrySaveJson(_resolutions, _filePath);    
    }
        
    public static List<Resolution> Resolutions => _resolutions
                                                      ??= JsonUtils.TryLoadingJson<List<Resolution>>(_filePath)
                                                          ??  new()
                                                                  {
                                                                      new("Fill", 0, 0, useAsAspectRatio: true),
                                                                      new("1:1", 1, 1, useAsAspectRatio: true),
                                                                      new("16:9", 16, 9, useAsAspectRatio: true),
                                                                      new("4:3", 4, 3, useAsAspectRatio: true),
                                                                      new("480p", 850, 480),
                                                                      new("720p", 1280, 720),
                                                                      new("1080p", 1920, 1080),
                                                                      new("4k", 1920 * 2, 1080 * 2),
                                                                      new("8k", 1920 * 4, 1080 * 4),
                                                                      new("4k Portrait", 1080 * 2, 1920 * 2),
                                                                  };
    
    private static List<Resolution> _resolutions;
    private static readonly string _filePath = System.IO.Path.Combine(FileLocations.SettingsDirectory, "resolutions.json");
    public static readonly Resolution DefaultResolution = Resolutions[0];
    private static Resolution _resolutionForEdit = new("untitled", 256, 256);

    public sealed class Resolution
    {
        public Resolution(string title, int width, int height, bool useAsAspectRatio = false)
        {
            Title = title;
            Size.Width = width;
            Size.Height = height;
            UseAsAspectRatio = useAsAspectRatio;
        }

        public string Title;
        public Int2 Size;
        public bool UseAsAspectRatio;

        public Int2 ComputeResolution()
        {
            if (!UseAsAspectRatio)
                return Size;

            var windowSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();

            var paddingForFocusBorder = UserSettings.Config.FocusMode ? 0 : 1;
            
            if (Size.Width <= 0 || Size.Height <= 0)
            {
                return new Int2((int)windowSize.X - paddingForFocusBorder * 2,
                                (int)windowSize.Y - paddingForFocusBorder * 2);
            }

            var windowAspectRatio = windowSize.X / windowSize.Y;
            var requestedAspectRatio = (float)Size.Width / Size.Height;

            return (requestedAspectRatio > windowAspectRatio)
                       ? new Int2((int)windowSize.X, (int)(windowSize.X / requestedAspectRatio))
                       : new Int2((int)(windowSize.Y * requestedAspectRatio), (int)windowSize.Y);
        }

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(Title)
                       && !Resolutions.Any(res => res != this && res.Title == Title)
                       && Size.Width > 0 && Size.Width < 16384
                       && Size.Height > 0 && Size.Height < 16384;
            }
        }

    }
}