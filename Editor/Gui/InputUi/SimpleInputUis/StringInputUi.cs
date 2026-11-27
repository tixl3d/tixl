#nullable enable
using System.IO;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
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
    private const int MaxStringLength = 32000;

    private static Instance? _drawnInstance;

    public enum UsageType
    {
        Default,
        FilePath,
        DirectoryPath,
        Multiline,
        CustomDropdown,

        /// <summary>
        /// A file the operator writes. Not an asset dependency: it may not exist yet and is never bundled on export.
        /// </summary>
        OutputFilePath,

        /// <summary>
        /// A MIDI device name or alias. Lets the settings panel find every device reference across loaded projects.
        /// </summary>
        MidiDevice,

        /// <summary>
        /// Source code, e.g. the snippet of a Custom* op. Drawn like <see cref="Multiline"/>, and marked when the operator
        /// reports a problem with it through <see cref="IInputStatusProvider"/>. <see cref="CodeLanguage"/> names the language.
        /// </summary>
        Code,
    }

    public UsageType Usage { get; private set; } = UsageType.Default;
    public string FileFilter { get; set; } = string.Empty;

    /// <summary>The language of a <see cref="UsageType.Code"/> input, e.g. "Hlsl" or "CSharp"; empty when unknown.</summary>
    public string CodeLanguage { get; set; } = string.Empty;

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
                       CodeLanguage = CodeLanguage,
                   };
    }

    /** Keeps the drawn op for the code controls: a pinned parameter window can show another op than the selected one. */
    protected override InputEditStateFlags DrawValueControl(string name, InputSlot<string> inputSlot, ref string? value, bool readOnly)
    {
        _drawnInstance = inputSlot.Parent;
        var result = base.DrawValueControl(name, inputSlot, ref value, readOnly);
        _drawnInstance = null;
        return result;
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
                inputEditStateFlags = DrawMultilineTextEdit(ref value, null);
                break;
            case UsageType.Code:
                var hasStatus = TryGetInputStatus(input, out var status);
                inputEditStateFlags = DrawCodeEditorButton(input, ref value, hasStatus ? status : null);
                inputEditStateFlags |= DrawMultilineTextEdit(ref value, hasStatus ? status : null, startsOnNewLine: true);
                break;
            case UsageType.FilePath:
                inputEditStateFlags = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, FileFilter, ref value);
                ImGui.SameLine();
                if (ImGui.Button("Edit"))
                {
                    if (value != null)
                    {
                        AssetRegistry.TryResolveAddress(value, FilePickingUi.SearchResourceConsumer, out var absolutePath, out _);
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
            case UsageType.OutputFilePath:
                inputEditStateFlags = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, FileFilter, ref value, isOutputPath: true);
                ImGui.SameLine();
                if (ImGui.Button("Open"))
                {
                    // Written files register as assets once they exist, so the read resolver is enough here
                    if (AssetRegistry.TryResolveAddress(value, FilePickingUi.SearchResourceConsumer, out var absolutePath, out _))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(absolutePath);
                    }
                    else
                    {
                        Log.Warning($"File '{value}' hasn't been written yet");
                    }
                }

                NormalizePathSeparators(inputEditStateFlags, ref value);
                break;
            case UsageType.DirectoryPath:
                inputEditStateFlags = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.Folder, FileFilter, ref value);
                
                NormalizePathSeparators(inputEditStateFlags, ref value);
                break;
            case UsageType.CustomDropdown:
            case UsageType.MidiDevice:
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


    
    /// <summary>
    /// An icon at the right end of the name row that opens the larger <see cref="CodeEditorPopup"/>, tinted when the code
    /// has a problem. The popup is drawn here too, before the inline field, so that field stays the last item.
    /// </summary>
    private InputEditStateFlags DrawCodeEditorButton(Symbol.Child.Input input, ref string value, InputStatus? status)
    {
        var instance = GetDrawnInstance();
        if (instance == null)
        {
            // Without the icon, nothing ends the name row for the field below.
            ImGui.Dummy(new Vector2(1, 1));
            return InputEditStateFlags.Nothing;
        }

        var iconWidth = MathF.Round(Icons.FontSize + 4 * T3Ui.UiScaleFactor);
        var origin = ImGui.GetCursorScreenPos();
        var availableWidth = ImGui.GetContentRegionAvail().X - InputArea.ValueEditRightMargin;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + availableWidth - iconWidth, origin.Y));

        var hasError = status is { IsError: true, Message.Length: > 0 };
        var clicked = hasError
                          ? CustomComponents.IconButton(Icon.PopUp, new Vector2(iconWidth, ImGui.GetFrameHeight()), UiColors.StatusError)
                          : CustomComponents.IconButton(Icon.PopUp, new Vector2(iconWidth, ImGui.GetFrameHeight()));
        if (clicked)
            CodeEditorPopup.Open(instance, input.InputDefinition.Id, new Vector2(origin.X, ImGui.GetItemRectMax().Y + 4 * T3Ui.UiScaleFactor));

        CustomComponents.TooltipForLastItem("Edit code", "A larger editor with line numbers and every problem the compiler reported.");

        return CodeEditorPopup.Draw(instance, input.InputDefinition.Id, input.Name, CodeLanguage, ref value, status);
    }

    private static Instance? GetDrawnInstance()
    {
        return _drawnInstance ?? ProjectView.Focused?.NodeSelection.GetSelectedInstanceWithoutComposition();
    }

    /// <summary>What the drawn operator reports for this input, e.g. a snippet that doesn't compile.</summary>
    private static bool TryGetInputStatus(Symbol.Child.Input input, out InputStatus status)
    {
        var instance = GetDrawnInstance();
        status = default;
        return instance != null && CodeInputStatus.TryGet(instance, input.InputDefinition.Id, out status);
    }

    /// <param name="status">An error marks the field with a border and shows the message below it; a note is shown dimmed.</param>
    /// <param name="startsOnNewLine">False when the field follows the parameter name on its row and must wrap below it.</param>
    private static InputEditStateFlags DrawMultilineTextEdit(ref string value, InputStatus? status, bool startsOnNewLine = false)
    {
        if (!startsOnNewLine)
            ImGui.Dummy(new Vector2(1, 1));
        
        ImGui.PushFont(Fonts.Code);
        var lineCount = value.LineCount().Clamp(1, 30) + 1;
        var lineHeight = Fonts.Code.FontSize;
        var requestedContentHeight = lineCount * lineHeight;
        var clampedToWindowHeight = MathF.Min(requestedContentHeight, ImGui.GetWindowSize().Y * 0.5f);
        
        var hasError = status is { IsError: true, Message.Length: > 0 };
        var marksErrorLine = hasError && TryMarkErrorLine(value, status!.Value.Line, lineHeight, clampedToWindowHeight);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, (marksErrorLine ? Color.Transparent : UiColors.BackgroundInputField).Rgba);
        if (hasError)
        {
            ImGui.PushStyleColor(ImGuiCol.Border, UiColors.StatusError.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1);
        }

        var changed = ImGui.InputTextMultiline("##textEdit", ref value, MaxStringLength, new Vector2(-1, clampedToWindowHeight));
        if (hasError)
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor();
        ImGui.PopFont();
        if (status is { Message.Length: > 0 } shown)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, (shown.IsError ? UiColors.StatusError : UiColors.TextMuted).Rgba);
            ImGui.TextWrapped(shown.Message);
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }

        FormInputs.AddVerticalSpace(3);
        return changed ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
    }

    /// <summary>
    /// Draws the field background with the error line highlighted, so the field must be drawn with a transparent background.
    /// Skipped when the text doesn't fit, because the field then scrolls it by an offset that can't be read from here.
    /// </summary>
    private static bool TryMarkErrorLine(string value, int errorLine, float lineHeight, float fieldHeight)
    {
        var framePaddingY = ImGui.GetStyle().FramePadding.Y;
        if (errorLine <= 0 || errorLine > value.LineCount() || value.LineCount() * lineHeight + 2 * framePaddingY > fieldHeight)
            return false;

        var fieldMin = ImGui.GetCursorScreenPos();
        var fieldMax = fieldMin + new Vector2(ImGui.GetContentRegionAvail().X, fieldHeight);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(fieldMin, fieldMax, UiColors.BackgroundInputField);

        var rowTop = MathF.Round(fieldMin.Y + framePaddingY + (errorLine - 1) * lineHeight);
        drawList.AddRectFilled(new Vector2(fieldMin.X, rowTop), new Vector2(fieldMax.X, rowTop + lineHeight), UiColors.StatusError.Fade(0.18f));
        return true;
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

        
        if (Usage is UsageType.FilePath or UsageType.OutputFilePath)
        {
            FormInputs.DrawFieldSetHeader("File Filter");
            
            var tmp = FileFilter;
            /*var warning = !string.IsNullOrEmpty(tmp) && !tmp.Contains('|')
                              ? "Filter must include at least one | symbol.\nPlease read tooltip for examples"
                              : null;*/

            if (FormInputs.AddStringInput("##File Filter", ref tmp, null, null,
                                          "This will only work for file FilePath-Mode.\nSeparate multiple extensions by a comma.(example: png, jpg, dds)"))
            {
                modified = true;
                FileFilter = tmp;
            }
        }

        if (Usage == UsageType.Code)
        {
            FormInputs.DrawFieldSetHeader("Code Language");
            var language = CodeLanguage;
            if (FormInputs.AddStringInput("##Code Language", ref language, null, null,
                                          "Language of the code, e.g. Hlsl or CSharp. Used for highlighting; empty for plain text."))
            {
                modified = true;
                CodeLanguage = language;
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

        if (!string.IsNullOrEmpty(CodeLanguage))
            writer.WriteObject(nameof(CodeLanguage), CodeLanguage);
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
        CodeLanguage = inputToken[nameof(CodeLanguage)]?.Value<string>() ?? string.Empty;
    }
}