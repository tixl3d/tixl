using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;

// ReSharper disable StringLiteralTypo
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable ArrangeTypeMemberModifiers

namespace T3.Editor.Gui.Windows;

internal static class ConfigurationTest
{
    #region user interface
    public static void Draw()
    {
        FormInputs.SetIndentToParameters();
        ImGui.Indent(20);
        FormInputs.AddSectionHeader("Category test");

        if (!_initialized)
        {
            ImportFromCsv();
            _initialized = true;
        }

        if (ImGui.TreeNode("Definition"))
        {
            if (ImGui.InputTextMultiline("Options (Tab Separated)", ref _optionsAsCvs, 100000, new Vector2(800, 200)))
            {
                ImportFromCsv();
            }

            ImGui.TreePop();
        }

        DrawCategorySelection();
        DrawSelectedOptions();
    }

    private static void DrawCategorySelection()
    {
        if (!ImGui.TreeNode("Categories:"))
            return;

        foreach (var c in _categories)
        {
            TryGetValidOptionsForCategory(c, out var validOptions);
            if (validOptions.Count < 2)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.Text(c.Id + $" ({validOptions.Count})");
                ImGui.PopStyleColor();
                continue;
            }

            var isCurrent = _selectedCategory == c;

            var suffix = validOptions.Count > 0
                             ? $" ({validOptions.Count} options)"
                             : string.Empty;

            if (ImGui.Selectable($"{c.Id} {suffix}", isCurrent))
                _selectedCategory = c;

            if (isCurrent)
                DrawCategoryOptions(validOptions);
        }

        ImGui.TreePop();
    }

    private static void DrawCategoryOptions(List<Option> validOptions)
    {
        ImGui.Indent();
        foreach (var categoryOption in validOptions)
        {
            var isOptionSelected = _selectedOptions.Contains(categoryOption);
            if (!ImGui.Selectable(categoryOption.Id, isOptionSelected))
                continue;

            if (!isOptionSelected)
            {
                SelectOption(categoryOption);
            }
        }

        ImGui.Unindent();
    }

    private static void DrawSelectedOptions()
    {
        if (!ImGui.TreeNode("SelectedOptions"))
            return;

        FormInputs.AddCheckBox("Show all", ref _showAllOptions);

        foreach (var option in _options)
        {
            var isSelected = _selectedOptions.Contains(option);
            if (!_showAllOptions && !isSelected)
                continue;

            var suffix = option.AllPreconditionOptions.Count > 0
                             ? $" ({option.AllPreconditionOptions.Count} required)"
                             : string.Empty;

            {
                ImGui.Selectable(option.Id + suffix, isSelected);
            }

            if (!ImGui.IsItemHovered())
                continue;

            var linked = string.Empty;
            if (option.AllPreconditionOptions != null)
            {
                linked = string.Join('\n', option.AllPreconditionOptions.Select(c => c.Id));
            }

            CustomComponents.TooltipForLastItem("Required:\n" + linked);
        }

        ImGui.TreePop();
    }
    #endregion

    #region  modification
    
    /// <summary>
    /// Activate option, potentially deactivating the option already set of category  
    /// </summary>
    private static void SelectOption(Option newOption)
    {
        if (newOption == null)
        {
            Log.Warning("Can't set undefined option");
            return;
        }

        var currentCategory = newOption.Category;

        var alreadySelected = false;
        for (var index = _selectedOptions.Count - 1; index >= 0; index--)
        {
            var selectedOption = _selectedOptions[index];
            if (selectedOption.Category != currentCategory)
                continue;

            alreadySelected |= selectedOption == newOption;
            if (!alreadySelected)
                _selectedOptions.RemoveAt(index);
        }

        if (!alreadySelected)
            _selectedOptions.Add(newOption);

        ClearInvalidOptions();
        PopulateActiveCategoryOptions();
    }

    private static void ClearInvalidOptions()
    {
        for (var index = _selectedOptions.Count - 1; index >= 0; index--)
        {
            var o = _selectedOptions[index];
            if (!IsOptionValid(o))
                _selectedOptions.RemoveAt(index);
        }
    }

    /** Make sure at least one option is set for each valid(?) category*/
    private static void PopulateActiveCategoryOptions()
    {
        foreach (var c in _categories)
        {
            if (!TryGetValidOptionsForCategory(c, out var validOptions))
                continue;

            if (validOptions.Any(o => _selectedOptions.Contains(o)))
                continue;

            _selectedOptions.Add(validOptions[0]);
        }
    }

    private static bool TryGetValidOptionsForCategory(Category c, out List<Option> result)
    {
        result = new List<Option>();

        foreach (var o in _options)
        {
            if (o.Category != c)
                continue;

            var isOptionInvalid = o.AllPreconditionOptions.Any(ro => !_selectedOptions.Contains(ro));
            if (!isOptionInvalid)
                result.Add(o);
        }

        return result.Count > 0;
    }

    private static bool IsOptionValid(Option option)
    {
        return option.AllPreconditionOptions.All(required => _selectedOptions.Contains(required));
    }
    #endregion

    #region importing
    private static void ImportFromCsv()
    {
        _options.Clear();
        _categories.Clear();

        // First pass collect all options and categories
        foreach (var line in _optionsAsCvs.Split("\n"))
        {
            if (!ExtractOptionAttributes(line, out var optionId, out var optionTypeId, out var categoryId, out var preconditionOptionId))
                continue;

            if (_options.Any(o => o.Id == optionId))
            {
                Log.Warning($"option {optionId} already defined");
                continue;
            }

            var category = _categories.FirstOrDefault(c => c.Id == categoryId);
            if(category == null)
            {
                category = new Category { Id = categoryId };
                _categories.Add(category);
            }

            var newOption = new Option { Id = optionId, Category = category, PreconditionOptionId = preconditionOptionId };
            _options.Add(newOption);
        }

        // Second pass: initialize required options
        foreach (var option in _options)
        {
            if (option == null)
            {
                Log.Warning("Huh?! Option null");
                continue;
            }

            if (string.IsNullOrEmpty(option.PreconditionOptionId))
                continue;

            var requiredOption = _options.FirstOrDefault(o => o.Id == option.PreconditionOptionId);
            
            if (requiredOption == null)
            {
                Log.Warning($"Can't find required option {option.PreconditionOptionId}");
                continue;
            }

            option.RequiredOption = requiredOption;
        }

        // 3rd pass: collect all requirements
        foreach (var option in _options)
        {
            var link = option;
            var maxSteps = 100;

            while (true)
            {
                var next = link.RequiredOption;
                if (next == null)
                    break;

                if (maxSteps == 0)
                {
                    Log.Warning("Cycle?");
                    break;
                }

                maxSteps--;

                option.AllPreconditionOptions.Add(next);
                link = next;
            }
        }

        // Initialize with default gender
        if (_options.Count == 0)
        {
            Log.Warning("Couldn't find any options");
            return;
        }

        _selectedOptions.Add(_options[0]);
        ClearInvalidOptions();
        PopulateActiveCategoryOptions();
    }

    private static bool ExtractOptionAttributes(string line, out string id, out string optionType, out string category, out string precondition)
    {
        id = null;
        optionType = null;
        category = null;
        precondition = null;

        var t = line.Split("\t");
        if (t.Length < 3)
        {
            Log.Warning($"invalid line format '{line}'");
            return false;
        }

        id = t[0].Trim();
        optionType = t[1].Trim();
        category = t[2].Trim();
        precondition = t.Length > 2 ? t[3].Trim() : null;
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(category))
            return true;

        Log.Warning("Invalid option or category");
        return false;
    }
    #endregion

    private static bool _initialized;
    private static bool _showAllOptions;
    private static Category _selectedCategory;
    private static List<Option> _selectedOptions = new();
    private static List<Option> _options = new();
    private static List<Category> _categories = new();
    
    private static string _optionsAsCvs = @"
