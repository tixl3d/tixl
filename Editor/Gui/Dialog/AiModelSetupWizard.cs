#nullable enable
using System.IO;
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.AiModels;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// An installer-style wizard for the AI models: Welcome → Choose Components → Installing → Finished.
/// <para>
/// The component page works like a classic Windows setup: a setup type (Recommended, Minimal, Everything,
/// Custom) preselects a set of models, a checkbox tree lists every model by family, and a description
/// pane explains the hovered model with speed/quality ratings and hardware hints. The required download
/// size is compared with the free space on the model store's drive.
/// </para>
/// <para>
/// Downloads run through <see cref="AiModelDownloadService"/>, one model after the other. They are not
/// tied to the dialog: "Run in background" closes it while the queue continues, and reopening the wizard
/// returns to the progress page. The wizard opens once on its own on a fresh installation without any
/// downloaded models (<see cref="ShowOnFirstStartIfNeeded"/>), and from Help and Settings › AI Models.
/// </para>
/// </summary>
internal sealed class AiModelSetupWizard : ModalDialog
{
    private enum Pages { Welcome, Components, Installing, Finished }

    private enum SetupTypes { Recommended, Minimal, Everything, Custom }

    private enum ItemStates { Pending, Running, Done, Failed, Cancelled, Skipped }

    private sealed class QueueItem(ModelCatalogEntry entry, long sizeBytes)
    {
        public readonly ModelCatalogEntry Entry = entry;
        public readonly long SizeBytes = sizeBytes;
        public volatile ItemStates State = ItemStates.Pending;
        public string? Error;
    }

    // ---------------------------------------------------------------- entry points

    /// <summary>
    /// Once per installation: opens the wizard when the user never saw it and no model has been downloaded
    /// into the store yet. Afterwards it is only reached through the menu or the settings page.
    /// </summary>
    internal void ShowOnFirstStartIfNeeded()
    {
        if (AiModelSettings.Config.SetupWizardOffered)
            return;

        AiModelSettings.Config.SetupWizardOffered = true;
        AiModelSettings.Save();

        foreach (var entry in AiModelCatalog.Entries)
        {
            if (entry.IsDownloadable && !entry.ShippedWithTiXl && entry.IsInstalled())
                return; // Someone already set models up by hand or through the settings page
        }

        _openedAutomatically = true;
        ShowNextFrame();
    }

    protected override void OnShowNextFrame()
    {
        if (_queueTask is { IsCompleted: false })
        {
            _page = Pages.Installing;
            return;
        }

        // A queue that finished in the background still shows its summary once
        if (_page == Pages.Installing)
        {
            _page = Pages.Finished;
            return;
        }

        if (_page == Pages.Finished)
            _page = Pages.Welcome;

        InvalidateStateCache();
        AiSetupChecker.Run();
        ApplySetupType(_setupType == SetupTypes.Custom ? SetupTypes.Recommended : _setupType);
        RefreshFreeSpace();
    }

    // ---------------------------------------------------------------- frame

