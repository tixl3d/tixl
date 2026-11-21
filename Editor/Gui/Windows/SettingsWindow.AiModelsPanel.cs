#nullable enable
using System;
using System.IO;
using System.Linq;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.AiModels;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows;

// ReSharper disable once UnusedType.Global
internal sealed partial class SettingsWindow
{
    /** Which family the model browser lists; All shows every entry grouped by family. */
    private enum AiModelBrowserFilter { All, DepthAnything, StreamDiffusion }

    private static AiModelBrowserFilter _aiBrowserFilter = AiModelBrowserFilter.All;

    private static long _aiStoreSizeBytes;
    private static bool _aiStoreSizeValid;
    private static bool _aiAutoCheckDone;

    private static void DrawAiModelsPanel(ref bool changed)
    {
        var aiConfigChanged = false;

        DrawAiRecommendedSection();
        DrawAiSetupCheckSection();
        DrawAiModelBrowserSection();
        DrawAiAdvancedSection(ref aiConfigChanged);

        if (aiConfigChanged)
        {
            changed = true;
            AiModelSettings.Save();
            AiModelSettings.ResetRuntimeProbes();
        }
    }

    // ---------------------------------------------------------------- model store

    private static void DrawAiStoreSection(ref bool aiConfigChanged)
    {
        PanelLayout.BeginSection("Model Store",
                                 "Where downloaded AI models live. Set this to a drive with room to spare - models are multi-gigabyte and deliberately kept out of the TiXL installation and the git repository. The operators find them here automatically.");

        var previousDirectory = AiModelSettings.Config.ModelsDirectory;
        aiConfigChanged |= FormInputs.AddFilePicker("Store folder",
                                            ref AiModelSettings.Config.ModelsDirectory,
                                            $"(default: {AiModelSettings.ModelsRoot})",
                                            null,
                                            "Models download into one subfolder per operator package here.",
                                            FileOperations.FilePickerTypes.Folder);

        if (!string.Equals(previousDirectory, AiModelSettings.Config.ModelsDirectory, StringComparison.Ordinal))
        {
            _aiStoreSizeValid = false;
            try
            {
                Directory.CreateDirectory(AiModelSettings.ModelsRoot);
            }
            catch (Exception e)
            {
                Log.Error($"[AI Models] Could not create model store folder '{AiModelSettings.ModelsRoot}': {e.Message}");
            }
        }

        FormInputs.ApplyIndent();
        if (ImGui.Button("Open folder"))
            CoreUi.Instance.OpenWithDefaultApplication(AiModelSettings.ModelsRoot);

        CustomComponents.TooltipForLastItem("Open the model store in the file browser.", AiModelSettings.ModelsRoot);

        ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);
        if (!_aiStoreSizeValid)
        {
            if (ImGui.Button("Calculate size"))
            {
                _aiStoreSizeBytes = GetDirectorySize(AiModelSettings.ModelsRoot);
                _aiStoreSizeValid = true;
            }
        }
        else
        {
            var sizeText = AiModelCatalog.FormatSize(_aiStoreSizeBytes);
            ImGui.TextUnformatted($"Store size: {sizeText}");
            ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);
            if (ImGui.Button("Refresh"))
                _aiStoreSizeValid = false;
        }

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------- your computer / engine recommendation

    private static void DrawAiRecommendedSection()
    {
        PanelLayout.BeginSection("Your computer",
                                 "The AI operators automatically use the fastest engine this machine can run: "
                               + "TensorRT, CUDA, DirectML on AMD/Intel GPUs, CoreML on macOS, or the CPU. "
                               + "This shows what is active and what would make it faster.");

        var recommendation = AiEngineAdvisor.GetRecommendation();
        var pillColor = recommendation.Engine == AiEngineAdvisor.Engine.Cpu ? UiColors.StatusWarning : UiColors.StatusControlled;
        PanelLayout.StatusPill(recommendation.Title, pillColor);

        var gpuNames = AiEngineAdvisor.GetGpuNames();
        if (gpuNames.Length > 0)
        {
            ImGui.SameLine(0, 10 * T3Ui.UiScaleFactor);
            ImGui.TextColored(UiColors.TextMuted, string.Join("  ·  ", gpuNames));
        }

        CustomComponents.TooltipForLastItem($"{recommendation.Title} - the engine the AI operators use on this machine",
                                            recommendation.Summary);

        PanelLayout.Space(PanelLayout.SpaceXS);
        CustomComponents.HelpText(recommendation.Summary);

        if (!string.IsNullOrEmpty(recommendation.Hint))
        {
            FormInputs.AddVerticalSpace(2);
            CustomComponents.HelpText(recommendation.Hint);
            if (recommendation.ActionUrl != null)
            {
                FormInputs.ApplyIndent();
                if (ImGui.SmallButton("Show me…"))
                    CoreUi.Instance.OpenWithDefaultApplication(recommendation.ActionUrl);

                CustomComponents.TooltipForLastItem("Opens the download page in your browser.",
                                                    recommendation.ActionUrl);
            }
        }

        PanelLayout.Space(PanelLayout.SpaceM);

        // The engine choice itself needs no user action: the operators resolve it per machine. The old
        // DirectML preference checkbox is gone - the current model graphs cannot run through DirectML.
        CustomComponents.HelpText("The operators pick this engine on their own when they start - no setup needed. "
                                + "You can pin a specific engine in each AI operator's \"Execution provider\" dropdown "
                                + "(the default \"Auto\" uses the fastest one available here).");

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------- setup check / one-click

    private static void DrawAiSetupCheckSection()
    {
        PanelLayout.BeginSection("AI models",
                                 "Everything the operators need beside the engine: a model per AI family. "
                               + "\"Download missing models\" installs the fastest default of every family in one click; "
                               + "the wizard lets you pick models individually. The MediaPipe tracking models ship bundled with TiXL and need no setup.");

        // Run once per session when the page is first opened, so the state of things is
        // visible without a click. Re-checking stays on the button.
        if (!_aiAutoCheckDone)
        {
            _aiAutoCheckDone = true;
            AiSetupChecker.Run();
        }

        if (AiSetupChecker.IsFixing)
        {
            DrawStatusLine(UiColors.StatusActivated, AiSetupChecker.FixProgressText.Length > 0 ? AiSetupChecker.FixProgressText : "Working…");
            PanelLayout.Space(PanelLayout.SpaceS);
        }

        if (AiSetupChecker.LastCheckAt == DateTime.MinValue)
        {
            PanelLayout.Space(PanelLayout.SpaceS);
            CustomComponents.HelpText("Not checked yet in this session.");
            PanelLayout.EndSection();
            return;
        }

        // Engine rows are covered by the "Your computer" section; the compact list shows only the store
        // and model rows, with everything OK collapsed into a single line.
        var modelRows = AiSetupChecker.LastResults.Where(i => i.Kind != AiSetupCheckItem.CheckKind.Engine).ToList();
        var problemRows = modelRows.Where(i => i.Result is AiCheckResult.Fixable or AiCheckResult.Missing).ToList();

        var checking = AiSetupChecker.IsFixing;
        if (problemRows.Count == 0)
        {
            DrawStatusLine(UiColors.StatusControlled, "All models ready");
            CustomComponents.TooltipForLastItem("Model status",
                                                string.Join("\n", modelRows.Select(i => $"{i.ResultText}: {i.Title} - {i.Detail}")));
        }
        else
        {
            foreach (var item in problemRows)
            {
                var color = item.Result == AiCheckResult.Missing ? UiColors.StatusAttention : UiColors.StatusWarning;
                DrawStatusLine(color, item.Title);
                CustomComponents.TooltipForLastItem(item.Title, item.Detail);

                // The "what to do" guidance sits directly on the page, not in a tooltip
                if (!string.IsNullOrWhiteSpace(item.Detail))
                {
                    ImGui.Indent();
                    CustomComponents.HelpText(item.Detail);
                    ImGui.Unindent();
                }
            }
        }

        PanelLayout.Space(PanelLayout.SpaceM);
        FormInputs.ApplyIndent();
        if (checking)
        {
            ImGui.BeginDisabled();
        }

        if (problemRows.Any(i => i.EntryToInstall != null))
        {
            var pendingSize = problemRows.Where(i => i.EntryToInstall != null)
                                         .Sum(i => i.EntryToInstall!.RemainingDownloadBytes());
            if (CustomComponents.DrawCtaButton("Download missing models", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
            {
                if (AiSetupChecker.LastCheckAt == DateTime.MinValue)
                    AiSetupChecker.Run();

                _ = AiSetupChecker.FixAllAsync();
            }

            CustomComponents.TooltipForLastItem("Downloads the fastest default model for every AI family that has none.",
                                                $"About {AiModelCatalog.FormatSize(pendingSize)} into the model store - no restart needed.");
            ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);
        }

        if (ImGui.Button("Setup wizard…"))
        {
            T3Ui.AiModelSetupWizard.ShowNextFrame();
        }

        CustomComponents.TooltipForLastItem("Choose the models to install, installer-style, with a description of each.",
                                            "Downloads go into the model store and continue if the wizard is closed.");
        ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);

        if (ImGui.Button("Run check"))
        {
            AiSetupChecker.Run();
        }

        CustomComponents.TooltipForLastItem("Probes the engine runtimes and checks which models are installed.",
                                            "Probing CUDA/TensorRT loads the runtime DLLs, exactly like the operators do later.");

        if (checking)
        {
            ImGui.EndDisabled();
        }

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------- advanced: folders and runtimes

    private static void DrawAiAdvancedSection(ref bool aiConfigChanged)
    {
        if (!PanelLayout.BeginCollapsibleSection("Advanced: folders and runtimes",
                                                 "The engine runtimes are found automatically (PATH, CUDA_PATH, TENSORRT_ROOT, the NVIDIA install folders, the shipped DirectML build). "
                                               + "These options only matter when a runtime lives outside those places, or to place the model store on another drive.",
                                                 defaultOpen: false))
            return;

        // ---- model store
        DrawAiStoreSection(ref aiConfigChanged);

        FormInputs.AddVerticalSpace();

        // ---- engine runtime folders: the auto-detection makes them optional
        aiConfigChanged |= FormInputs.AddFilePicker("CUDA runtime folder",
                                            ref AiModelSettings.Config.CudaRuntimeDirectory,
                                            "(search PATH and CUDA_PATH)",
                                            null,
                                            "Folder containing cudart64_12.dll and cudnn64_9.dll. Checked before every standard location.",
                                            FileOperations.FilePickerTypes.Folder);

        aiConfigChanged |= FormInputs.AddFilePicker("TensorRT runtime folder",
                                            ref AiModelSettings.Config.TensorRtDirectory,
                                            "(search TENSORRT_ROOT and installs)",
                                            null,
                                            "Folder containing nvinfer_10.dll. Checked before TENSORRT_ROOT and the standard installs.",
                                            FileOperations.FilePickerTypes.Folder);

        if (OperatingSystem.IsWindows())
        {
            aiConfigChanged |= FormInputs.AddFilePicker("DirectML.dll folder",
                                                ref AiModelSettings.Config.DirectMlDirectory,
                                                "(use the shipped copy)",
                                                null,
                                                "Preferred over the DirectML.dll shipped with the DirectML build.",
                                                FileOperations.FilePickerTypes.Folder);
        }

        FormInputs.ApplyIndent();
        if (ImGui.Button("Re-detect runtimes"))
        {
            AiModelSettings.ResetRuntimeProbes();
            AiSetupChecker.Run();
        }

        CustomComponents.TooltipForLastItem("Re-probes CUDA and TensorRT after installing or moving a runtime.",
                                            "Operators pick the change up on their next use without a restart. "
                                          + "In automatic engine mode the recommendation follows the new probes immediately.");

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------- browser

    private static void DrawAiModelBrowserSection()
    {
        if (!PanelLayout.BeginCollapsibleSection("All models",
                                                 "Compatible models for the AI operators, grouped by family. Downloads land in the model store; the operators find them automatically.",
                                                 defaultOpen: false))
            return;

        CustomComponents.SegmentedButton(ref _aiBrowserFilter);
        PanelLayout.Space(PanelLayout.SpaceM);

        switch (_aiBrowserFilter)
        {
            case AiModelBrowserFilter.All:
                foreach (var family in AiModelCatalog.VisibleFamilies)
                {
                    DrawFamilyGroup(family);
                }
                break;

            // Explicit mapping: the browser filter and the family enum have different bases.
            case AiModelBrowserFilter.DepthAnything:
                DrawFamilyGroup(AiModelFamily.DepthAnything);
                break;

            case AiModelBrowserFilter.StreamDiffusion:
                DrawFamilyGroup(AiModelFamily.StreamDiffusion);
                break;
        }

        PanelLayout.EndSection();
    }

    private static void DrawFamilyGroup(AiModelFamily family)
    {
        var entries = AiModelCatalog.ForFamily(family);
        if (entries.Count == 0)
            return;

        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(family.Title());
        ImGui.PopFont();
        CustomComponents.TooltipForLastItem(family.Title(), family.Description());
        PanelLayout.Space(PanelLayout.SpaceS);

        foreach (var entry in entries)
        {
            DrawCatalogEntry(entry);
        }

        PanelLayout.Space(PanelLayout.SpaceS);
    }

    private static void DrawCatalogEntry(ModelCatalogEntry entry)
    {
        ImGui.PushID(entry.Id);

        var rowStartX = ImGui.GetCursorPosX();
        var rowAvailX = ImGui.GetContentRegionAvail().X;
        var status = AiModelDownloadService.GetStatus(entry);
        var running = AiModelDownloadService.IsRunning(entry);
        var installed = entry.IsInstalled();
        var bundled = !installed && entry.IsAvailable();

        // --- first row: name + size + action button on the right edge
        ImGui.TextUnformatted(entry.DisplayName);
        if (entry.TotalSizeBytes > 0)
        {
            ImGui.SameLine(0, 8 * T3Ui.UiScaleFactor);
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(UiColors.TextMuted, AiModelCatalog.FormatSize(entry.TotalSizeBytes));
            ImGui.PopFont();
        }

        CustomComponents.TooltipForLastItem(entry.DisplayName, entry.Description);

        // Right-aligned action on the name line, pinned to the row's right edge
        const float actionWidth = 104;
        var actionSize = new Vector2(actionWidth * T3Ui.UiScaleFactor, 0);
        var buttonX = Math.Max(ImGui.GetCursorPosX() + 8 * T3Ui.UiScaleFactor,
                               rowStartX + rowAvailX - actionWidth * T3Ui.UiScaleFactor);
        ImGui.SameLine(buttonX);

        if (entry.RequiresLocalExport)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(installed ? UiColors.StatusControlled : UiColors.TextMuted,
                              installed ? "export present" : "own export");
            ImGui.PopFont();
        }
        else if (running)
        {
            if (ImGui.Button("Cancel", actionSize))
                AiModelDownloadService.Cancel(entry);

            CustomComponents.TooltipForLastItem("Stop the download.",
                                                "Partial files are removed; already completed files of this model stay and a retry only fetches the rest.");
        }
        else if (status.State == AiModelDownloadService.States.Failed)
        {
            if (ImGui.Button("Retry##failed", actionSize))
                AiModelDownloadService.Start(entry);

            CustomComponents.TooltipForLastItem("Download failed", status.Error);
        }
        else if (installed)
        {
            if (PanelLayout.DestructiveButton("Remove", actionWidth))
                AiModelDownloadService.Remove(entry);

            CustomComponents.TooltipForLastItem("Delete the installed model from the store.", entry.StoreDirectory);
        }
        else
        {
            var downloadLabel = bundled ? "Copy to store" : "Download";
            if (ImGui.Button(downloadLabel, actionSize))
                AiModelDownloadService.Start(entry);

            if (bundled)
                CustomComponents.TooltipForLastItem("The model ships with TiXL. A copy in the store also covers trimmed installations.",
                                                    entry.StoreDirectory);
            else if (entry.IsDefault)
                CustomComponents.TooltipForLastItem("Download this model", "Installed as the family's default by the one-click setup.");
        }

        // --- second row: description / progress
        if (running)
        {
            var barWidth = ImGui.GetContentRegionAvail().X;
            if (barWidth > 0)
                ImGui.ProgressBar(status.Progress, new Vector2(barWidth, ImGui.GetFrameHeight() * 0.6f), "");

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(UiColors.TextMuted, status.CurrentFile.Length > 0 ? status.CurrentFile : "preparing…");
            ImGui.PopFont();
        }
        else if (status.State == AiModelDownloadService.States.Failed)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextColored(UiColors.StatusAttention, $"Download failed: {status.Error}");
            ImGui.PopFont();
        }
        else
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.SetCursorPosX(rowStartX + 14 * T3Ui.UiScaleFactor);
            ImGui.PushTextWrapPos(rowStartX + rowAvailX);
            var statusText = installed ? "Installed"
                             : bundled ? "Bundled with TiXL"
                             : entry.RequiresLocalExport ? entry.Description
                             : entry.IsDefault ? $"{entry.Description}  Default for the one-click setup."
                             : entry.Description;
            ImGui.TextColored(UiColors.TextMuted, statusText);
            ImGui.PopTextWrapPos();
            ImGui.PopFont();
        }

        PanelLayout.Space(PanelLayout.SpaceM);
        ImGui.PopID();
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;

            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
