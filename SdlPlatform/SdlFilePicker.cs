using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// <see cref="IFilePicker"/> on SDL's native dialogs: the portal or zenity on Linux, the common dialog on Windows.
/// SDL's dialogs are asynchronous; <see cref="ChooseFile"/> blocks until one closes, as <c>OpenFileDialog</c> did.
/// </summary>
/// <remarks>
/// Callers pick folders the way WinForms made them: <see cref="CheckFileExists"/> off and a placeholder
/// <see cref="FileName"/>, then <c>Path.GetDirectoryName</c> of the result. That combination opens SDL's folder
/// dialog, and the chosen folder comes back with the placeholder appended, so those callers keep working.
/// </remarks>
public sealed unsafe class SdlFilePicker : IFilePicker
{
    public string FileName { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string InitialDirectory { get; set; } = string.Empty;
    public bool Multiselect { get; set; }
    public bool RestoreDirectory { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowReadOnly { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ValidateNames { get; set; } = true;
    public bool CheckFileExists { get; set; } = true;
    public bool CheckPathExists { get; set; } = true;

    /// <summary>1-based, as in WinForms. SDL cannot preselect a filter, so the chosen one is listed first.</summary>
    public int FilterIndex { get; set; } = 1;

    public bool ChooseFile()
    {
        var pickFolder = !CheckFileExists && !ValidateNames;
        var request = new Request();
        var handle = GCHandle.Alloc(request);
        var allocations = new List<IntPtr>();

        try
        {
            var defaultLocation = string.IsNullOrEmpty(InitialDirectory) ? null : InitialDirectory;

            if (pickFolder)
            {
                SDL_ShowOpenFolderDialog(&OnDialogClosed, GCHandle.ToIntPtr(handle), null, defaultLocation, false);
            }
            else
            {
                var filters = ParseFilters(Filter, FilterIndex);
                var nativeFilters = stackalloc SDL_DialogFileFilter[Math.Max(1, filters.Count)];
                for (var i = 0; i < filters.Count; i++)
                {
                    nativeFilters[i].name = AllocateUtf8(filters[i].Name, allocations);
                    nativeFilters[i].pattern = AllocateUtf8(filters[i].Pattern, allocations);
                }

                SDL_ShowOpenFileDialog(&OnDialogClosed, GCHandle.ToIntPtr(handle), null, nativeFilters, filters.Count, defaultLocation,
                                       Multiselect);
            }

            // The callback can arrive on another thread or from inside the event pump, so pump until it has.
            while (!request.IsDone)
            {
                SDL_PumpEvents();
                Thread.Sleep(10);
            }
        }
        finally
        {
            handle.Free();
            foreach (var allocation in allocations)
            {
                Marshal.FreeHGlobal(allocation);
            }
        }

        if (request.Error != null)
            throw new InvalidOperationException(request.Error);

        if (request.Path == null)
            return false;

        FileName = pickFolder ? Path.Combine(request.Path, string.IsNullOrEmpty(FileName) ? "." : FileName) : request.Path;
        return true;
    }

    public void Dispose()
    {
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDialogClosed(IntPtr userData, byte** fileList, int filter)
    {
        var request = (Request)GCHandle.FromIntPtr(userData).Target!;

        if (fileList == null)
        {
            request.Error = SDL_GetError() ?? "The file dialog failed.";
        }
        else if (fileList[0] != null)
        {
            // Only the first of several: IFilePicker has no list to hand back.
            request.Path = Marshal.PtrToStringUTF8((IntPtr)fileList[0]);
        }

        request.IsDone = true;
    }

    /// <summary>WinForms' "Images (*.jpg;*.png)|*.jpg;*.png|All files (*.*)|*.*" as SDL's name and "jpg;png" pairs.</summary>
    private static List<(string Name, string Pattern)> ParseFilters(string filter, int filterIndex)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(filter))
            return result;

        var parts = filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var extensions = new List<string>();
            foreach (var pattern in parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var extension = pattern.StartsWith("*.") ? pattern[2..] : pattern.TrimStart('*', '.');
                extensions.Add(extension is "" or "*" ? "*" : extension);
            }

            result.Add((parts[i], extensions.Contains("*") ? "*" : string.Join(';', extensions)));
        }

        var selected = filterIndex - 1;
        if (selected > 0 && selected < result.Count)
        {
            var preferred = result[selected];
            result.RemoveAt(selected);
            result.Insert(0, preferred);
        }

        return result;
    }

    /// <summary>A zero-terminated copy that outlives the dialog call; freed with the rest after it closes.</summary>
    private static byte* AllocateUtf8(string text, List<IntPtr> allocations)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var copy = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, copy, bytes.Length);
        ((byte*)copy)[bytes.Length] = 0;
        allocations.Add(copy);
        return (byte*)copy;
    }

    private sealed class Request
    {
        public volatile bool IsDone;
        public string? Path;
        public string? Error;
    }
}
