#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Dialogs;

internal static class SymbolModificationInputs
{
    public static bool DrawFieldInputs(Symbol symbol, [NotNullWhen(true)] ref string parameterName, [NotNullWhen(true)] ref Type selectedType, out bool isValid)
    {
        var changed = DrawFieldNameInput(symbol, ref parameterName, out var isNameValid);
        changed |= DrawTypeInput(ref selectedType, out var hasType);
        isValid = isNameValid && hasType;
        return changed;
    }

    private static bool DrawTypeInput(ref Type selectedType, out bool isValid)
    {
        FormInputs.DrawInputLabel("Type");

        var previous = selectedType;
        TypeSelector.Draw(ref selectedType);
        var changed = previous != selectedType;
        isValid = selectedType != null;
        return changed;
    }

    public static bool DrawFieldNameInput(Symbol symbol, ref string parameterName, out bool isValid)
    {
        var tmp = parameterName;
        var changed = FormInputs.AddStringInput("Name", ref tmp);
        parameterName = tmp ?? string.Empty;
        
        CustomComponents.HelpText("This is a C# field name. It must be unique and not include spaces or special characters");

        isValid = GraphUtils.IsNewFieldNameValid(parameterName, symbol, out var reason);
        if (!isValid)
        {
            ImGui.TextColored(UiColors.StatusWarning, reason);
        }

        return changed;
    }

    public static bool DrawSymbolNameAndNamespaceInputs(ref string newTypeName, ref string newNamespace, EditableSymbolProject destinationProject,
                                                        out bool isValid)
    {
        var changed = DrawNamespaceInput(ref newNamespace, destinationProject, false, out var namespaceCorrect);

        var autoFocus = ImGui.IsWindowAppearing();
        changed |= DrawSymbolNameInput(ref newTypeName, newNamespace, destinationProject, autoFocus, out var symbolNameValid);
        isValid = namespaceCorrect && symbolNameValid;
        return changed;
    }

    public static bool DrawNamespaceInput(ref string newNamespace, EditableSymbolProject destinationProject, bool needsToBeUnique, out bool isValid)
    {
        var rootNamespace = destinationProject.CsProjectFile.RootNamespace;
        var isNamespaceCorrect = IsNamespaceCorrect(newNamespace, rootNamespace, needsToBeUnique);

        var changed = FormInputs.AddStringInputWithSuggestions("Namespace",
                                                               ref newNamespace,
                                                               destinationProject.SymbolUis.Values
                                                                                 .Select(x => x.Symbol)
                                                                                 .Select(i => i.Namespace)
                                                                                 .Distinct()
                                                                                 .OrderBy(i => i),
                                                               "Sub.Category",
                                                               isNamespaceCorrect ? null : "Namespace not correct",
                                                               """
                                                               This needs to be a valid c# namespaces without spaces or special characters.

                                                               Items should not start with a number and be separated by a .
                                                               """,
                                                               null);

        if (changed)
        {
            isNamespaceCorrect = IsNamespaceCorrect(newNamespace, rootNamespace, needsToBeUnique);
        }

        isValid = isNamespaceCorrect;
        return changed;

        static bool IsNamespaceCorrect(string? newNamespace, string rootNamespace, bool needsToBeUnique)
        {
            return newNamespace != null 
                   && newNamespace.StartsWith(rootNamespace) 
                   && GraphUtils.IsNamespaceValid(newNamespace, needsToBeUnique, out _);
        }
    }

    public static bool DrawSymbolNameInput(ref string newTypeName, string destinationNamespace, SymbolPackage destinationPackage, bool focus, out bool isValid)
    {
        isValid = GraphUtils.IsNewSymbolNameValid(newTypeName, destinationNamespace, destinationPackage, out var invalidReason);

        var changed = FormInputs.AddStringInput("Symbol name",
                                                ref newTypeName!,
                                                "MySymbolName",
                                                invalidReason,
                                                """
                                                This is a C# class name.
                                                
                                                It must be unique and not include spaces or special characters.
                                                We suggested to use PascalCase.
                                                """,
                                                null,
                                                focus);

        return changed;
    }

    public static bool DrawProjectDropdown(ref string nameSpace, ref EditableSymbolProject? projectToCopyTo)
    {
        var projectChanged = CustomComponents.DrawProjectDropdown(ref projectToCopyTo);
        if (projectChanged && projectToCopyTo != null)
        {
            nameSpace = projectToCopyTo.CsProjectFile.RootNamespace;
        }

        return projectChanged;
    }
}