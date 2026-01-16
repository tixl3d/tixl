#nullable enable
using System.IO;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

public sealed class StringInputUi : InputValueUi<string>
{
    private const int MaxStringLength = 4000;

    public enum UsageType
    {
        Default,
        FilePath,
        DirectoryPath,
        Multiline,
        CustomDropdown,
    }

    public UsageType Usage { get; private set; } = UsageType.Default;
    public string FileFilter { get; private set; } = string.Empty;

    public override IInputUi Clone()
    {
        return new StringInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Size = Size,
                       Usage = Usage,
                       FileFilter = FileFilter,
                   };
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref string? value, bool readOnly)
    {
        value ??= string.Empty;

        var inputEditStateFlags = InputEditStateFlags.Nothing;

        switch (Usage)
        {
            case UsageType.Default:
                inputEditStateFlags = DrawDefaultTextEdit(ref value) ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
                break;
            case UsageType.Multiline:
                inputEditStateFlags = DrawMultilineTextEdit(ref value);
                break;
            case UsageType.FilePath:
                inputEditStateFlags = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, FileFilter, ref value);
                ImGui.SameLine();
                if (ImGui.Button("Edit"))
                {
                    if (value != null)
                    {
                        ResourceManager.TryResolveRelativePath(value, FilePickingUi.SearchResourceConsumer, out var absolutePath, out _);
                        if (!File.Exists(absolutePath))
                        {
                            Log.Error("Can't open non-existing file " + absolutePath);
                        }
                        else
                        {
                            CoreUi.Instance.OpenWithDefaultApplication(absolutePath);
                        }
                        //OpenFileManager(FileOperations.FilePickerTypes.File, _searchResourceConsumer.AvailableResourcePackages, new string[0], isFolder: false, async: true);
                    }
                }

                NormalizePathSeparators(inputEditStateFlags, ref value);
                break;
            case UsageType.DirectoryPath:
                inputEditStateFlags = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.Folder, FileFilter, ref value);
                
                NormalizePathSeparators(inputEditStateFlags, ref value);
                break;
            case UsageType.CustomDropdown:
                inputEditStateFlags = DrawCustomDropdown(input);
                break;
        }

        inputEditStateFlags |= ImGui.IsItemClicked() ? InputEditStateFlags.Started : InputEditStateFlags.Nothing;
        inputEditStateFlags |= ImGui.IsItemDeactivatedAfterEdit() ? InputEditStateFlags.Finished : InputEditStateFlags.Nothing;

        return inputEditStateFlags;

            
        static void NormalizePathSeparators(InputEditStateFlags inputEditStateFlags, ref string? value)
        {
            if (value == null)
                return;
                
            // normalize path separators when modified
            // use only forward slashes as windows is the only OS that supports backslashes
            if ((inputEditStateFlags & InputEditStateFlags.Modified) == InputEditStateFlags.Modified
                || (inputEditStateFlags & InputEditStateFlags.Finished) == InputEditStateFlags.Finished)
            {
                value = value.Replace('\\', '/');
                    
                // todo: handle trailing slashes
                //if (value.EndsWith('/'))
                //  value = value[..^1];
            }
        }
    }

    private static bool DrawDefaultTextEdit(ref string value)
    {
        return ImGui.InputText("##textEdit", ref value, MaxStringLength);
    }


    
    private static InputEditStateFlags DrawMultilineTextEdit(ref string value)
    {
        // Wrap to next line
        ImGui.Dummy(new Vector2(1, 1));
        
        ImGui.PushFont(Fonts.Code);
        var lineCount = value.LineCount().Clamp(1, 30) + 1;
        var lineHeight = Fonts.Code.FontSize;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
        var requestedContentHeight = lineCount * lineHeight;
        var clampedToWindowHeight = MathF.Min(requestedContentHeight, ImGui.GetWindowSize().Y * 0.5f);
        
        var changed = ImGui.InputTextMultiline("##textEdit", ref value, MaxStringLength, new Vector2(-1, clampedToWindowHeight));
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(3);
        return changed ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
    }

    private static InputEditStateFlags DrawCustomDropdown(Symbol.Child.Input input)
    {
        var instance = ProjectView.Focused?.NodeSelection.GetSelectedInstanceWithoutComposition();
        if (instance != null && instance is ICustomDropdownHolder customValueHolder)
        {
            var changed = false;

            var currentValue = customValueHolder.GetValueForInput(input.InputDefinition.Id) ?? string.Empty;
                
            if (InputWithTypeAheadSearch.Draw("##customDropdown", 
                                              customValueHolder.GetOptionsForInput(input.InputDefinition.Id),
                                              false,
                                              
                                              ref currentValue, out var selected, out var isInList))
            {
                ImGui.CloseCurrentPopup();
                customValueHolder.HandleResultForInput(input.InputDefinition.Id, selected, isInList);
                changed = true;
            }
                
            return changed ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
        }
        else
        {
            ImGui.NewLine();
            //Log.Warning($"{instance?.Parent?.Symbol?.Name} doesn't support custom inputs");
            return InputEditStateFlags.Nothing;
        }
    }

    // private static InputWithTypeAheadSearch.Texts GetTextInfo(string arg)
    // {
    //     return new InputWithTypeAheadSearch.Texts(arg, arg, null);
    // }

    protected override void DrawReadOnlyControl(string name, ref string? value)
    {
        if (value != null)
        {
            ImGui.InputText(name, ref value, MaxStringLength, ImGuiInputTextFlags.ReadOnly);
        }
        else
        {
            var nullString = "<null>";
            ImGui.InputText(name, ref nullString, MaxStringLength, ImGuiInputTextFlags.ReadOnly);
        }
    }

    public override bool DrawSettings()
    {
        var modified= base.DrawSettings();
        FormInputs.AddVerticalSpace();

        FormInputs.DrawFieldSetHeader("Usage");
        {
            var tmpForRef = Usage;
            if (FormInputs.AddEnumDropdown(ref tmpForRef, null))
            {
                modified = true;
                Usage = tmpForRef;
            }
        }

        
        if (Usage == UsageType.FilePath)
        {
            FormInputs.DrawFieldSetHeader("File Filter");
            
            var tmp = FileFilter;
            var warning = !string.IsNullOrEmpty(tmp) && !tmp.Contains('|')
                              ? "Filter must include at least one | symbol.\nPlease read tooltip for examples"
                              : null;

            if (FormInputs.AddStringInput("##File Filter", ref tmp, null, warning,
                                          "This will only work for file FilePath-Mode.\nThe filter has to be in following format:\n\n Your Description (*.ext)|*.ext"))
            {
                modified = true;
                FileFilter = tmp;
            }
        }

        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        writer.WriteObject(nameof(Usage), Usage.ToString());

        if (!string.IsNullOrEmpty(FileFilter))
            writer.WriteObject(nameof(FileFilter), FileFilter);
    }

    public override void Read(JToken? inputToken)
    {
        if (inputToken == null)
            return;

        base.Read(inputToken);

        var usageEnumToken = inputToken[nameof(Usage)];
        if (usageEnumToken != null && Enum.TryParse<UsageType>(usageEnumToken.Value<string>(), out var enumValue))
        {
            Usage = enumValue;
        }

        FileFilter = inputToken[nameof(FileFilter)]?.Value<string>() ?? string.Empty;
    }
}