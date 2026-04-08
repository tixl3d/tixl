using System.Diagnostics;
using T3.SystemUi;

namespace T3.LinuxUi;

/// <summary>
/// Linux file picker using zenity (GTK) or kdialog (KDE) as fallback.
/// </summary>
public sealed class LinuxFilePicker : IFilePicker
{
    public string FileName { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string InitialDirectory { get; set; } = string.Empty;
    public bool Multiselect { get; set; }
    public bool RestoreDirectory { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowReadOnly { get; set; }
    public string Title { get; set; } = "Open File";
    public bool ValidateNames { get; set; }
    public bool CheckFileExists { get; set; }
    public bool CheckPathExists { get; set; }
    public int FilterIndex { get; set; }

    public bool ChooseFile()
    {
        // Try zenity first (GTK), then kdialog (KDE)
        var result = TryZenity() ?? TryKdialog();
        if (result != null)
        {
            FileName = result.Trim();
            return !string.IsNullOrEmpty(FileName);
        }

        return false;
    }

    private string? TryZenity()
    {
        try
        {
            var args = $"--file-selection --title=\"{Title}\"";
            if (!string.IsNullOrEmpty(InitialDirectory))
                args += $" --filename=\"{InitialDirectory}/\"";
            if (!string.IsNullOrEmpty(Filter))
                args += $" --file-filter=\"{ConvertFilterToZenity(Filter)}\"";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);
                return process.ExitCode == 0 ? output : null;
            }
        }
        catch
        {
            // zenity not available
        }

        return null;
    }

    private string? TryKdialog()
    {
        try
        {
            var args = $"--getopenfilename \"{InitialDirectory}\" --title \"{Title}\"";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "kdialog",
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);
                return process.ExitCode == 0 ? output : null;
            }
        }
        catch
        {
            // kdialog not available
        }

        return null;
    }

    private static string ConvertFilterToZenity(string winFormsFilter)
    {
        // Convert "Image Files|*.png;*.jpg" to "*.png *.jpg"
        var parts = winFormsFilter.Split('|');
        if (parts.Length >= 2)
            return parts[1].Replace(";", " ");
        return "*";
    }

    public void Dispose()
    {
    }
}
