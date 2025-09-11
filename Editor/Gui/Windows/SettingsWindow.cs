using System.IO;
using ImGuiNET;
using Operators.Utils;
using T3.Core.IO;
using T3.Core.UserData;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Windows;

internal sealed class SettingsWindow : Window
{
    internal SettingsWindow()
    {
        Config.Title = "Settings";
    }

    private enum Categories
    {
        Interface,
        Theme,
        Project,
        Midi,
        OSC,
        SpaceMouse,
        Keyboard,
        Profiling,
    }

    private Categories _activeCategory;

    protected override void DrawContent()
    {
        var changed = false;

        ImGui.BeginChild("categories", new Vector2(120 * T3Ui.UiScaleFactor, -1), true, ImGuiWindowFlags.NoScrollbar);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
            FormInputs.AddSegmentedButtonWithLabel(ref _activeCategory, "", 110 * T3Ui.UiScaleFactor);
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 5));
        ImGui.BeginChild("content", new Vector2(0, 0), true);
        {
            FormInputs.SetIndentToParameters();
            switch (_activeCategory)
            {
                case Categories.Interface:
                    FormInputs.SetIndentToLeft();
                    FormInputs.AddSectionHeader("User Interface");

                    FormInputs.AddVerticalSpace();
                    FormInputs.SetIndentToParameters();
                    changed |= FormInputs.AddFloat("UI Scale",
                                                   ref UserSettings.Config.UiScaleFactor,
                                                   0.1f, 5f, 0.01f, true, true,
                                                   "The global scale of all rendered UI in the application",
                                                   UserSettings.Defaults.UiScaleFactor);

                    changed |= FormInputs.AddEnumDropdown(ref UserSettings.Config.ValueEditMethod,
                                                          "Value input method",
                                                          "The control that pops up when dragging on a number value"
                                                         );
                    
                    
                    changed |= FormInputs.AddInt("Value input smoothing", 
                                                 ref UserSettings.Config.ValueEditSmoothing, 
                                                 0, 20, 0.1f,
                                                 """
                                                 Smoothes the result of value edit controllers. 
                                                 This introduces a delay but might look more conformable to the audience
                                                 of a live performance.
                                                 """,
                                                 UserSettings.Defaults.ValueEditSmoothing);
                    FormInputs.AddVerticalSpace();
                    FormInputs.SetIndentToParameters();
                    FormInputs.AddSectionSubHeader("Graph style");

                    changed |= FormInputs.AddEnumDropdown(ref UserSettings.Config.GraphStyle,
                                                          "Graph Style",
                                                          """
                                                          Allows to switch between different graphical representations.
                                                          This also will affect usability and performance
                                                          """, UserSettings.Defaults.GraphStyle
                                                         );
                    

                    
                    if (UserSettings.Config.GraphStyle == UserSettings.GraphStyles.Magnetic)
                    {
                        // changed |= FormInputs.AddCheckBox("Disconnect on unsnap",
                        //                                   ref UserSettings.Config.DisconnectOnUnsnap,
                        //                                   """
                        //                                   Defines if unsnapping operators from a block will automatically disconnect them.
                        //                                   Ops dragged out between snapped blocks will always be disconnected.
                        //                                   """,
                        //                                   UserSettings.Defaults.DisconnectOnUnsnap);

                        changed |= FormInputs.AddCheckBox("Snap horizontally",
                                                          ref UserSettings.Config.EnableHorizontalSnapping,
                                                          """
                                                          Snap horizontally to ops above or below. 
                                                          This can be useful because connections of vertically aligned operators will avoid overlapping.  
                                                          """,
                                                          UserSettings.Defaults.EnableHorizontalSnapping);
                        
                        changed |= FormInputs.AddFloat("Connection radius",
                                                       ref UserSettings.Config.MaxCurveRadius,
                                                       0.0f, 1000f, 1f, true, true, 
                                                       "Controls the roundness of curve lines",
                                                       UserSettings.Defaults.MaxCurveRadius);
                        changed |= FormInputs.AddInt("Connection segments",
                                                       ref UserSettings.Config.MaxSegmentCount, 1, 100, 1f,
                                                       "Controls the number of segments used to draw connections between operators.", UserSettings.Defaults.MaxSegmentCount);
                        

                    }
                    else
                    {
                        changed |= FormInputs.AddCheckBox("Use arc connections",
                                                          ref UserSettings.Config.UseArcConnections,
                                                          "Affects the shape of the connections between your operators",
                                                          UserSettings.Defaults.UseArcConnections);

                        changed |= FormInputs.AddCheckBox("Drag snapped nodes",
                                                          ref UserSettings.Config.SmartGroupDragging,
                                                          "An experimental features that will drag neighbouring snapped operators",
                                                          UserSettings.Defaults.SmartGroupDragging);

                        changed |= FormInputs.AddCheckBox("Show Graph thumbnails",
                                                          ref UserSettings.Config.ShowThumbnails, null,
                                                          UserSettings.Defaults.ShowThumbnails);

                        changed |= FormInputs.AddCheckBox("Show nodes thumbnails when hovering",
                                                          ref UserSettings.Config.EditorHoverPreview, null,
                                                          UserSettings.Defaults.EditorHoverPreview);
                    }

                    FormInputs.AddVerticalSpace();

                    changed |= FormInputs.AddFloat("Scroll smoothing",
                                                   ref UserSettings.Config.ScrollSmoothing,
                                                   0.0f, 0.2f, 0.01f, true, true,
                                                   null,
                                                   UserSettings.Defaults.ScrollSmoothing);

                    changed |= FormInputs.AddFloat("Click threshold",
                                                   ref UserSettings.Config.ClickThreshold,
                                                   0.0f, 10f, 0.1f, true, true,
                                                   "The threshold in pixels until a click becomes a drag. Adjusting this might be useful for stylus input",
                                                   UserSettings.Defaults.ClickThreshold);

                    changed |= FormInputs.AddFloat("Gizmo size",
                                                   ref UserSettings.Config.GizmoSize,
                                                   0.0f, 200f, 0.01f, true, true, "Size of the transform gizmo in 3d views",
                                                   UserSettings.Defaults.GizmoSize);

                    
                    changed |= FormInputs.AddCheckBox("Enable keyboard shortcut",
                                                      ref UserSettings.Config.EnableKeyboardShortCuts,
                                                      "This might prevent unintended user interactions while live performing with [KeyInput] operators.",
                                                      UserSettings.Defaults.EnableKeyboardShortCuts);
                    
                    changed |= FormInputs.AddCheckBox("Readable parameter names",
                                                      ref UserSettings.Config.AddSpacesToParameterNames,
                                                      """
                                                      Insert spaces to parameters names. E.g. prints "XAxisValue" becomes "X Axis Value".
                                                      This is purely for output formatting. If you're developing, and used to PascalCase you might not need it.
                                                      """,
                                                      UserSettings.Defaults.AddSpacesToParameterNames);                    
                    
                    FormInputs.AddVerticalSpace();
                    FormInputs.AddSectionSubHeader("Timeline");

                    changed |= FormInputs.AddFloat("Grid density",
                                                   ref UserSettings.Config.TimeRasterDensity,
                                                   0.0f, 10f, 0.01f, true, true,
                                                   "Density/opacity of the marks (time or beat) at the bottom of the timeline",
                                                   UserSettings.Defaults.TimeRasterDensity);

                    changed |= FormInputs.AddFloat("Snap strength",
                                                   ref UserSettings.Config.SnapStrength,
                                                   0.0f, 0.2f, 0.01f, true, true,
                                                   "Controls the distance until items such as keyframes snap in the timeline",
                                                   UserSettings.Defaults.SnapStrength);
                    
                    changed |= FormInputs.AddFloat("Audio Volume",
                                                   ref ProjectSettings.Config.PlaybackVolume,
                                                   0.0f, 10f, 0.01f, true, true,
                                                   "Limit the audio playback volume",
                                                   ProjectSettings.Defaults.PlaybackVolume);

                    changed |= FormInputs.AddEnumDropdown(ref UserSettings.Config.FrameStepAmount,
                                                          "Frame step amount",
                                                          "Controls the next rounding and step amount when jumping between frames.\nDefault shortcut is Shift+Cursor Left/Right"
                                                        , UserSettings.Defaults.FrameStepAmount);

                    changed |= FormInputs.AddCheckBox("Reset time after playback",
                                                      ref UserSettings.Config.ResetTimeAfterPlayback,
                                                      "After the playback is halted, the time will reset to the moment when the playback began. This feature proves beneficial for iteratively reviewing animations without requiring manual rewinding.",
                                                      UserSettings.Defaults.ResetTimeAfterPlayback);

                    FormInputs.SetIndentToLeft();
                    FormInputs.AddVerticalSpace();

                    FormInputs.AddVerticalSpace();
                    FormInputs.AddSectionSubHeader("Advanced options");

                    changed |= FormInputs.AddCheckBox("Editing values with mousewheel needs CTRL key",
                                                      ref UserSettings.Config.MouseWheelEditsNeedCtrlKey,
                                                      "In parameter window you can edit numeric values by using the mouse wheel. This setting will prevent accidental modifications while scrolling because by using ctrl key for activation.",
                                                      UserSettings.Defaults.MouseWheelEditsNeedCtrlKey);

                    changed |= FormInputs.AddCheckBox("Mousewheel adjust flight speed",
                                                      ref UserSettings.Config.AdjustCameraSpeedWithMouseWheel,
                                                      "If enabled, scrolling the mouse wheel while holding left of right mouse button will control navigation speed with WASD keys. This is similar to Unity and Unreal",
                                                      UserSettings.Defaults.AdjustCameraSpeedWithMouseWheel);

                    changed |= FormInputs.AddCheckBox("Mirror UI on second view",
                                                      ref UserSettings.Config.MirrorUiOnSecondView,
                                                      "On Windows mirroring displays can be extremely slow. This settings is will copy the UI to the second view instead of mirroring it.",
                                                      UserSettings.Defaults.MirrorUiOnSecondView);

                    changed |= FormInputs.AddCheckBox("Balance soundtrack visualizer",
                                                      ref UserSettings.Config.ExpandSpectrumVisualizerVertically,
                                                      "If true, changes the visualized pitch's logarithmic scale from base 'e' to base 10.\nLower frequencies will become more visible, making the frequency spectrum\n appear more \"balanced\"",
                                                      UserSettings.Defaults.ExpandSpectrumVisualizerVertically);
                    FormInputs.AddVerticalSpace();
                    changed |= FormInputs.AddCheckBox("Middle mouse button zooms canvas",
                                                      ref UserSettings.Config.MiddleMouseButtonZooms,
                                                      "This can be useful if you're working with tablets or other input devices that lack a mouse wheel.",
                                                      UserSettings.Defaults.MiddleMouseButtonZooms);

                    changed |= FormInputs.AddCheckBox("Suspend invalidation of inactive time clips",
                                                      ref ProjectSettings.Config.TimeClipSuspending,
                                                      "An experimental optimization that avoids dirty flag evaluation of graph behind inactive TimeClips. This is only relevant for very complex projects and multiple parts separated by timelines.",
                                                      ProjectSettings.Defaults.TimeClipSuspending);

                    changed |= FormInputs.AddCheckBox("Warn before Lib modifications",
                                                      ref UserSettings.Config.WarnBeforeLibEdit,
                                                      "This warning pops up when you attempt to enter an Operator that ships with the application.\n" +
                                                      "If unsure, this is best left checked.",
                                                      UserSettings.Defaults.WarnBeforeLibEdit);

                    changed |= FormInputs.AddCheckBox("Suspend rendering when hidden",
                                                      ref UserSettings.Config.SuspendRenderingWhenHidden,
                                                      "Suspend rendering and update when Tooll's editor window is minimized. This will reduce energy consumption significantly.",
                                                      UserSettings.Defaults.SuspendRenderingWhenHidden);

                    break;
                case Categories.Theme:
                    FormInputs.AddSectionHeader("Color Theme");
                    FormInputs.AddVerticalSpace();

                    ColorThemeEditor.DrawEditor();
                    break;

                case Categories.Project:
                {
                    var projectSettingsChanged = false;
                    FormInputs.AddSectionHeader("Project specific settings");
                    FormInputs.AddVerticalSpace();

                    FormInputs.AddSectionSubHeader("Project Settings");
                    changed |= FormInputs.AddStringInput("Project Directory",
                                                         ref UserSettings.Config.ProjectsFolder,
                                                         "Folder",
                                                         Directory.Exists(UserSettings.Config.ProjectsFolder) ? null : "Folder does not exists",
                                                         """
                                                         A writable directory for your projects.
                                                         Changing it will require a restart!
                                                         """,
                                                         FileLocations.DefaultProjectFolder);
                    
                    FormInputs.AddVerticalSpace();
                    changed |= FormInputs.AddStringInput("UserName",
                                                         ref UserSettings.Config.UserName,
                                                         "Nickname",
                                                          GraphUtils.IsValidProjectName(UserSettings.Config.UserName)? null :"Must not contain spaces or special characters",
                                                           """
                                                           Enter your nickname to group your projects into a namespace.
                                                           Your nickname should be short and not contain spaces or special characters.
                                                           """,
                                                           Environment.UserName.ToValidClassName("Unknown"));                    
                    FormInputs.SetIndentToLeft();
                    
                    changed |= FormInputs.AddCheckBox("Enable Backup",
                                                      ref UserSettings.Config.EnableAutoBackup,
                                                      $"""
                                                       Save backups of your projects every {AutoBackup.AutoBackup.SecondsBetweenSaves / 60} min. Backup files will be thinned out so fewer backups are kept the older they are.
                                                       The total number of files stored will not exceed 40.
                                                       Files exceed 100mb will not be archived.

                                                       They are saved as zip-archives to {AutoBackup.AutoBackup.BackupDirectory}.
                                                       """,
                                                      UserSettings.Defaults.EnableAutoBackup);
                    
                    FormInputs.AddSectionSubHeader("Performance Settings");
                    FormInputs.SetIndentToLeft();

                    projectSettingsChanged |= FormInputs.AddCheckBox("Skip Shader Optimization",
                                                                     ref ProjectSettings.Config.SkipOptimization,
                                                                     "This make working with shader graphs easier.",
                                                                     ProjectSettings.Config.SkipOptimization);
                    
                    projectSettingsChanged |= FormInputs.AddCheckBox("Enable DirectX Debug Mode",
                                                                     ref ProjectSettings.Config.EnableDirectXDebug,
                                                                     """
                                                                     This will add debug information for to shaders and buffers that can help developing wiht Tools like RenderDoc.
                                                                     Enabling this can impact rendering performance.

                                                                     Changing this option requires a restart.
                                                                     """,
                                                                     ProjectSettings.Config.EnableDirectXDebug);
                    
                    FormInputs.AddSectionSubHeader("Audio Sync");
                    
                    FormInputs.SetIndentToParameters();


                    FormInputs.AddVerticalSpace();
                    
                    FormInputs.AddSectionSubHeader("Export Settings");
                    CustomComponents.HelpText("These settings only when playback as executable");
                    FormInputs.AddVerticalSpace();

                    projectSettingsChanged |= FormInputs.AddEnumDropdown(ref ProjectSettings.Config.DefaultWindowMode,
                                                                         "Show export as",
                                                                         "The default window mode when exporting an executable.",
                                                                         WindowMode.Fullscreen);

                    projectSettingsChanged |= FormInputs.AddCheckBox("Enable Playback Control",
                                                                     ref ProjectSettings.Config.EnablePlaybackControlWithKeyboard,
                                                                     "Users can use cursor left/right to skip through time\nand space key to pause playback\nof exported executable.",
                                                                     ProjectSettings.Defaults.EnablePlaybackControlWithKeyboard);



                    if (projectSettingsChanged)
                        ProjectSettings.Save();

                    FormInputs.SetIndentToParameters();

                    break;
                }
                case Categories.Midi:
                {
                    FormInputs.AddSectionHeader("Midi");

                    if (ImGui.Button("Rescan devices"))
                    {
                        MidiConnectionManager.Rescan();
                        //MidiOutConnectionManager.Init();
                        CompatibleMidiDeviceHandling.InitializeConnectedDevices();
                    }

                    {
                        FormInputs.AddVerticalSpace();
                        ImGui.TextUnformatted("Limit captured MIDI devices...");
                        CustomComponents
                           .HelpText("This can be useful it avoid capturing devices required by other applications.\nEnter one search string per line...");

                        var limitMidiDevices = string.IsNullOrEmpty(ProjectSettings.Config.LimitMidiDeviceCapture)
                                                   ? string.Empty
                                                   : ProjectSettings.Config.LimitMidiDeviceCapture;

                        if (ImGui.InputTextMultiline("##Limit MidiDevices", ref limitMidiDevices, 2000, new Vector2(-1, 100)))
                        {
                            changed = true;
                            ProjectSettings.Config.LimitMidiDeviceCapture = string.IsNullOrEmpty(limitMidiDevices) ? null : limitMidiDevices;
                            MidiConnectionManager.Rescan();
                        }

                        FormInputs.AddVerticalSpace();
                    }

                    FormInputs.AddVerticalSpace();
                    break;
                }
                case Categories.OSC:
                {
                    FormInputs.AddSectionHeader("OSC");

                    CustomComponents
                       .HelpText("On startup, Tooll will listen for OSC messages on the default port." +
                                 "The IO indicator in the timeline will show incoming messages.\n" +
                                 "You can also use the OscInput operator to receive OSC from other ports.");

                    CustomComponents
                       .HelpText("Changing the port will require a restart of Tooll.");

                    FormInputs.AddInt("Default Port", ref ProjectSettings.Config.DefaultOscPort,
                                      0, 65535, 1,
                                      "If a valid port is set, Tooll will listen for OSC messages on this port by default.",
                                      -1);

                    FormInputs.AddVerticalSpace();
                    break;
                }

                case Categories.SpaceMouse:
                    FormInputs.AddSectionHeader("Space Mouse");

                    CustomComponents.HelpText("These settings only apply with a connected space mouse controller");
                    FormInputs.AddVerticalSpace();

                    changed |= FormInputs.AddFloat("Smoothing",
                                                   ref UserSettings.Config.SpaceMouseDamping,
                                                   0.0f, 10f, 0.01f, true, true);

                    changed |= FormInputs.AddFloat("Move Speed",
                                                   ref UserSettings.Config.SpaceMouseMoveSpeedFactor,
                                                   0.0f, 10f, 0.01f, true, true);

                    changed |= FormInputs.AddFloat("Rotation Speed",
                                                   ref UserSettings.Config.SpaceMouseRotationSpeedFactor,
                                                   0.0f, 10f, 0.01f, true, true);
                    break;

                case Categories.Keyboard:
                    FormInputs.AddSectionHeader("Keyboard Shortcuts");
                    CustomComponents.HelpText("The keyboard layout can't be edited yet. Working on it");

                    KeyMapEditor.DrawEditor();

                    break;
                
                case Categories.Profiling:
                {
                    FormInputs.AddSectionHeader("Profiling and debugging");

                    CustomComponents.HelpText("Enabling this will add slight performance overhead.\nChanges will require a restart of Tooll.");
                    FormInputs.AddVerticalSpace();

                    FormInputs.SetIndentToParameters();
                    FormInputs.AddSectionSubHeader("Log events");
                    FormInputs.SetIndentToLeft();
                    changed |= FormInputs.AddCheckBox("Enable Frame Profiling",
                                                      ref UserSettings.Config.EnableFrameProfiling,
                                                      "A basic frame profile for the duration of frame processing. Overhead is minimal.",
                                                      UserSettings.Defaults.EnableFrameProfiling);
                    
                    changed |= FormInputs.AddCheckBox("Keep Log Messages",
                                                      ref UserSettings.Config.KeepTraceForLogMessages,
                                                      "Store log messages in the profiling data. This can be useful to see correlation between frame drops and log messages.",
                                                      UserSettings.Defaults.KeepTraceForLogMessages);

                    changed |= FormInputs.AddCheckBox("Log GC Profiling",
                                                      ref UserSettings.Config.EnableGCProfiling,
                                                      "Log garbage collection information. This can be useful to see correlation between frame drops and GC activity.",
                                                      UserSettings.Defaults.EnableGCProfiling);
                    
                    changed |= FormInputs.AddCheckBox("Profile Beat Syncing",
                                                      ref ProjectSettings.Config.EnableBeatSyncProfiling,
                                                      "Logs beat sync timing to IO Window",
                                                      ProjectSettings.Defaults.EnableBeatSyncProfiling);

                    FormInputs.AddSectionSubHeader("Compilation");
                    
                    // Compilation details
                    {
                        
                        changed |= FormInputs.AddCheckBox("Log Assembly Version mismatches",
                                                          ref ProjectSettings.Config.LogAssemblyVersionMismatches,
                                                          """
                                                          Version mismatches are frequently caused by slightly outdated 3rd party library that we depend on.
                                                          These are only relevant in situations where you need to debug or analyse assembly loading problems. 
                                                          """,
                                                          ProjectSettings.Defaults.LogAssemblyVersionMismatches);                        
                        
                        changed |= FormInputs.AddCheckBox("Log Loading Details",
                                                          ref ProjectSettings.Config.LogAssemblyLoadingDetails,
                                                          """
                                                          Logs additional details about resolving and identifying assemblies and other resources.
                                                          This can be useful to debug issues related to loading projects.
                                                          """,
                                                          ProjectSettings.Defaults.LogAssemblyLoadingDetails);
                        
                        changed |= FormInputs.AddCheckBox("Log C# Compilation Details",
                                                          ref ProjectSettings.Config.LogCompilationDetails,
                                                          "Logs additional compilation details with the given severity",
                                                          ProjectSettings.Defaults.LogCompilationDetails);
                        
             

                        if (ProjectSettings.Config.LogCompilationDetails)
                        {
                            FormInputs.SetIndentToParameters();
                            changed |= FormInputs.AddEnumDropdown(ref UserSettings.Config.CompileCsVerbosity,
                                                                  "C# compiler logs",
                                                                  null,
                                                                  UserSettings.Defaults.CompileCsVerbosity
                                                                 );
                        }

                    }
                    FormInputs.AddVerticalSpace();
                    FormInputs.SetIndentToLeft();
                    
                    changed |= FormInputs.AddCheckBox("Show Operator status indicators",
                                                      ref UserSettings.Config.ShowOperatorStats,
                                                      """
                                                      Draws an context overlay with various operator stats. 
                                                      """,
                                                      UserSettings.Defaults.ShowOperatorStats);

                    FormInputs.AddVerticalSpace();
                    

                    

                    break;
                }
            }

            if (changed)
                UserSettings.Save();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }
}