    internal void Draw()
    {
        DialogSize = new Vector2(780, 0);
        Padding = 0;

        if (BeginDialog("AI Model Setup###AiModelSetupWizard"))
        {
            // Finished downloads advance the page from the UI thread, never from the worker
            if (_page == Pages.Installing && _queueTask is { IsCompleted: true })
                _page = Pages.Finished;

            DrawHeader();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(PagePadding, 14) * T3Ui.UiScaleFactor);
            ImGui.BeginChild("##page", new Vector2(0, PageHeight * T3Ui.UiScaleFactor),
                             ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
            switch (_page)
            {
                case Pages.Welcome:    DrawWelcomePage(); break;
                case Pages.Components: DrawComponentsPage(); break;
                case Pages.Installing: DrawInstallingPage(); break;
                case Pages.Finished:   DrawFinishedPage(); break;
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();

            DrawFooter();

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawHeader()
    {
        var (title, subtitle) = _page switch
                                    {
                                        Pages.Welcome    => ("Welcome to the AI Model Setup", "Get the models TiXL's AI operators need, in a few clicks."),
                                        Pages.Components => ("Choose Components", "Choose which models you want to install."),
                                        Pages.Installing => ("Installing", "Please wait while the selected models are downloaded."),
                                        _                => ("Setup Complete", HasFailures ? "Some models could not be installed." : "Your AI operators are ready to use."),
                                    };

        var scale = T3Ui.UiScaleFactor;
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X, HeaderHeight * scale);
        drawList.AddRectFilled(min, max, UiColors.BackgroundGaps);
        drawList.AddLine(new Vector2(min.X, max.Y), max, UiColors.BackgroundFull.Fade(0.8f), 1);

        ImGui.SetCursorScreenPos(min + new Vector2(PagePadding, 14) * scale);
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.TextUnformatted(title);
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(new Vector2(min.X + PagePadding * scale, ImGui.GetCursorScreenPos().Y));
        ImGui.TextColored(UiColors.TextMuted, subtitle);

        // Step indicator on the right edge of the header band
        var step = $"Step {(int)_page + 1} of 4";
        var stepSize = ImGui.CalcTextSize(step);
        drawList.AddText(new Vector2(max.X - stepSize.X - PagePadding * scale, min.Y + 18 * scale), UiColors.TextMuted, step);

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
        ImGui.Dummy(new Vector2(1, 1));
    }

    private void DrawFooter()
    {
        var scale = T3Ui.UiScaleFactor;
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        drawList.AddLine(min, min + new Vector2(ImGui.GetContentRegionAvail().X, 0), UiColors.BackgroundFull.Fade(0.8f), 1);

        ImGui.SetCursorScreenPos(min + new Vector2(PagePadding, 12) * scale);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextColored(UiColors.TextMuted, FooterHint());
        ImGui.PopFont();

        ImGui.SetCursorScreenPos(min + new Vector2(0, 8 * scale));
        ImGui.Indent(PagePadding * scale);
        var escape = ImGui.IsKeyPressed(ImGuiKey.Escape, false);

        switch (_page)
        {
            case Pages.Welcome:
            {
                AlignButtons("Not now", "Next >");
                if (CustomComponents.DrawCtaButton("Not now", Icon.None, CustomComponents.ButtonStates.Default) || escape)
                    Close();

                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Next >", isEnabled: true, enableTriggerWithReturn: true))
                    _page = Pages.Components;
                break;
            }

            case Pages.Components:
            {
                var canInstall = _selected.Count > 0 && !IsShortOnSpace;
                AlignButtons("< Back", "Cancel", "Install");
                if (CustomComponents.DrawCtaButton("< Back", Icon.None, CustomComponents.ButtonStates.Default))
                    _page = Pages.Welcome;

                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default) || escape)
                    Close();

                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Install", isEnabled: canInstall, enableTriggerWithReturn: true))
                    StartInstall(SelectedEntries());

                if (!canInstall)
                    CustomComponents.TooltipForLastItem(_selected.Count == 0
                                                            ? "Select at least one model to install."
                                                            : "Not enough free space on the model store's drive.",
                                                        "Change the store folder on the first page to use another drive.");
                break;
            }

            case Pages.Installing:
            {
                AlignButtons("Cancel", "Run in background");
                if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
                    CancelInstall();

                CustomComponents.TooltipForLastItem("Stops the running download and skips the remaining models.",
                                                    "Models that finished already stay installed.");
                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Run in background", Icon.None, CustomComponents.ButtonStates.Default))
                    Close();

                CustomComponents.TooltipForLastItem("Closes this window; downloads continue.",
                                                    "Reopen Help › AI Model Setup… or Settings › AI Models to follow the progress.");
                break;
            }

            case Pages.Finished:
            {
                if (HasFailures)
                {
                    AlignButtons("Retry failed", "Finish");
                    if (CustomComponents.DrawCtaButton("Retry failed", Icon.None, CustomComponents.ButtonStates.Default))
                        StartInstall(_queue.Where(i => i.State == ItemStates.Failed).Select(i => i.Entry).ToList());

                    ImGui.SameLine();
                }
                else
                {
                    AlignButtons("Finish");
                }

                if (CustomComponents.DrawCtaButton("Finish", isEnabled: true, enableTriggerWithReturn: true) || escape)
                {
                    if (_openStoreOnFinish)
                        CoreUi.Instance.OpenWithDefaultApplication(AiModelSettings.ModelsRoot);

                    Close();
                }

                break;
            }
        }

        ImGui.Unindent(PagePadding * scale);
        ImGui.Dummy(new Vector2(1, 12 * scale));
    }

    private string FooterHint() => _page switch
        {
            Pages.Welcome    => _openedAutomatically ? "Shown once. Reopen it any time from Help › AI Model Setup…" : "",
            Pages.Components => $"{_selected.Count} model(s) selected",
            Pages.Installing => _cancelRequested ? "Cancelling…" : "Downloads continue if you close this window.",
            _                => "",
        };

