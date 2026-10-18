#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Helpers;
using T3.IoServices.Midi;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Creates, renames or merges a MIDI device alias, rewriting the affected inputs in all loaded symbols.
/// </summary>
internal sealed class MidiAliasDialog : ModalDialog
{
    private enum Modes
    {
        /// <summary>A new, unused alias.</summary>
        Add,

        /// <summary>Replace a physical device name in all loaded symbols with an alias.</summary>
        ReplaceDeviceName,

        /// <summary>Rename an alias in all loaded symbols; renaming onto an existing alias merges the two.</summary>
        RenameAlias,
    }

    internal void ShowForNewAlias()
    {
        _mode = Modes.Add;
        _sourceName = null;
        _shortName = string.Empty;
        _productName = string.Empty;
        ShowNextFrame();
    }

    /// <summary>Opens the dialog to replace <paramref name="deviceName"/> with an alias in every rewritable input.</summary>
    internal void ShowForDeviceName(string deviceName)
    {
        _mode = Modes.ReplaceDeviceName;
        _sourceName = deviceName;
        _shortName = SuggestAliasName(deviceName);
        _productName = MidiDeviceCombo.IsConnected(deviceName) ? deviceName : string.Empty;
        ShowNextFrame();
    }

    /// <summary>Opens the dialog to rename <paramref name="alias"/>, or merge it into another alias, in every rewritable input.</summary>
    internal void ShowForAlias(string alias)
    {
        _mode = Modes.RenameAlias;
        _sourceName = alias;
        _shortName = MidiDeviceAlias.WithoutPrefix(alias);
        _productName = MidiDeviceAliases.TryFind(alias, out var entry) ? entry!.ProductName : string.Empty;
        ShowNextFrame();
    }

