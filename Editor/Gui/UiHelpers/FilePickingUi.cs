#nullable enable
using System.Threading;
using ImGuiNET;
using SilkWindows;
using SilkWindows.Implementations.FileManager;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using ResourceInputWithTypeAheadSearch = T3.Editor.Gui.Input.ResourceInputWithTypeAheadSearch;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Handles drawing a project file resource picker e.g. for StringInputUis our Soundtracks.
/// </summary>
internal static class FilePickingUi
{
    public static InputEditStateFlags DrawTypeAheadSearch(FileOperations.FilePickerTypes type, string? fileFilter, ref string? filterAndSelectedPath)
    {
        ImGui.SetNextItemWidth(-70 * T3Ui.UiScaleFactor);

        var nodeSelection = ProjectView.Focused?.NodeSelection;
        if (ProjectView.Focused?.CompositionInstance == null || nodeSelection == null)
            return InputEditStateFlags.Nothing;

        var selectedInstances = nodeSelection.GetSelectedInstances().ToArray();
        var needsToGatherPackages = true; //SearchResourceConsumer is null;

        if (selectedInstances.Length == 0)
        {
            SearchResourceConsumer = new TempResourceConsumer(ProjectView.Focused.CompositionInstance.AvailableResourcePackages);
        }
        else
        {
            // Check later...            
            // || selectedInstances.Length != StringInputUi._selectedInstances.Length 
            // || !selectedInstances.Except(StringInputUi._selectedInstances).Any();
            if (needsToGatherPackages)
            {
                var packagesInCommon = selectedInstances.PackagesInCommon().ToArray();
                SearchResourceConsumer = new TempResourceConsumer(packagesInCommon);
            }
        }

        var isFolder = type == FileOperations.FilePickerTypes.Folder;
        var exists = ResourceManager.TryResolveRelativePath(filterAndSelectedPath, SearchResourceConsumer, out _, out _, isFolder);

        var warning = type switch
                          {
                              FileOperations.FilePickerTypes.File when !exists   => "File doesn't exist:\n",
                              FileOperations.FilePickerTypes.Folder when !exists => "Directory doesn't exist:\n",
                              _                                                  => string.Empty
                          };

        if (warning != string.Empty)
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAnimated.Rgba);

        var fileManagerOpen = _fileManagerOpen;
        if (fileManagerOpen)
        {
            ImGui.BeginDisabled();
        }

        var fileFiltersInCommon = ExtendFileFilterWithSelectedOps(fileFilter, selectedInstances);

        var inputEditStateFlags = InputEditStateFlags.Nothing;
        if (filterAndSelectedPath != null && SearchResourceConsumer != null)
        {
            var allItems = ResourceManager.EnumerateResources(fileFiltersInCommon,
                                                              isFolder,
                                                              SearchResourceConsumer.AvailableResourcePackages,
                                                              ResourceManager.PathMode.Aliased);

            var changed = ResourceInputWithTypeAheadSearch.Draw("##filePathSearch", allItems, !exists, ref filterAndSelectedPath, out _);

            var result = new InputResult(changed, filterAndSelectedPath);
            filterAndSelectedPath = result.Value;
            inputEditStateFlags = result.Modified ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
        }

        if (warning != string.Empty)
            ImGui.PopStyleColor();

        if (ImGui.IsItemHovered() && filterAndSelectedPath != null && filterAndSelectedPath.Length > 0 &&
            ImGui.CalcTextSize(filterAndSelectedPath).X > ImGui.GetItemRectSize().X)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(warning + filterAndSelectedPath);
            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        //var modifiedByPicker = FileOperations.DrawFileSelector(type, ref value, filter);

        if (ImGui.Button("...##fileSelector"))
        {
            if (SearchResourceConsumer != null)
            {
                OpenFileManager(type, SearchResourceConsumer.AvailableResourcePackages, fileFiltersInCommon, isFolder, async: true);
            }
            else
            {
                Log.Warning("Can open file manager with undefined resource consumer");
            }
        }

        if (fileManagerOpen)
        {
            ImGui.EndDisabled();
        }

        // refresh value because 

        string? fileManValue;
        lock (_fileManagerResultLock)
        {
            fileManValue = _latestFileManagerResult;
            _latestFileManagerResult = null;
        }

        var valueIsUpdated = !string.IsNullOrEmpty(fileManValue) && fileManValue != filterAndSelectedPath;