Male	Group	Gender	
Female	Group	Gender	
FemaleChar1	Style	BodyStyle	Female
FemaleChar2	Style	BodyStyle	Female
MaleChar1	Style	BodyStyle	Male
MaleChar2	Style	BodyColor	Male
FemaleChar1_PonyTail	Style	HairStyle	Female
FemaleChar1_Short	Style	HairStyle	Female
FemaleChar2_Bobcut	Style	HairStyle	Female
MaleChar1_Mullet	Style	HairStyle	Male
MaleChar1_Normal	Style	HairStyle	Male
MaleChar2_PonyTail	Style	HairStyle	Male
FemaleChar1_PonyTail_Blond	Color	HairColor	Female
FemaleChar1_PonyTail_Brown	Color	HairColor	Female
FemaleChar1_Short_Brown	Color	HairColor	Female
FemaleChar2_Bobcut_Brown	Color	HairColor	Female
MaleChar1_Mullet_Brown	Color	HairColor	MaleChar1_Mullet
MaleChar1_Normal_Black	Color	HairColor	MaleChar1_Normal
MaleChar2_PonyTail_Blond	Color	HairColor	MaleChar2_PonyTail
MaleJumper	Style	ClothingStyle	Male
MaleSuit	Style	ClothingStyle	Male
MaleShirt	Style	ClothingStyle	Male
FemaleJumper	Style	ClothingStyle	Female
FemaleShirt	Style	ClothingStyle	Female
FemaleSuit	Style	ClothingStyle	Female
MaleJacket_Blue	Color	ClothingColor	MaleSuit
MaleJacket_Black	Color	ClothingColor	MaleSuit
Tie_Red	Color	TieColor	MaleSuit
Tie_Blue	Color	TieColor	MaleSuit
MaleJumper_Beige	Color	ClothingColor	MaleJumper
MaleShirt_White	Color	ShirtColor	MaleJumper
MaleJumper_Beige	Color	ShirtColor	MaleJumper
MaleShirt_White	Color	ShirtColor	MaleJumper
MalePants_Black	Color	ClothingColor	MaleJumper
MalePants_Blue	Color	ClothingColor	MaleJumper
Beard_None	Style	BeardStyle	Male
Beard_Goatee	Style	BeardStyle	Male
Beard_Full	Style	BeardStyle	Male
Beard_Goattee_Blonde	Color	BeardColor	Beard_Goatee
Beard_Goattee_Black	Color	BodyColor	Beard_Goatee
Beard_Full_Brown	Color	BodyColor	Beard_Full";

    private sealed class Option
    {
        public string Id;
        public Category Category;
        public List<Option> AllPreconditionOptions = new();
        public Option RequiredOption;
        public string PreconditionOptionId;

        public override string ToString()
        {
            return Id + " (Option)";
        }
    }

    private sealed class Category
    {
        public string Id;

        public override string ToString()
        {
            return Id;
        }
    }
}