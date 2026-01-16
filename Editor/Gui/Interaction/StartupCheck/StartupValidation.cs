using System.IO;
using System.Text;
using T3.Core.SystemUi;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Interaction.StartupCheck;

/// <summary>
/// Looks for required files and folders
/// and shows a warning popup instead of an exception...
/// </summary>
internal static class StartupValidation
{
    public static void CheckInstallation()
    {
            
        var checks = new Check[]
                         {
                             new()
                                 {
                                     RequiredFilePaths = new List<string>()
                                                             {
                                                                 @"Resources\",
                                                                 @"Resources\images\editor\t3-icons.png",
                                                                 @"Resources\images\editor\t3.ico",
                                                                 @"Resources\images\editor\t3-SplashScreen.png",
                                                                 @"Resources\fonts\editor\Roboto-Regular.ttf",
                                                             },
                                     Message = @"Please make sure to set the correct start up directory.\n ",
                                     URL = "https://github.com/tixl3d/tixl/wiki/help.InstallDev",
                                 },
                             new()
                                 {
                                     RequiredFilePaths = new List<string>()
                                                             {
                                                                 LayoutHandling.LayoutFolder + "layout1.json",
                                                                 @"Editor\bin\Release\net9.0-windows\bass.dll",
                                                                 @"Editor\bin\Debug\net8.0-windows\bass.dll",
                                                             },
                                     Message = "Please run Install/install.bat.",
                                     URL = "https://github.com/tixl3d/tixl/wiki/help.Installation",
                                 },
                             new()
                                 {
                                     RequiredFilePaths = new List<string>()
                                                             {
                                                                 @"Player\bin\Release\net8.0-windows\Player.exe",
                                                             },
                                     Message = "This will prevent you from exporting as executable.\nPlease rebuild/reinstall your solution.",
                                     URL = "https://github.com/tixl3d/tixl/wiki/help.Installation",
                                 }
                         };
        var _ = checks.Any(check => !check.Do());
    }
        
    private struct Check
    {
        public List<string> RequiredFilePaths;
        public string Message;
        public string URL;

        public bool Do()
        {
            var missingPaths = new List<string>();
            foreach (var filepath in RequiredFilePaths)
            {
                if (filepath.EndsWith(@"\"))
                {
                    if (!Directory.Exists(filepath))
                    {
                        missingPaths.Add(filepath);
                    }
                }
                else if (!File.Exists(filepath))
                {
                    missingPaths.Add(filepath);
                }
            }

            if (missingPaths.Count <= 0)
                return true;

            const string caption = "TiXL setup looks incomplete";
                
            var sb = new StringBuilder();

            var startupPath = Path.GetFullPath(".");
            sb.Append($"Startup folder is:\n{startupPath}\n\n");
                
            sb.Append($"We are unable to find the following files...\n\n  {string.Join("\n  ", missingPaths)}");
            sb.Append("\n\n");
            sb.Append(Message);
            string[] buttons;
            const string helpButton = "Get help";
            if (!string.IsNullOrEmpty(URL))
            {
                sb.Append("\n\n");
                sb.Append("Click Yes to get help.");
                buttons = [helpButton, "Just close"];
            }
            else
            {
                buttons = ["Close"];
            }


            var result = BlockingWindow.Instance.ShowMessageBox(sb.ToString(), caption, buttons);
            if (result == helpButton)
            {
                CoreUi.Instance.OpenWithDefaultApplication(URL);
            }
                
            EditorUi.Instance.ExitApplication();
            EditorUi.Instance.ExitThread();
            return false;
        }
    }

    internal static void ValidateNotRunningFromSystemFolder()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var specialFolders = new[]
                                 {
                                     Environment.SpecialFolder.ProgramFilesX86,
                                     Environment.SpecialFolder.ProgramFiles,
                                     Environment.SpecialFolder.System,
                                     Environment.SpecialFolder.Windows,
                                 };
            
        foreach (var p in specialFolders)
        {
            var folderPath = Environment.GetFolderPath(p);
            if (currentDir.IndexOf(folderPath, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            BlockingWindow.Instance.ShowMessageBox("TiXL cannot be started from {folderPath}", @"Error", "Ok");
            EditorUi.Instance.ExitApplication();
        }
            
        // Not writeable
        var directoryInfo = new DirectoryInfo(currentDir);
        if (!directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            return;
            
        BlockingWindow.Instance.ShowMessageBox($"Cannot write to the current working directory: {currentDir}.", @"Error", "Ok");
        EditorUi.Instance.ExitApplication();
    }

    /// <summary>
    /// Validate that operators.dll has been updated to warn users if they started "T3Editor.exe"
    /// </summary>
    public static void ValidateCurrentStandAloneExecutable()
    {
        var fiveMinutes = new TimeSpan(0, 2, 0);
        const string operatorFilePath = "Operators.dll";
        if (File.Exists(operatorFilePath) && (DateTime.Now - File.GetLastWriteTime(operatorFilePath)) <= fiveMinutes)
            return;
            
        BlockingWindow.Instance.ShowMessageBox($"Operators.dll is outdated.\nPlease use StartT3.exe to run TiXL.",
                                               @"Error", "Ok");
        EditorUi.Instance.ExitApplication();
    }

    internal static void ValidateExecutionPolicy()
    {
        if (IsExecutionPolicySufficient()) 
            return;
        
        if (TrySetExecutionPolicyToRemoteSigned()) 
            return;
        
        BlockingWindow.Instance.ShowMessageBox($"Cannot proceed: PowerShell script execution is too restricted..",
                                               @"Error", "Ok");
        EditorUi.Instance.ExitApplication();        
    }
    
    private static bool IsExecutionPolicySufficient()
    {
        var process = new System.Diagnostics.Process
                          {
                              StartInfo = new System.Diagnostics.ProcessStartInfo
                                              {
                                                  FileName = "powershell",
                                                  Arguments = "-Command \"Get-ExecutionPolicy -Scope CurrentUser\"",
                                                  RedirectStandardOutput = true,
                                                  UseShellExecute = false,
                                                  CreateNoWindow = true
                                              }
                          };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output != "Restricted" && output != "AllSigned";
    }
    
    private static bool TrySetExecutionPolicyToRemoteSigned()
    {
        var process = new System.Diagnostics.Process
                          {
                              StartInfo = new System.Diagnostics.ProcessStartInfo
                                              {
                                                  FileName = "powershell",
                                                  Arguments = "-Command \"Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force\"",
                                                  UseShellExecute = true, // This allows UAC prompt if needed
                                                  Verb = "runas" // Elevate
                                              }
                          };

        try
        {
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // User refused elevation or it's blocked
            Log.Warning("Failed to elevate: " + ex.Message);
            return false;
        }
    }    
    

}