        if (valueIsUpdated)
        {
            filterAndSelectedPath = fileManValue;
            inputEditStateFlags |= InputEditStateFlags.Modified;
        }

        if (_hasClosedFileManager)
        {
            _hasClosedFileManager = false;
            inputEditStateFlags |= InputEditStateFlags.Finished;
        }

        return inputEditStateFlags;
    }

    /// <remarks>
    /// Tom: I think this method is overengineered and introduces a lot of dependencies:
    /// - We can't assume that <see cref="IDescriptiveFilename"/> is used for all ops (and yes, the interface's name is not ideal)
    /// - At the current state, only a single parameter of an operator can be invoked for file picking, which already have the
    /// fileFilter accessible.
    /// - The whole notion and format of Microsoft's file filter API seems dated, and error-prone.
    /// </remarks>
    private static string[] ExtendFileFilterWithSelectedOps(string? fileFilter, Instance[] selectedInstances)
    {
        string[] uiFilter;
        if (fileFilter == null)
            uiFilter = [];
        else if (!fileFilter.Contains('|'))
            uiFilter = [fileFilter];
        else
            uiFilter = fileFilter.Split('|')[1].Split(';');

        var fileFiltersInCommon = selectedInstances
                                 .Where(x => x is IDescriptiveFilename)
                                 .Cast<IDescriptiveFilename>()
                                 .Select(x => x.FileFilter)
                                 .Aggregate(Enumerable.Empty<string>(), (a, b) => a.Intersect(b))
                                 .Concat(uiFilter)
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Distinct()
                                 .ToArray();
        return fileFiltersInCommon;
    }

    private static void OpenFileManager(FileOperations.FilePickerTypes type, IEnumerable<IResourcePackage> packagesInCommon, string[] fileFiltersInCommon,
                                        bool isFolder, bool async)
    {
        var rootDirectories = packagesInCommon
                                .Concat(ResourceManager.GetSharedPackagesForFilters(fileFiltersInCommon, isFolder, out var culledFilters))
                                .Distinct()
                                .OrderBy(package => !package.IsReadOnly)
                                .Select(package => new ManagedDirectory(package.ResourcesFolder, package.IsReadOnly, !package.IsReadOnly, package.Alias));

        var fileManagerMode = type == FileOperations.FilePickerTypes.File ? FileManagerMode.PickFile : FileManagerMode.PickDirectory;

        Func<string, bool> filterFunc = culledFilters.Length == 0
                                            ? _ => true
                                            : str => { return culledFilters.Any(filter => StringUtils.MatchesSearchFilter(str, filter, ignoreCase: true)); };

        var options = new SimpleWindowOptions(new Vector2(960, 600), 60, true, true, false);
        if (!async)
        {
            StartFileManagerBlocking();
        }
        else
        {
            StartFileManagerAsync();
        }

        return;

        void StartFileManagerAsync()
        {
            var fileManager = new FileManager(fileManagerMode, rootDirectories, filterFunc)
                                  {
                                      CloseOnResult = false,
                                      ClosingCallback = () =>
                                                        {
                                                            _hasClosedFileManager = true;
                                                            _fileManagerOpen = false;
                                                        }
                                  };
            _fileManagerOpen = true;
            _ = ImGuiWindowService.Instance.ShowAsync("Select a path", fileManager, (result) =>
                                                                                    {
                                                                                        lock (_fileManagerResultLock)
                                                                                            _latestFileManagerResult =
                                                                                                result.RelativePathWithAlias ?? result.RelativePath;
                                                                                    }, options);
        }

        void StartFileManagerBlocking()
        {
            var fileManager = new FileManager(fileManagerMode, rootDirectories, filterFunc);
            _fileManagerOpen = true;
            var fileManagerResult = ImGuiWindowService.Instance.Show("Select a path", fileManager, options);
            _fileManagerOpen = false;

            if (fileManagerResult != null)
            {
                lock (_fileManagerResultLock) // unnecessary, but consistent
                {
                    _latestFileManagerResult = fileManagerResult.RelativePathWithAlias ?? fileManagerResult.RelativePath;
                }
            }

            _hasClosedFileManager = true;
        }
    }

    public static TempResourceConsumer? SearchResourceConsumer;

    private static string? _latestFileManagerResult;
    private static bool _fileManagerOpen;
    private static readonly Lock _fileManagerResultLock = new();
    private static bool _hasClosedFileManager;

    private readonly record struct InputResult(bool Modified, string Value);
}