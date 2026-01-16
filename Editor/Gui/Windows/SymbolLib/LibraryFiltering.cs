#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Windows.SymbolLib;

/// <summary>
/// Provides UI for filtering operators in the symbol library by issues such as missing descriptions,
/// invalid references, or obsolete dependencies.
/// </summary>
/// <remarks>
/// Only shown after scanning the operator library by pressing the update icon in the symbol library window.
/// </remarks>
internal sealed class LibraryFiltering
{

    internal LibraryFiltering(SymbolLibrary symbolLibrary)
    {
        _symbolLibrary = symbolLibrary;
    }

    /// <summary>
    /// Draws the filter toggle button and, when active, the full set of problem filters and updates the filtered tree.
    /// </summary>
    internal void DrawSymbolFilters()
    {
        ImGui.SameLine();
        var status = _showFilters ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;

        if (CustomComponents.IconButton(Icon.Flame, Vector2.Zero, status))
            _showFilters = !_showFilters;

        CustomComponents.TooltipForLastItem(
            "Show problem filters",
            "Allows filter operators to different problems and attributes.");

        if (!_showFilters)
            return;

        ImGui.Indent();

        var opInfos = SymbolAnalysis.InformationForSymbolIds.Values;

        var totalOpCount = _onlyInLib
                               ? opInfos.Count(i => i.OperatorType == SymbolAnalysis.OperatorClassification.Lib)
                               : opInfos.Count;

        CustomComponents.SmallGroupHeader($"Out of {totalOpCount} show those with...");

        var needsUpdate = false;

        // Missing overall help/description
        needsUpdate |= DrawFilterToggle(
            "Help missing ({0})",
            opInfos.Count(i => i.LacksDescription
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.MissingDescriptions,
            ref _activeFilters);

        // Missing all parameter descriptions
        needsUpdate |= DrawFilterToggle(
            "Parameter help missing ({0})",
            opInfos.Count(i => i.LacksAllParameterDescription
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.MissingAllParameterDescriptions,
            ref _activeFilters);

        // Incomplete parameter descriptions (some missing)
        needsUpdate |= DrawFilterToggle(
            "Parameter help incomplete",
            0,
            Flags.MissingSomeParameterDescriptions,
            ref _activeFilters);

        // No grouping of parameters
        needsUpdate |= DrawFilterToggle(
            "No grouping ({0})",
            opInfos.Count(i => i.LacksParameterGrouping
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.MissingParameterGrouping,
            ref _activeFilters);

        // Unused operators (no dependents)
        needsUpdate |= DrawFilterToggle(
            "Unused ({0})",
            opInfos.Count(i => i.DependingSymbols.Count == 0
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.Unused,
            ref _activeFilters);

        // Invalid required operator references
        needsUpdate |= DrawFilterToggle(
            "Invalid Op dependencies ({0})",
            opInfos.Count(i => i.InvalidRequiredIds.Count > 0
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.InvalidRequiredOps,
            ref _activeFilters);

        // Depends on obsolete operators
        needsUpdate |= DrawFilterToggle(
            "Depends on obsolete ops ({0})",
            opInfos.Count(i => i.DependsOnObsoleteOps
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.DependsOnObsoleteOps,
            ref _activeFilters);

        FormInputs.AddVerticalSpace(5);

        // Obsolete operators
        needsUpdate |= DrawFilterToggle(
            "Obsolete ({0})",
            opInfos.Count(i => i.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.Obsolete,
            ref _activeFilters);

        // Operators marked as NeedsFix
        needsUpdate |= DrawFilterToggle(
            "NeedsFix ({0})",
            opInfos.Count(i => i.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix)
                               && (i.OperatorType == SymbolAnalysis.OperatorClassification.Lib || !_onlyInLib)),
            Flags.NeedsFix,
            ref _activeFilters);

        FormInputs.AddVerticalSpace(5);

        // Restrict filters to Lib namespace operators only
        needsUpdate |= ImGui.Checkbox("Only in Lib", ref _onlyInLib);

        ImGui.Unindent();

        if (needsUpdate)
        {
            _symbolLibrary.FilteredTree.PopulateCompleteTree(s =>
            {
                // Ensure info exists and classification is available for this symbol
                if (!SymbolAnalysis.TryGetSymbolInfo(s.Symbol, out var info))
                    return false;

                if (_onlyInLib && info.OperatorType != SymbolAnalysis.OperatorClassification.Lib)
                    return false;

                if (!AnyFilterActive)
                    return true;

                return
                    _activeFilters.HasFlag(Flags.MissingDescriptions) && info.LacksDescription
                    || _activeFilters.HasFlag(Flags.MissingAllParameterDescriptions) && info.LacksAllParameterDescription
                    || _activeFilters.HasFlag(Flags.MissingSomeParameterDescriptions) && info.LacksSomeParameterDescription
                    || _activeFilters.HasFlag(Flags.MissingParameterGrouping) && info.LacksParameterGrouping
                    || _activeFilters.HasFlag(Flags.InvalidRequiredOps) && info.InvalidRequiredIds.Count > 0
                    || _activeFilters.HasFlag(Flags.Unused) && info.DependingSymbols.Count == 0
                    || _activeFilters.HasFlag(Flags.Obsolete) && info.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                    || _activeFilters.HasFlag(Flags.NeedsFix) && info.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix)
                    || _activeFilters.HasFlag(Flags.DependsOnObsoleteOps) && info.DependsOnObsoleteOps;
            });
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace();
        CustomComponents.SmallGroupHeader("Result...");
    }

    /// <summary>
    /// Draws a checkbox-style toggle for a specific filter flag and updates the active filter mask.
    /// </summary>
    /// <param name="label">The label template for the checkbox; uses <c>string.Format</c> with the count value.</param>
    /// <param name="count">The number of operators that currently match this filter.</param>
    /// <param name="filterFlag">The filter flag represented by this toggle.</param>
    /// <param name="activeFlags">The full set of currently active filter flags.</param>
    /// <returns>
    /// True if the user clicked the checkbox (state changed); otherwise false.
    /// </returns>
    private static bool DrawFilterToggle(string label, int count, Flags filterFlag, ref Flags activeFlags)
    {
        var isActive = activeFlags.HasFlag(filterFlag);
        var clicked = ImGui.Checkbox(string.Format(label, count), ref isActive);
        if (clicked)
        {
            activeFlags ^= filterFlag;
        }

        return clicked;
    }

    /// <summary>
    /// Flags that represent individual filter criteria, combined as a bit mask.
    /// </summary>
    [Flags]
    private enum Flags
    {
        None = 0,
        MissingDescriptions = 1 << 1,
        MissingAllParameterDescriptions = 1 << 2,
        MissingSomeParameterDescriptions = 1 << 3,
        MissingParameterGrouping = 1 << 4,
        InvalidRequiredOps = 1 << 5,
        Unused = 1 << 6,
        Obsolete = 1 << 7,
        NeedsFix = 1 << 8,
        DependsOnObsoleteOps = 1 << 9,
    }

    /// <summary>
    /// Gets a value indicating whether any filter flag is currently active.
    /// </summary>
    internal bool AnyFilterActive => _activeFilters != Flags.None;

    /// <summary>
    /// Backing reference to the symbol library whose operators are filtered.
    /// </summary>
    private readonly SymbolLibrary _symbolLibrary;

    /// <summary>
    /// Indicates whether only operators in the Lib namespace should be included in filter results.
    /// </summary>
    private bool _onlyInLib = true;

    /// <summary>
    /// Bit mask of currently active filter flags.
    /// </summary>
    private Flags _activeFilters;

    /// <summary>
    /// Indicates whether the filter UI section is currently expanded and visible.
    /// </summary>
    private bool _showFilters;
}
