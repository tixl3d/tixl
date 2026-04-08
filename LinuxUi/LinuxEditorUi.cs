using System.Diagnostics;
using System.Drawing;
using T3.SystemUi;

namespace T3.LinuxUi;

/// <summary>
/// Linux implementation of IEditorSystemUiService. Used by the Editor on Linux.
/// </summary>
public class LinuxEditorUi : LinuxCoreUi, IEditorSystemUiService
{
    public void EnableDpiAwareScaling()
    {
        // On Linux, DPI scaling is handled by the compositor/window manager
    }

    public void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            // Try xclip first, fall back to xsel
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process != null)
            {
                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(1000);
            }
        }
        catch
        {
            // Fallback: try xsel
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "xsel",
                    Arguments = "--clipboard --input",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process != null)
                {
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    process.WaitForExit(1000);
                }
            }
            catch
            {
                // Clipboard not available
            }
        }
    }

    public string GetClipboardText()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard -o",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process != null)
            {
                var text = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                return text;
            }
        }
        catch
        {
            // Fallback: try xsel
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "xsel",
                    Arguments = "--clipboard --output",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process != null)
                {
                    var text = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(1000);
                    return text;
                }
            }
            catch
            {
                // Clipboard not available
            }
        }

        return string.Empty;
    }

    public IFilePicker CreateFilePicker()
    {
        return new LinuxFilePicker();
    }

    public IReadOnlyList<IScreen> AllScreens =>
    [
        new LinuxScreen()
    ];

    private sealed class LinuxScreen : IScreen
    {
        public int BitsPerPixel => 32;
        public Rectangle Bounds => new(0, 0, 1920, 1080); // TODO: query via Silk.NET monitor API
        public Rectangle WorkingArea => Bounds;
        public string DeviceName => "Primary";
        public bool Primary => true;
    }
}
