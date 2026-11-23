#nullable enable
using ImGuiNET;
using T3.Editor.ExtensionPackages;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// Settings → Extensions: optional operator packages with a checkbox each. Installing and removing are staged and
/// applied by a restart (<see cref="InstalledExtensions"/>). Row texts are built when the catalog or an operation
/// changes, so drawing allocates nothing.
/// </summary>
internal static class ExtensionsSettingsPanel
{
    private sealed class Row
    {
        public required string Name;
        public required string Title;
        public required string Description;
        public required string Details;
        public string? Status;
        public bool IsAvailable;
        public bool IsWanted;

        /** Installed or staged for installation, and not staged for removal. */
        public bool IsInstalledAfterRestart;

        public bool NeedsUpdate;
    }

    public static void Draw()
    {
        ExtensionCatalog.LoadIfNeeded();
        RebuildRowsIfNeeded();

        if (_restartRequested)
        {
            _restartRequested = false;
            _restartFailed = !EditorRestart.TryRestart();
        }

        CustomComponents.HelpText("Operator packages that are not part of the default install, because of their licence, size or platform. "
                                  + "Changes apply after a restart.");
        FormInputs.AddVerticalSpace();

        var catalog = ExtensionCatalog.State;
        if (catalog.Status == ExtensionCatalog.LoadStatus.Loading)
        {
            CustomComponents.StylizedText("Loading the list of extensions...", Fonts.FontSmall, UiColors.TextMuted);
        }
        else if (catalog.Status == ExtensionCatalog.LoadStatus.Failed)
        {
            CustomComponents.StylizedText(catalog.Error ?? "The list of extensions could not be loaded.", Fonts.FontSmall, UiColors.TextMuted);
            if (ImGui.Button("Retry"))
                ExtensionCatalog.Reload();
        }

        for (var index = 0; index < _rows.Count; index++)
            DrawRow(_rows[index]);

        FormInputs.AddVerticalSpace();
        DrawActions();
    }

    private static void DrawRow(Row row)
    {
        ImGui.PushID(row.Name);
        FormInputs.AddVerticalSpace(4);

        if (!row.IsAvailable)
            ImGui.BeginDisabled();

        ImGui.Checkbox("##wanted", ref row.IsWanted);
        ImGui.SameLine();
        var textIndent = ImGui.GetCursorPosX();
        ImGui.TextUnformatted(row.Title);

        ImGui.PushFont(Fonts.FontSmall);
        CustomComponents.RightAlign(ImGui.CalcTextSize(row.Details).X);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(row.Details);

        ImGui.SetCursorPosX(textIndent);
        ImGui.TextWrapped(row.Description);
        ImGui.PopStyleColor();
        if (row.Status != null)
        {
            ImGui.SetCursorPosX(textIndent);
            ImGui.TextUnformatted(row.Status);
        }

        ImGui.PopFont();

        if (!row.IsAvailable)
            ImGui.EndDisabled();

        ImGui.PopID();
    }

    private static void DrawActions()
    {
        var operation = ExtensionCatalog.Operation;
        var hasNewChanges = false;
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            if (row.IsWanted != row.IsInstalledAfterRestart || (row.IsWanted && row.NeedsUpdate))
                hasNewChanges = true;
        }

        var label = hasNewChanges || !_hasStagedChanges ? "Install & Restart" : "Restart to Apply";
        var isEnabled = !operation.IsRunning && (hasNewChanges || _hasStagedChanges);
        if (CustomComponents.DrawCtaButton(label, Icon.None, isEnabled ? CustomComponents.ButtonStates.Emphasized : CustomComponents.ButtonStates.Disabled)
            && isEnabled)
        {
            if (hasNewChanges)
                StageAndRestart();
            else
                _restartRequested = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Install from File...") && !operation.IsRunning)
            InstallFromFile();

        if (operation.Message != null)
            CustomComponents.StylizedText(operation.Message, Fonts.FontSmall, operation.Failed ? UiColors.StatusAttention : UiColors.TextMuted);

        if (_restartFailed)
            CustomComponents.StylizedText("TiXL could not restart itself. Please restart it to apply the changes.", Fonts.FontSmall, UiColors.StatusAttention);
    }

