using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Core.Settings;
using T3.Editor.App;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.ProjectHandling;
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
            CustomComponents.MenuGroupHeader("Output Resolution");
            for (var index = 0; index < Resolutions.ToArray().Length; index++)
            {
                var resolution = Resolutions[index];
                ImGui.PushID(resolution.Title);
                if (CustomComponents.DrawMenuItem(index, resolution.Title, isChecked:resolution == selectedResolution))
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

            CustomComponents.SeparatorLine();
            if(CustomComponents.DrawMenuItem(666, "Add"))
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

    public static Resolution? FindByTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        for (var i = 0; i < Resolutions.Count; i++)
        {
            if (Resolutions[i].Title == title)
                return Resolutions[i];
        }

        return null;
    }

    /// <summary>
    /// The active setup's outputs and their present-on-display bindings, hung in the output window's
    /// breadcrumb menu. Binding a projector/display presents it fullscreen; the output manager then
    /// composites and drives it. This lives here (not in the resolution selector) — an output target
    /// is not a view resolution.
    /// </summary>
    public static void DrawOutputBindingMenu()
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        if (!ImGui.BeginMenu("Output Binding"))
            return;

        if (setup.Outputs.Count == 0)
            ImGui.TextDisabled("No outputs in this setup");

        for (var index = 0; index < setup.Outputs.Count; index++)
        {
            var output = setup.Outputs[index];
            var binding = machineConfig.TryGetBinding(output.Id);
            var isBindable = output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
            var label = binding == null
                            ? $"{output.Name}  ·  {output.CanvasResolution.Width}×{output.CanvasResolution.Height}"
                            : $"{output.Name}  →  Display {binding.DisplayIndex + 1}";

            ImGui.PushID(output.Id.GetHashCode());
            if (isBindable)
            {
                if (ImGui.BeginMenu(label))
                {
                    DrawBindingMenuItems(output, machineConfig);
                    ImGui.EndMenu();
                }
            }
            else
            {
                // Default/format outputs are render targets, not something you present to a display.
                ImGui.MenuItem(label, false);
            }

            ImGui.PopID();
        }

        ImGui.EndMenu();
    }

    private static void DrawBindingMenuItems(OutputDefinition output, MachineConfig machineConfig)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var binding = machineConfig.TryGetBinding(output.Id);
        for (var screenIndex = 0; screenIndex < screens.Length; screenIndex++)
        {
            var screen = screens[screenIndex];
            var isBound = binding != null && binding.DisplayIndex == screenIndex;
            var label = $"Fullscreen on Display {screenIndex + 1} ({screen.Bounds.Width}×{screen.Bounds.Height})";
            if (CustomComponents.DrawMenuItem(screenIndex, label, isChecked: isBound))
            {
                machineConfig.Bind(new DeviceBinding
                                       {
                                           OutputId = output.Id,
                                           DisplayName = screen.DeviceName,
                                           DisplayIndex = screenIndex,
                                       });
                OutputSetupHandling.SaveActive();
                PresentOnDisplay(screenIndex, output.Id);
            }
        }

        if (binding != null)
        {
            CustomComponents.SeparatorLine();
            if (CustomComponents.DrawMenuItem(999, "Stop presenting"))
            {
                machineConfig.Unbind(output.Id);
                OutputSetupHandling.SaveActive();
                WindowManager.ShowSecondaryRenderWindow = false;
                if (OutputManager.PresentedOutputId == output.Id)
                    OutputManager.PresentedOutputId = Guid.Empty;
            }
        }
    }

    private static void PresentOnDisplay(int screenIndex, Guid outputId)
    {
        OutputManager.PresentedOutputId = outputId;
        WindowManager.ShowSecondaryRenderWindow = true;
        ProgramWindows.Viewer.SetFullScreen(screenIndex);
    }

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

            var windowSize = ImGui.GetWindowSize();

            var paddingForFocusBorder = LayoutHandling.FocusMode ? 0 : 1;
            
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