    /** Right-aligns the button row; the base helper anchors to the dialog's minimum height, which is 0 here. */
    private static void AlignButtons(params ReadOnlySpan<string> labels)
    {
        var width = (labels.Length - 1) * ImGui.GetStyle().ItemSpacing.X;
        foreach (var label in labels)
            width += CustomComponents.GetCtaButtonSize(label).X;

        var x = ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - PagePadding * T3Ui.UiScaleFactor - width);
        ImGui.SetCursorPosX(MathF.Floor(x));
    }

    private void Close()
    {
        _openedAutomatically = false;
        ImGui.CloseCurrentPopup();
    }

    // ---------------------------------------------------------------- welcome

    private void DrawWelcomePage()
    {
        WrappedText("TiXL's AI operators - depth estimation, realtime diffusion, and face, hand and body tracking - " +
                    "need trained models to run. This setup downloads the ones you choose into a model store on your disk, " +
                    "outside the TiXL installation, so they survive updates and are shared by all projects.");
        FormInputs.AddVerticalSpace(4);
        WrappedText("You can rerun this setup at any time to add or update models.", UiColors.TextMuted);

        FormInputs.AddVerticalSpace(14);
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted("Destination folder");
        ImGui.PopFont();

        FormInputs.SetIndentToParameters();
        var previous = AiModelSettings.Config.ModelsDirectory;
        if (FormInputs.AddFilePicker("Store folder",
                                     ref AiModelSettings.Config.ModelsDirectory,
                                     $"(default: {AiModelSettings.ModelsRoot})",
                                     null,
                                     "Pick a drive with room to spare: the full set of models is several gigabytes.",
                                     FileOperations.FilePickerTypes.Folder)
            && !string.Equals(previous, AiModelSettings.Config.ModelsDirectory, StringComparison.Ordinal))
        {
            AiModelSettings.Save();
            RefreshFreeSpace();
        }

        FormInputs.ApplyIndent();
        ImGui.TextColored(UiColors.TextMuted, _freeSpaceBytes >= 0
                                                  ? $"{AiModelCatalog.FormatSize(_freeSpaceBytes)} free on this drive"
                                                  : "Free space unknown");

        FormInputs.AddVerticalSpace(14);
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted("This computer");
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);

        foreach (var item in AiSetupChecker.LastResults)
        {
            if (item.EntryToInstall != null || item.Title == "Model store" || item.Title.EndsWith("models", StringComparison.Ordinal))
                continue;

            var ok = item.Result == AiCheckResult.Ok;
            DrawDot(ok ? UiColors.StatusControlled : UiColors.TextMuted);
            ImGui.TextUnformatted(item.Title);
            ImGui.SameLine();
            ImGui.TextColored(UiColors.TextMuted, ok ? "found" : "not found");
            CustomComponents.TooltipForLastItem(item.Title, item.Detail);
        }

        if (!HasCuda())
        {
            FormInputs.AddVerticalSpace(4);
            WrappedText("Without CUDA the depth and diffusion models run much slower or not at all. The MediaPipe tracking models ship bundled with TiXL and need no setup.",
                        UiColors.StatusWarning);
        }
    }

    // ---------------------------------------------------------------- components

    private void DrawComponentsPage()
    {
        var scale = T3Ui.UiScaleFactor;

        // Setup type, like the "type of install" combo of classic installers
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Setup type:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * scale);
        if (ImGui.BeginCombo("##setupType", SetupTypeTitle(_setupType)))
        {
            foreach (var type in Enum.GetValues<SetupTypes>())
            {
                if (ImGui.Selectable(SetupTypeTitle(type), type == _setupType))
                    ApplySetupType(type);

                CustomComponents.TooltipForLastItem(SetupTypeTitle(type), SetupTypeDescription(type));
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextColored(UiColors.TextMuted, SetupTypeDescription(_setupType));

        FormInputs.AddVerticalSpace(6);

        // Component tree (left) and description pane (right)
        var listHeight = (PageHeight - 104) * scale;
        var paneWidth = 250 * scale;
        var treeWidth = ImGui.GetContentRegionAvail().X - paneWidth - 10 * scale;

        _hoveredEntry = null;
        _hoveredFamily = null;

        ImGui.BeginChild("##components", new Vector2(treeWidth, listHeight), ImGuiChildFlags.FrameStyle);
        foreach (var family in AiModelCatalog.VisibleFamilies)
            DrawFamilyRows(family);

        ImGui.EndChild();

        ImGui.SameLine(0, 10 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginChild("##description", new Vector2(paneWidth, listHeight),
                         ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AlwaysUseWindowPadding);
        DrawDescriptionPane();
        ImGui.EndChild();
        ImGui.PopStyleVar();

        // Disk space summary
        FormInputs.AddVerticalSpace(4);
        var required = RequiredBytes();
        ImGui.TextUnformatted($"Space required: {AiModelCatalog.FormatSize(required)}");
        ImGui.SameLine(0, 24 * scale);
        if (_freeSpaceBytes >= 0)
        {
            ImGui.TextColored(IsShortOnSpace ? UiColors.StatusAttention : UiColors.TextMuted,
                              $"Space available: {AiModelCatalog.FormatSize(_freeSpaceBytes)}");
            if (IsShortOnSpace)
            {
                ImGui.SameLine();
                ImGui.TextColored(UiColors.StatusAttention, "- not enough space");
            }
        }
    }

    private void DrawFamilyRows(AiModelFamily family)
    {
        var scale = T3Ui.UiScaleFactor;
        var entries = AiModelCatalog.ForFamily(family);
        if (entries.Count == 0)
            return;

        ImGui.PushID(family.ToString());

        // Bit mask of the selectable entries; CheckboxFlags then draws the family's mixed state for free
        var selectedMask = 0;
        var allMask = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!IsSelectable(entries[i]))
                continue;

            allMask |= 1 << i;
            if (_selected.Contains(entries[i].Id))
                selectedMask |= 1 << i;
        }

        var collapsed = _collapsedFamilies.Contains(family);
        if (ImGui.ArrowButton("##fold", collapsed ? ImGuiDir.Right : ImGuiDir.Down))
        {
            if (!_collapsedFamilies.Remove(family))
                _collapsedFamilies.Add(family);
        }

        ImGui.SameLine();
        if (allMask == 0)
        {
            ImGui.BeginDisabled();
            var allDone = true;
            ImGui.Checkbox("##family", ref allDone);
            ImGui.EndDisabled();
        }
        else if (ImGui.CheckboxFlags("##family", ref selectedMask, allMask))
        {
            var selectAll = (selectedMask & allMask) == allMask;
            for (var i = 0; i < entries.Count; i++)
            {
                if ((allMask & (1 << i)) == 0)
                    continue;

                if (selectAll)
                    _selected.Add(entries[i].Id);
                else
                    _selected.Remove(entries[i].Id);
            }

            _setupType = SetupTypes.Custom;
        }

        TrackHover(family, null);
        ImGui.SameLine();
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(family.Title());
        ImGui.PopFont();
        TrackHover(family, null);

        if (!collapsed)
        {
            ImGui.Indent(26 * scale);
            foreach (var entry in entries)
                DrawEntryRow(entry);

            ImGui.Unindent(26 * scale);
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.PopID();
    }

    private void DrawEntryRow(ModelCatalogEntry entry)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushID(entry.Id);

        var rowStartX = ImGui.GetCursorPosX();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        var state = EntryState(entry);

        if (state == EntryStates.Selectable)
        {
            var isSelected = _selected.Contains(entry.Id);
            if (ImGui.Checkbox(entry.DisplayName, ref isSelected))
            {
                if (isSelected)
                    _selected.Add(entry.Id);
                else
                    _selected.Remove(entry.Id);

                _setupType = SetupTypes.Custom;
            }
        }
        else
        {
            // Installed, bundled or manual entries: shown for the complete picture, but not selectable
            ImGui.BeginDisabled();
            var isChecked = state != EntryStates.ManualExport;
            ImGui.Checkbox(entry.DisplayName, ref isChecked);
            ImGui.EndDisabled();
        }

        TrackHover(entry.Family, entry);

        if (entry.IsRecommended && state == EntryStates.Selectable)
        {
            ImGui.SameLine(0, 6 * scale);
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(UiColors.StatusActivated, "recommended");
            ImGui.PopFont();
            TrackHover(entry.Family, entry);
        }

        // Right column: size or state
        var (rightText, rightColor) = state switch
                                          {
                                              EntryStates.Installed    => ("installed", UiColors.StatusControlled),
                                              EntryStates.Bundled      => ("bundled", UiColors.StatusControlled),
                                              EntryStates.Downloading  => ($"{AiModelDownloadService.GetStatus(entry).Progress:0%}", UiColors.StatusActivated),
                                              EntryStates.ManualExport => ("manual export", UiColors.TextMuted),
                                              _                        => (AiModelCatalog.FormatSize(Remaining(entry)), UiColors.TextMuted),
                                          };
        var rightWidth = ImGui.CalcTextSize(rightText).X;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + 8 * scale, rowStartX + rowWidth - rightWidth - 4 * scale));
        ImGui.TextColored(rightColor, rightText);
        TrackHover(entry.Family, entry);

        ImGui.PopID();
    }

    private void DrawDescriptionPane()
    {
        var entry = _hoveredEntry ?? _lastHoveredEntry;
        var family = _hoveredEntry != null ? (AiModelFamily?)null : _hoveredFamily ?? (entry == null ? _lastHoveredFamily : null);

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextColored(UiColors.TextMuted, "Description");
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);

        if (family != null)
        {
            ImGui.PushFont(Fonts.FontBold);
            WrappedText(family.Value.Title());
            ImGui.PopFont();
            FormInputs.AddVerticalSpace(4);
            WrappedText(family.Value.Details());
            FormInputs.AddVerticalSpace(6);
            WrappedText(family.Value.Description(), UiColors.TextMuted);
            return;
        }

        if (entry == null)
        {
            WrappedText("Position your mouse over a component to see its description.", UiColors.TextMuted);
            return;
        }

        ImGui.PushFont(Fonts.FontBold);
        WrappedText(entry.DisplayName);
        ImGui.PopFont();

        var state = EntryState(entry);
        var stateText = state switch
                            {
                                EntryStates.Installed    => "Installed",
                                EntryStates.Bundled      => "Ships with TiXL",
                                EntryStates.Downloading  => "Downloading…",
                                EntryStates.ManualExport => "Not a download - produced with local export tools",
                                _                        => $"Download: {AiModelCatalog.FormatSize(Remaining(entry))}",
                            };
        ImGui.TextColored(state is EntryStates.Installed or EntryStates.Bundled ? UiColors.StatusControlled : UiColors.TextMuted, stateText);

        FormInputs.AddVerticalSpace(6);
        WrappedText(string.IsNullOrEmpty(entry.Details) ? entry.Description : entry.Details);

        FormInputs.AddVerticalSpace(8);
        if (entry.Speed > 0)
            DrawRating("Speed", entry.Speed);

        if (entry.Quality > 0)
            DrawRating("Quality", entry.Quality);

        if (!string.IsNullOrEmpty(entry.Hardware))
            DrawFact("Runs on", entry.Hardware);

        if (!string.IsNullOrEmpty(entry.UsedBy))
            DrawFact("Used by", entry.UsedBy);
    }

    private static void DrawRating(string label, int value)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.TextColored(UiColors.TextMuted, label);
        ImGui.SameLine(64 * scale);

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, ImGui.GetTextLineHeight() * 0.3f);
        var size = new Vector2(16, ImGui.GetTextLineHeight() * 0.45f) * new Vector2(scale, 1);
        for (var i = 0; i < 5; i++)
        {
            var min = pos + new Vector2(i * (size.X + 3 * scale), 0);
            drawList.AddRectFilled(min, min + size, i < value ? UiColors.StatusActivated : UiColors.BackgroundButton);
        }

        ImGui.Dummy(new Vector2(5 * (size.X + 3 * scale), ImGui.GetTextLineHeight()));
    }

    private static void DrawFact(string label, string value)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.TextColored(UiColors.TextMuted, label);
        ImGui.SameLine(64 * scale);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(value);
        ImGui.PopTextWrapPos();
    }

    private void TrackHover(AiModelFamily family, ModelCatalogEntry? entry)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        _hoveredFamily = family;
        _hoveredEntry = entry;
        _lastHoveredEntry = entry;
        _lastHoveredFamily = entry == null ? family : null;
    }

    // ---------------------------------------------------------------- installing

    private void DrawInstallingPage()
    {
        var scale = T3Ui.UiScaleFactor;

        var total = 0L;
        var done = 0L;
        double rate = 0;
        QueueItem? current = null;
        foreach (var item in _queue)
        {
            total += item.SizeBytes;
            switch (item.State)
            {
                case ItemStates.Done:
                    done += item.SizeBytes;
                    break;
                case ItemStates.Running:
                {
                    var status = AiModelDownloadService.GetStatus(item.Entry);
                    done += (long)(item.SizeBytes * (double)status.Progress);
                    rate = status.BytesPerSecond;
                    current = item;
                    break;
                }
            }
        }

        var overall = total > 0 ? Math.Clamp(done / (float)total, 0f, 1f) : 0f;

        ImGui.TextUnformatted(current != null ? $"Installing {current.Entry.DisplayName}…" : _cancelRequested ? "Cancelling…" : "Preparing…");
        if (current != null)
        {
            var fileText = AiModelDownloadService.GetStatus(current.Entry).CurrentFile;
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(UiColors.TextMuted, fileText.Length > 0 ? fileText : "connecting…");
            ImGui.PopFont();
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.ProgressBar(overall, new Vector2(-1, 22 * scale), $"{overall:0%}");

        var remainingBytes = Math.Max(0, total - done);
        var speedText = rate > 1024
                            ? $"{AiModelCatalog.FormatSize((long)rate)}/s  ·  about {FormatDuration(remainingBytes / rate)} left"
                            : "";
        ImGui.TextColored(UiColors.TextMuted, $"{AiModelCatalog.FormatSize(done)} of {AiModelCatalog.FormatSize(total)}");
        if (speedText.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.TextMuted, $"  ·  {speedText}");
        }

        FormInputs.AddVerticalSpace(8);
        DrawQueueList(ImGui.GetContentRegionAvail().Y - 6 * scale);
    }

    private void DrawQueueList(float height)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.BeginChild("##queue", new Vector2(0, height), ImGuiChildFlags.FrameStyle);
        foreach (var item in _queue)
        {
            ImGui.PushID(item.Entry.Id);
            var rowWidth = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            var (text, color) = item.State switch
                                    {
                                        ItemStates.Done      => ("Done", UiColors.StatusControlled),
                                        ItemStates.Running   => ($"{AiModelDownloadService.GetStatus(item.Entry).Progress:0%}", UiColors.StatusActivated),
                                        ItemStates.Failed    => ("Failed", UiColors.StatusAttention),
                                        ItemStates.Cancelled => ("Cancelled", UiColors.TextMuted),
                                        ItemStates.Skipped   => ("Skipped", UiColors.TextMuted),
                                        _                    => ("Waiting", UiColors.TextMuted),
                                    };

            DrawDot(color);
            ImGui.TextUnformatted(item.Entry.DisplayName);
            if (item.State == ItemStates.Failed && item.Error != null)
                CustomComponents.TooltipForLastItem(UiColors.StatusAttention, "Download failed", item.Error);

            var sizeText = $"{AiModelCatalog.FormatSize(item.SizeBytes)}";
            ImGui.SameLine(rowWidth - 150 * scale);
            ImGui.TextColored(UiColors.TextMuted, sizeText);
            ImGui.SameLine(rowWidth - 70 * scale);
            ImGui.TextColored(color, text);

            if (item.State == ItemStates.Failed && item.Error != null)
            {
                ImGui.Indent(18 * scale);
                ImGui.PushFont(Fonts.FontSmall);
                WrappedText(item.Error, UiColors.StatusAttention);
                ImGui.PopFont();
                ImGui.Unindent(18 * scale);
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    // ---------------------------------------------------------------- finished

    private void DrawFinishedPage()
    {
        var installed = _queue.Count(i => i.State == ItemStates.Done);
        var failed = _queue.Count(i => i.State == ItemStates.Failed);
        var skipped = _queue.Count(i => i.State is ItemStates.Cancelled or ItemStates.Skipped);

        if (installed > 0)
            WrappedText($"{installed} model(s) were installed into the model store. The AI operators find them automatically - no restart needed.");
        else if (failed == 0)
            WrappedText("Nothing was installed.");

        // The diffusion checkpoint is an export input, not a runnable model: without this note the
        // wizard "finished" state looks like [StreamDiffusion] should already work.
        if (_queue.Any(i => i.Entry.Id == "streamdiffusion-v2-checkpoint" && i.State == ItemStates.Done))
        {
            WrappedText("One step remains for StreamDiffusion: run the export scripts in Operators/StreamDiffusion/tools/streamdiffusionv2 " +
                        "against the downloaded checkpoint (see the README there). The finished export is picked up automatically, " +
                        "and the setup check turns green once it is in the model store.", UiColors.TextMuted);
        }

        if (skipped > 0)
            WrappedText($"{skipped} model(s) were skipped because the setup was cancelled. Run the setup again to install them.", UiColors.TextMuted);

        if (failed > 0)
        {
            FormInputs.AddVerticalSpace(4);
            WrappedText($"{failed} model(s) could not be installed. Partial downloads are kept, so \"Retry failed\" resumes where they stopped.",
                        UiColors.StatusAttention);
        }

        FormInputs.AddVerticalSpace(10);
        var scale = T3Ui.UiScaleFactor;
        DrawQueueList(Math.Max(80 * scale, ImGui.GetContentRegionAvail().Y - 90 * scale));

        FormInputs.AddVerticalSpace(8);
        ImGui.Checkbox("Open the model store folder", ref _openStoreOnFinish);
        FormInputs.AddVerticalSpace(2);
        WrappedText("Add or remove models later under Help › AI Model Setup… or Settings › AI Models.", UiColors.TextMuted);
    }

    // ---------------------------------------------------------------- queue

    private void StartInstall(IReadOnlyList<ModelCatalogEntry> entries)
    {
        if (entries.Count == 0 || _queueTask is { IsCompleted: false })
            return;

        _queue = entries.Select(e => new QueueItem(e, Math.Max(1, e.RemainingDownloadBytes()))).ToList();
        _cancelRequested = false;
        _page = Pages.Installing;
        InvalidateStateCache();

        var queue = _queue;
        _queueTask = Task.Run(() => RunQueueAsync(queue));
    }

    private async Task RunQueueAsync(List<QueueItem> queue)
    {
        try
        {
            foreach (var item in queue)
            {
                if (_cancelRequested)
                {
                    item.State = ItemStates.Skipped;
                    continue;
                }

                item.State = ItemStates.Running;
                Log.Info($"[AI Models] Setup: installing '{item.Entry.DisplayName}'");
                await AiModelDownloadService.GetOrStartTask(item.Entry);

                var status = AiModelDownloadService.GetStatus(item.Entry);
                item.Error = status.Error;
                item.State = status.State switch
                                 {
                                     AiModelDownloadService.States.Completed => ItemStates.Done,
                                     AiModelDownloadService.States.Cancelled => ItemStates.Cancelled,
                                     _                                       => ItemStates.Failed,
                                 };
            }
        }
        catch (Exception e)
        {
            Log.Error($"[AI Models] Setup stopped: {e.Message}");
            foreach (var item in queue.Where(i => i.State is ItemStates.Pending or ItemStates.Running))
            {
                item.State = ItemStates.Failed;
                item.Error = e.Message;
            }
        }
        finally
        {
            AiSetupChecker.Run();
            Log.Info("[AI Models] Setup finished");
        }
    }

    private void CancelInstall()
    {
        _cancelRequested = true;
        foreach (var item in _queue)
        {
            if (item.State == ItemStates.Running)
                AiModelDownloadService.Cancel(item.Entry);
        }
    }

    // ---------------------------------------------------------------- selection

    private enum EntryStates { Selectable, Installed, Bundled, Downloading, ManualExport }

    /**
     * Installation states touch the disk and the asset registry. The wizard reads them several times per
     * row and frame, so they are cached and refreshed at most once a second; a running download is always
     * reported live.
     */
    private static EntryStates EntryState(ModelCatalogEntry entry)
    {
        if (AiModelDownloadService.IsRunning(entry))
            return EntryStates.Downloading;

        RefreshStateCacheIfStale();
        return _stateCache.TryGetValue(entry.Id, out var cached) ? cached.State : ComputeEntryState(entry);
    }

    private static long Remaining(ModelCatalogEntry entry)
    {
        RefreshStateCacheIfStale();
        return _stateCache.TryGetValue(entry.Id, out var cached) ? cached.RemainingBytes : entry.RemainingDownloadBytes();
    }

    private static void InvalidateStateCache() => _stateCacheTime = double.MinValue;

    private static void RefreshStateCacheIfStale()
    {
        var now = ImGui.GetTime();
        if (now - _stateCacheTime < 1.0)
            return;

        _stateCacheTime = now;
        foreach (var entry in AiModelCatalog.Entries)
            _stateCache[entry.Id] = (ComputeEntryState(entry), entry.RemainingDownloadBytes());
    }

    private static readonly Dictionary<string, (EntryStates State, long RemainingBytes)> _stateCache = new();
    private static double _stateCacheTime = double.MinValue;

    private static EntryStates ComputeEntryState(ModelCatalogEntry entry)
    {
        if (entry.RequiresLocalExport || entry.Files.Count == 0)
            return entry.IsInstalled() ? EntryStates.Installed : EntryStates.ManualExport;

        if (AiModelDownloadService.IsRunning(entry))
            return EntryStates.Downloading;

        if (entry.IsInstalled())
            return EntryStates.Installed;

        return entry.IsAvailable() ? EntryStates.Bundled : EntryStates.Selectable;
    }

    private static bool IsSelectable(ModelCatalogEntry entry) => EntryState(entry) == EntryStates.Selectable;

    private void ApplySetupType(SetupTypes type)
    {
        _setupType = type;
        if (type == SetupTypes.Custom)
            return;

        _selected.Clear();
        foreach (var entry in AiModelCatalog.Entries)
        {
            if (!IsSelectable(entry))
                continue;

            if (IsInPreset(entry, type))
                _selected.Add(entry.Id);
        }
    }

    private List<ModelCatalogEntry> SelectedEntries()
        => AiModelCatalog.Entries.Where(e => _selected.Contains(e.Id) && IsSelectable(e)).ToList();

    private long RequiredBytes()
    {
        var bytes = 0L;
        foreach (var entry in AiModelCatalog.Entries)
        {
            if (_selected.Contains(entry.Id))
                bytes += Remaining(entry);
        }

        return bytes;
    }

    private bool IsShortOnSpace => _freeSpaceBytes >= 0 && RequiredBytes() > _freeSpaceBytes;

    private static string SetupTypeTitle(SetupTypes type) => type switch
        {
            SetupTypes.Recommended => "Recommended",
            SetupTypes.Minimal     => "Minimal",
            SetupTypes.Everything  => "Everything",
            _                      => "Custom",
        };

    private static string SetupTypeDescription(SetupTypes type) => type switch
        {
            SetupTypes.Recommended => $"The fastest model of every family ({AiModelCatalog.FormatSize(PresetBytes(type))}).",
            SetupTypes.Minimal     => $"Small depth model and core tracking, no diffusion ({AiModelCatalog.FormatSize(PresetBytes(type))}).",
            SetupTypes.Everything  => $"Every downloadable model ({AiModelCatalog.FormatSize(PresetBytes(type))}).",
            _                      => "Your own selection.",
        };

    /** Download size of a setup type, counting only what is not installed yet. */
    private static long PresetBytes(SetupTypes type)
    {
        var bytes = 0L;
        foreach (var entry in AiModelCatalog.Entries)
        {
            if (IsInPreset(entry, type) && IsSelectable(entry))
                bytes += Remaining(entry);
        }

        return bytes;
    }

    private static bool IsInPreset(ModelCatalogEntry entry, SetupTypes type) => type switch
        {
            SetupTypes.Recommended => entry.IsRecommended,
            SetupTypes.Minimal     => entry.IsMinimal,
            SetupTypes.Everything  => true,
            _                      => false,
        };

    // ---------------------------------------------------------------- helpers

    private bool HasFailures => _queue.Any(i => i.State == ItemStates.Failed);

    private static bool HasCuda()
    {
        if (OperatingSystem.IsMacOS())
            return true; // CoreML; CUDA does not apply

        foreach (var item in AiSetupChecker.LastResults)
        {
            if (item.Title.StartsWith("CUDA", StringComparison.Ordinal))
                return item.Result == AiCheckResult.Ok;
        }

        return true; // not checked: don't warn
    }

    private void RefreshFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(AiModelSettings.ModelsRoot));
            _freeSpaceBytes = string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            _freeSpaceBytes = -1;
        }
    }

    private static void WrappedText(string text, Color? color = null)
    {
        ImGui.PushTextWrapPos(0);
        if (color != null)
            ImGui.TextColored(color.Value, text);
        else
            ImGui.TextUnformatted(text);

        ImGui.PopTextWrapPos();
    }

    private static void DrawDot(Color color)
    {
        var height = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos() + new Vector2(4, height * 0.5f);
        ImGui.GetWindowDrawList().AddCircleFilled(pos, 3.5f * T3Ui.UiScaleFactor, color);
        ImGui.Dummy(new Vector2(14 * T3Ui.UiScaleFactor, height));
        ImGui.SameLine();
    }

    private static string FormatDuration(double seconds) => seconds switch
        {
            < 60   => $"{Math.Max(1, (int)seconds)} s",
            < 3600 => $"{(int)(seconds / 60)} min",
            _      => $"{(int)(seconds / 3600)} h {(int)(seconds % 3600 / 60)} min",
        };

    // ---------------------------------------------------------------- state

    private const float HeaderHeight = 64;
    private const float PageHeight = 430;
    private const float PagePadding = 24;

    private Pages _page = Pages.Welcome;
    private SetupTypes _setupType = SetupTypes.Recommended;
    private readonly HashSet<string> _selected = new();
    private readonly HashSet<AiModelFamily> _collapsedFamilies = new();

    private ModelCatalogEntry? _hoveredEntry;
    private ModelCatalogEntry? _lastHoveredEntry;
    private AiModelFamily? _hoveredFamily;
    private AiModelFamily? _lastHoveredFamily;

    private List<QueueItem> _queue = [];
    private Task? _queueTask;
    private volatile bool _cancelRequested;

    private long _freeSpaceBytes = -1;
    private bool _openStoreOnFinish;
    private bool _openedAutomatically;
}