    private static void StageAndRestart()
    {
        var wanted = new List<string>();
        foreach (var row in _rows)
        {
            if (row.IsWanted)
                wanted.Add(row.Name);
        }

        ExtensionCatalog.StageChangesAsync(wanted).ContinueWith(task => _restartRequested = task.Result);
    }

    private static void InstallFromFile()
    {
        using var picker = EditorUi.Instance.CreateFilePicker();
        picker.Filter = "TiXL extensions (*.nupkg)|*.nupkg";
        try
        {
            if (!picker.ChooseFile())
                return;
        }
        catch (Exception e)
        {
            Log.Warning("Couldn't open file picker: " + e.Message);
            return;
        }

        ExtensionCatalog.StageFromFileAsync(picker.FileName);
    }

    /** Rebuilds the rows when the catalog was (re)loaded or an install operation finished. */
    private static void RebuildRowsIfNeeded()
    {
        var catalog = ExtensionCatalog.State;
        var operation = ExtensionCatalog.Operation;
        if (ReferenceEquals(catalog, _rowsBuiltFromCatalog) && ReferenceEquals(operation, _rowsBuiltFromOperation))
            return;

        _rowsBuiltFromCatalog = catalog;
        _rowsBuiltFromOperation = operation;
        if (operation.IsRunning)
            return;

        var installed = InstalledExtensions.ReadInstalled();
        var pending = InstalledExtensions.ReadPending();
        _hasStagedChanges = !pending.IsEmpty;
        _rows.Clear();

        foreach (var extension in catalog.Extensions)
        {
            installed.TryGetValue(extension.Name, out var installedExtension);
            _rows.Add(CreateRow(extension.Name, extension.Title, extension.Description,
                                $"{DisplayLicence(extension.Licence)} · {FormatSize(extension.Size)}",
                                extension.IsAvailableHere, installedExtension != null, pending,
                                needsUpdate: installedExtension != null && installedExtension.Sha256 != extension.Sha256));
        }

        // Installed from a file, or no longer published: still listed, so it can be removed
        foreach (var (name, extension) in installed.Concat(pending.Install))
        {
            if (_rows.Any(r => r.Name == name))
                continue;

            _rows.Add(CreateRow(name, extension.Title, $"Version {extension.Version}", "", true, installed.ContainsKey(name), pending, false));
        }
    }

    private static Row CreateRow(string name, string title, string description, string details, bool isAvailable, bool isInstalled,
                                 InstalledExtensions.PendingChanges pending, bool needsUpdate)
    {
        var willInstall = pending.Install.ContainsKey(name);
        var willRemove = pending.Remove.Contains(name);
        var installedAfterRestart = (isInstalled || willInstall) && !willRemove;

        string? status = null;
        if (!isAvailable)
            status = $"Not available on {PlatformName} yet";
        else if (willRemove)
            status = "Will be removed on restart";
        else if (willInstall)
            status = "Will be installed on restart";
        else if (isInstalled && needsUpdate)
            status = "Installed · update available";
        else if (isInstalled)
            status = "Installed";

        return new Row
                   {
                       Name = name,
                       Title = title,
                       Description = description,
                       Details = details,
                       Status = status,
                       IsAvailable = isAvailable,
                       IsWanted = installedAfterRestart,
                       IsInstalledAfterRestart = installedAfterRestart,
                       NeedsUpdate = needsUpdate && !willInstall,
                   };
    }

    private static string DisplayLicence(string licence)
        => licence.StartsWith("LicenseRef-", StringComparison.Ordinal) ? licence["LicenseRef-".Length..].Replace('-', ' ') : licence;

    private static string FormatSize(long bytes) => bytes < 100_000 ? "0.1 MB" : $"{bytes / 1_000_000.0:0.#} MB";

    private static string PlatformName => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

    private static readonly List<Row> _rows = [];
    private static ExtensionCatalog.CatalogState? _rowsBuiltFromCatalog;
    private static ExtensionCatalog.OperationState? _rowsBuiltFromOperation;
    private static bool _hasStagedChanges;
    private static volatile bool _restartRequested;
    private static bool _restartFailed;
}