    protected override void OnShowNextFrame()
    {
        // Existing aliases as type-ahead suggestions, so a typo or case mismatch can be resolved by picking the right one
        _suggestedNames.Clear();
        foreach (var entry in MidiDeviceAliases.Entries)
        {
            _suggestedNames.Add(MidiDeviceAlias.WithoutPrefix(entry.Alias));
        }

        foreach (var usage in MidiDeviceUsage.AliasUsages)
        {
            var shortName = MidiDeviceAlias.WithoutPrefix(usage.DeviceName);
            if (!_suggestedNames.Contains(shortName))
                _suggestedNames.Add(shortName);
        }

        _suggestedNames.Sort(StringComparer.OrdinalIgnoreCase);
        _orderedSuggestions = _suggestedNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns true when the alias table changed.</summary>
    internal bool Draw()
    {
        DialogSize = new Vector2(520, 200);
        var changed = false;

        var title = _mode switch
                        {
                            Modes.ReplaceDeviceName => "Replace Device Name with Alias",
                            Modes.RenameAlias       => "Rename Alias",
                            _                       => "Add MIDI Alias",
                        };

        if (BeginDialog(title))
        {
            // The settings panel draws its sections at the left edge; form rows here need the label column
            FormInputs.SetIndentToParameters();

            if (_sourceName != null)
            {
                ImGui.TextUnformatted(_mode == Modes.RenameAlias ? $"Alias: {_sourceName}" : $"Device name: {_sourceName}");
                FormInputs.AddVerticalSpace(4);
            }

            var fullAlias = MidiDeviceAlias.WithPrefix(_shortName.Trim());
            var isValid = IsValidShortName(_shortName.Trim());
            var isUnchanged = _mode == Modes.RenameAlias && fullAlias == _sourceName;
            MidiDeviceAlias? targetEntry = null;
            var targetExists = !isUnchanged && MidiDeviceAliases.TryFind(fullAlias, out targetEntry);
            var isMerge = targetExists && _mode == Modes.RenameAlias;
            var warning = isValid
                              ? null
                              : "Letters, digits, - and _ only";

            FormInputs.AddStringInputWithSuggestions("Alias",
                                                     ref _shortName,
                                                     _orderedSuggestions!,
                                                     "ApcMini",
                                                     warning,
                                                     $"Stored in projects as {fullAlias}. Choose the role of the device rather than its model. Pick an existing alias to merge into it.",
                                                     null,
                                                     ImGui.IsWindowAppearing());

            var keepsTargetMapping = isMerge && !string.IsNullOrEmpty(targetEntry!.ProductName);
            if (keepsTargetMapping)
            {
                CustomComponents.HelpText($"Merges into the existing alias, which stays mapped to {targetEntry!.ProductName}.");
            }
            else
            {
                if (targetExists)
                    CustomComponents.HelpText("This alias already exists. Its device mapping is updated.");

                FormInputs.DrawInputLabel("Device");
                MidiDeviceCombo.Draw("##aliasDevice", ref _productName, 260 * T3Ui.UiScaleFactor);
            }

            if (_sourceName != null && MidiDeviceUsage.TryGetUsage(_sourceName, out var usage))
            {
                FormInputs.AddVerticalSpace();
                FormInputs.ApplyIndent();
                ImGui.TextWrapped(BuildReplaceSummary(usage));
            }

            FormInputs.AddVerticalSpace();
            FormInputs.ApplyIndent();

            var confirmLabel = _mode switch
                                   {
                                       Modes.ReplaceDeviceName => "Replace and Save",
                                       Modes.RenameAlias       => isMerge ? "Merge and Save" : "Rename and Save",
                                       _                       => "Add",
                                   };

            if (CustomComponents.DrawCtaButton(confirmLabel, isValid && !isUnchanged, true))
            {
                UpsertAlias(fullAlias, keepsTargetMapping ? string.Empty : _productName);
                if (_sourceName != null)
                {
                    var count = MidiDeviceUsage.ReplaceDeviceName(_sourceName, fullAlias);
                    Log.Info($"Replaced MIDI device '{_sourceName}' with '{fullAlias}' in {count} inputs.");
                }

                if (_mode == Modes.RenameAlias && MidiDeviceAliases.Remove(_sourceName!))
                    MidiDeviceAliases.Save();

                changed = true;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
                ImGui.CloseCurrentPopup();

            EndDialogContent();
        }

        EndDialog();
        return changed;
    }

    /// <summary>
    /// A readable PascalCase alias from a product name: version tokens and the word MIDI are dropped,
    /// so "APC MINI MK2" becomes "ApcMini".
    /// </summary>
    internal static string SuggestAliasName(string productName)
    {
        var sb = new StringBuilder();
        foreach (var token in productName.Split(_tokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (_droppedTokens.IsMatch(token))
                continue;

            var wordStarted = false;
            foreach (var c in token)
            {
                if (!char.IsLetterOrDigit(c))
                    continue;

                sb.Append(wordStarted ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
                wordStarted = true;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "Device";
    }

    private static bool IsValidShortName(string shortName)
    {
        if (shortName.Length == 0)
            return false;

        foreach (var c in shortName)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        return true;
    }

    private static string BuildReplaceSummary(MidiDeviceUsage.DeviceUsage usage)
    {
        var sb = new StringBuilder();
        sb.Append($"Replaces the device name in {usage.RewritableCount} inputs. The modified symbols are saved; the change is one undo step.");
        if (usage.ReadOnlyCount > 0)
            sb.Append($"\n{usage.ReadOnlyCount} usages in read-only packages are skipped.");

        if (usage.ConnectedCount > 0)
            sb.Append($"\n{usage.ConnectedCount} connected inputs are skipped.");

        return sb.ToString();
    }

    private static void UpsertAlias(string alias, string productName)
    {
        if (MidiDeviceAliases.TryFind(alias, out var entry))
        {
            if (!string.IsNullOrEmpty(productName))
                entry!.ProductName = productName;
        }
        else
        {
            MidiDeviceAliases.Entries.Add(new MidiDeviceAlias { Alias = alias, ProductName = productName });
        }

        MidiDeviceAliases.Save();
    }

    private Modes _mode;
    private string? _sourceName;
    private string _shortName = string.Empty;
    private string _productName = string.Empty;
    private readonly List<string> _suggestedNames = new();
    private IOrderedEnumerable<string>? _orderedSuggestions;

    private static readonly char[] _tokenSeparators = [' ', '-', '_', '.', '/', ':', '(', ')', '[', ']', ','];
    private static readonly Regex _droppedTokens = new("^(mk\\d*|mkii|mkiii|v\\d+|\\d+|midi)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
