#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Windows.SymbolLib;

/// <summary>
/// A fun little experiment that shows random prompt suggestions, when entering '?' into the search field.
/// The number of characters defines the count. The relevancy threshold limits operator to a meaningful set. 
/// </summary>
internal sealed class RandomPromptGenerator(SymbolFilter filter)
{
    private int _randomSeed;
    private List<Symbol>? _allLibSymbols;
    private float _promptComplexity = 0.25f;

    internal void DrawRandomPromptList()
    {
        ImGui.Indent();
        FormInputs.AddSectionHeader("Random Prompts");

        var listNeedsUpdate = _allLibSymbols == null;
        FormInputs.SetIndent(80 * T3Ui.UiScaleFactor);
        FormInputs.AddInt("Seed", ref _randomSeed);
        listNeedsUpdate |= FormInputs.AddFloat("Complexity", ref _promptComplexity, 0, 1, 0.02f, true, true);
        FormInputs.SetIndentToLeft();

        FormInputs.AddVerticalSpace();

        // Rebuild list if necessary
        if (listNeedsUpdate)
        {
            // Count all lib ops
            if (_allLibSymbols == null)
            {
                _allLibSymbols = new List<Symbol>();
                foreach (var s in EditorSymbolPackage.AllSymbols)
                {
                    if (s.Namespace.StartsWith("Lib.") && !s.Name.StartsWith("_"))
                        _allLibSymbols.Add(s);
                }
            }

            // Filter 
            var limit = (int)(_allLibSymbols.Count * _promptComplexity).Clamp(1, _allLibSymbols.Count - 1);
            var keep = filter.SearchString;
            filter.SearchString = "Lib.";
            filter.UpdateIfNecessary(null, true, limit);
            filter.SearchString = keep;
        }

        var relevantCount = filter.MatchingSymbolUis.Count;

        if (_randomSeed == 0)
        {
            _randomSeed = (int)(ImGui.GetFrameCount() * 374761393U & 1023U);
        }

        var promptCount = filter.SearchString.Count(c => c == '?');
        for (uint i = 0; i < promptCount; i++)
        {
            var f = MathUtils.Hash01((uint)((i + 42 * _randomSeed * 668265263U) & 0x7fffffff));
            var randomIndex = (int)(f * relevantCount).Clamp(0, relevantCount - 1);
            SymbolLibrary.DrawSymbolItem(filter.MatchingSymbolUis[randomIndex].Symbol);
        }
    }
}