#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.App;
using System.Windows.Forms;
using T3.Core.Resource;
using System.Reflection;
using T3.Editor.Gui.Input;

namespace T3.Editor.Gui.Dialog;

internal sealed class AboutDialog : ModalDialog
{
    internal void Draw()
    {
        DialogSize = new Vector2(550, 550);

        if (BeginDialog("    TiXL Loves You! <3"))
        {
            var mousepos = ImGui.GetMousePos(); // Get the current mouse position
            var normalizedMouseX = Math.Clamp(mousepos.X / ImGui.GetIO().DisplaySize.X, .33f, 1f);
            var normalizedMouseY = Math.Clamp(mousepos.Y / ImGui.GetIO().DisplaySize.Y, .33f, 1f);// Normalize X to range [.33, 1]
            var rectColor = new Vector4(normalizedMouseX -0.1f, normalizedMouseY -.127f, 0.620f,10+ 1f); // Use normalizedMouseX for r normalizedMouseY for the g channel
            var rectSize = new Vector2(64f,64f);
    
                ImGui.GetWindowDrawList().AddRectFilled(
                ImGui.GetCursorScreenPos(),
                ImGui.GetCursorScreenPos() + (rectSize),
                ImGui.ColorConvertFloat4ToU32(rectColor)
            );
            
            ImGui.Image((IntPtr)SharedResources.t3logoAlphaTextureImageSrv, new Vector2(64, 64));

            FormInputs.AddSectionHeader("TiXL");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

            FormInputs.AddSectionHeader(Program.FormattedEditorVersion);
            ImGui.PopStyleColor();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, mySpacing);
           
            ImGui.TextColored(UiColors.TextMuted, $"Build Hash:"); // write git commit hash
            ImGui.SameLine();
            ImGui.Text($"{gitCommitHash}");
            
            ImGui.TextColored(UiColors.TextMuted, $"Date:");
            ImGui.SameLine();
            ImGui.Text($"{dateTime}");
          
#if DEBUG
            ImGui.TextColored(UiColors.TextMuted, "IDE:");
            ImGui.SameLine();
            ImGui.Text($"{ideName}");
#endif

            ImGui.TextColored(UiColors.TextMuted, "App language:");
            ImGui.SameLine();
            ImGui.Text($"{appLanguage}");
            ImGui.PopStyleVar();
            FormInputs.AddVerticalSpace(1);
            ImGui.Separator();
            
            FormInputs.AddSectionHeader("System Information");
            
            //FormInputs.AddVerticalSpace(0);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, mySpacing);
            ImGui.TextColored(UiColors.TextMuted, "OS:" );
            ImGui.SameLine();
            ImGui.Text($"{operatingSystemInfo}");
            ImGui.TextColored(UiColors.TextMuted, "System language:");
            ImGui.SameLine();
            ImGui.Text($"{systemLanguage}");
            ImGui.TextColored(UiColors.TextMuted, "Keyboard layout:");
            ImGui.SameLine();
            ImGui.Text($"{keyboardLayout}");
            
            FormInputs.AddVerticalSpace(8);
            
            ImGui.TextColored(UiColors.TextMuted, ".NET Runtime:");
            ImGui.SameLine();
            ImGui.Text($"{dotNetRuntime}");
            ImGui.TextColored(UiColors.TextMuted, ".NET SDK:");
            ImGui.SameLine();
            ImGui.Text($"{dotNetSdk}");

            FormInputs.AddVerticalSpace(8);

            ImGui.TextColored(UiColors.TextMuted, "Graphics processing unit(s):"); 
            ImGui.Text($"{gpuInformation}");
            FormInputs.AddVerticalSpace(8);
            ImGui.Separator();
            ImGui.PopStyleVar();

            FormInputs.AddVerticalSpace(5);
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
            if (ImGui.Button("Copy System Information"))
            {
                UpdateSystemInfo(); // Update system info and copy to clipboard
                ImGui.SetClipboardText(_systemInfo);
                ImGui.OpenPopup("SystemInfoCopied");
            }
            ImGui.PopStyleColor(2);
            // Confirmation popup
            if (ImGui.BeginPopup("SystemInfoCopied"))
            {
                ImGui.Text("System information copied to clipboard!");
                ImGui.EndPopup();
            }

            CustomComponents.TooltipForLastItem("Copy system info for bug reports");
            EndDialogContent();
           
           
        }
        EndDialog();
    }
    private static string GetGitCommitHash()
    {
        try
        {
            var assemblyLocation = Assembly.GetEntryAssembly()!.Location;
            var productVersion = FileVersionInfo.GetVersionInfo(assemblyLocation)?.ProductVersion;

            if (productVersion is null || !productVersion.Contains("+"))
                return "Unknown";

            var commitHash = productVersion.Split("+")[1];
            return commitHash.Substring(0, 8);
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }
    private void UpdateSystemInfo()
    {
        try
        {
            var systemInfo = new StringBuilder();

            systemInfo.AppendLine($"Date: {dateTime}");
            systemInfo.AppendLine($"TiXL version: {Program.VersionText}");
            systemInfo.AppendLine($"Build Hash: {GetGitCommitHash()}"); //get commit hash from git

#if DEBUG
            systemInfo.AppendLine($"IDE: {GetIdeName()}");
#endif
            systemInfo.AppendLine($"App language: {GetAppLanguage()}");
            systemInfo.AppendLine($"OS: {GetOperatingSystemInfo()}");
            systemInfo.AppendLine($"System language: {GetSystemLanguage()}");
            systemInfo.AppendLine($"Keyboard Layout: {GetKeyboardLayout()}");
            systemInfo.AppendLine($".NET runtime: {GetDotNetRuntimeVersion()}");
            systemInfo.AppendLine($".NET SDK: {GetDotNetSdkVersion()}");
            systemInfo.AppendLine($"GPU: {GetGpuInformation()}");
            
            _systemInfo = systemInfo.ToString();
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to gather system information: {e.Message}");
            _systemInfo = "Failed to gather system information.";
        }
    }

    private static string GetOperatingSystemInfo()
    {
        var osDescription = RuntimeInformation.OSDescription;
        var osArchitecture = RuntimeInformation.OSArchitecture.ToString();

        return $"{osDescription} ({osArchitecture})";
    }

    private static string GetSystemLanguage()
    {
        try
        {
            var currentCulture = CultureInfo.CurrentUICulture;
            return currentCulture.EnglishName;
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    private static string GetKeyboardLayout()
    {
        try
        {
            var currentInputLanguage = InputLanguage.CurrentInputLanguage;

            return $"{currentInputLanguage.Culture.Name} {currentInputLanguage.LayoutName} ";
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    private static string GetAppLanguage()
    {
        try
        {
            var appCulture = CultureInfo.CurrentCulture;
            return $"{appCulture.EnglishName} ";
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    private static string GetDotNetRuntimeVersion()
    {
        return RuntimeInformation.FrameworkDescription;
    }

    private static string GetDotNetSdkVersion()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = "--version";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch (Exception)
        {
            // Silently fail if we can't get the SDK version
        }

        return "Not found";
    }

    private static string GetGpuInformation(string infoType = "both")
    {
        var gpuList = new List<string>();
        var activeGpu = ProgramWindows.ActiveGpu;

        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    
                    var name = obj["Name"]?.ToString() ?? "Unknown";
                    if (name == activeGpu)
                        name += " (Active)";
                    
                    var driverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown";

                    string gpuDetails = infoType.ToLower() switch
                    {
                        "name" => name,
                        "driver" => driverVersion,
                        _ => $"{name}\nDriver version: {driverVersion}" // Default/both case
                    };

                    gpuList.Add(gpuDetails);
                }
            }

            return string.Join("\n", gpuList);
        }
        catch (Exception)
        {
            // Silently fail if we can't get GPU information
        }

        return "Unknown";
    }

    private static string GetIdeName()
    {
        try
        {
            // Get the current process
            var currentProcess = Process.GetCurrentProcess();

            // Use WMI to find the parent process
            var query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {currentProcess.Id}";
            using var searcher = new ManagementObjectSearcher(query);
            var result = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

            if (result != null)
            {
                var parentProcessId = Convert.ToInt32(result["ParentProcessId"]);
                using var parentProcess = Process.GetProcessById(parentProcessId);
                var processName = parentProcess.ProcessName.ToLower();

                // Map common IDE process names to user-friendly names
                return processName switch
                {
                    "devenv" => "Visual Studio",
                    "vsdebugconsole" => "Visual Studio",
                    "rider64" => "JetBrains Rider",
                    "vshost" => "Visual Studio (Debug Host)",
                    "msvsmon" => "Visual Studio Remote Debugger",
                    _ => parentProcess.ProcessName // Fallback to the raw process name
                };
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to get IDE name: {e.Message}");
        }

        return "Unknown";
    }
    private static readonly string ideName = GetIdeName();
    private static readonly string appLanguage = GetAppLanguage();
    private static readonly string dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static readonly string operatingSystemInfo = GetOperatingSystemInfo();
    private static readonly string systemLanguage = GetSystemLanguage();
    private static readonly string keyboardLayout = GetKeyboardLayout();
    private static readonly string dotNetRuntime = GetDotNetRuntimeVersion();
    private static readonly string dotNetSdk = GetDotNetSdkVersion();
    private static readonly string gpuInformation = GetGpuInformation();
    private static readonly string gitCommitHash = GetGitCommitHash(); // get commit hash from git
    private string _systemInfo = string.Empty;

    private static readonly Vector2 mySpacing = new (6.0f, 3.0f);


}