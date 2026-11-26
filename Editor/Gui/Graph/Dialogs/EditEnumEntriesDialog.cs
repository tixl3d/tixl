#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Enums;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Graph.Dialogs;

/// <summary>
/// Edits the entries of an enum an operator declares inside its class, e.g. the one an int parameter is shown as a
/// dropdown with, or of a new one that an int parameter switching from Value to Dropdown or Switch will be shown with.
/// Nothing is changed until Apply, which rewrites the operator's source and recompiles it.
/// </summary>
internal sealed class EditEnumEntriesDialog : ModalDialog
{
    public EditEnumEntriesDialog()
    {
        DialogSize = new Vector2(520, 400);
    }

    public void Draw()
    {
        if (BeginDialog("Edit Enum Entries"))
        {
            DrawContent();
            EndDialogContent();
        }

        EndDialog();
    }

    public void ShowNextFrame(Symbol symbol, Type enumType)
    {
        _isNewMapping = false;
        _symbolId = symbol.Id;
        _symbolName = symbol.Name;
        _enumName = enumType.Name;
        _isFlagEnum = enumType.IsDefined(typeof(FlagsAttribute), false);
        _hasCustomCode = InputsAndOutputs.HasCustomCode(symbol);
        _originalEntries = InputsAndOutputs.GetEnumEntries(enumType);
        _compileError = null;

        _rows.Clear();
        foreach (var entry in _originalEntries)
        {
            _rows.Add(new Row(_nextRowId++, entry.Name, entry.Value));
        }

        ShowNextFrame();
    }

    /// <summary>
    /// Starts a new enum for an int input that has none. The entries cover the input's Min to Max range if that's
    /// small, otherwise a few placeholders.
    /// </summary>
    public void ShowNextFrameForNewEnum(Symbol symbol, Guid inputId, string inputName, int min, int max, EnumDisplayMode displayMode)
    {
        _isNewMapping = true;
        _inputId = inputId;
        _displayModeOnApply = displayMode;
        _symbolId = symbol.Id;
        _symbolName = symbol.Name;
        _enumName = InputsAndOutputs.GetNameForNewEnum(symbol, inputName);
        _isFlagEnum = false;
        _hasCustomCode = false;
        _originalEntries = [];
        _compileError = null;

        var isRangeUsable = min >= 0 && max > min && max - min < MaxEntriesFromRange;
        var first = isRangeUsable ? min : 0;
        var last = isRangeUsable ? max : 2;
        _rows.Clear();
        for (var value = first; value <= last; value++)
        {
            _rows.Add(new Row(_nextRowId++, "Option" + value, value));
        }

        ShowNextFrame();
    }

    private void DrawContent()
    {
        FormInputs.SetIndentToLeft();
        CustomComponents.StylizedText($"{_symbolName}.{_enumName}", Fonts.FontBold, UiColors.Text);
        if (_isNewMapping)
        {
            FormInputs.AddHint($"Applying adds this enum to {_symbolName} and shows the parameter with its entries. "
                               + "Each entry stands for its value.");
        }
        else
        {
            FormInputs.AddHint("Entries keep their value when reordered. Changing values or deleting entries changes what existing "
                               + "instances show. Keeping them consistent is up to you.");
        }

        if (_hasCustomCode)
            FormInputs.AddHint($"{_symbolName} has custom code. Renaming or deleting entries it uses will fail to compile.");

        FormInputs.AddVerticalSpace();
        DrawRows();

        if (CustomComponents.IconButton(Icon.Plus, new Vector2(FormInputs.ListRowHeight)))
            AddRow();

        CustomComponents.TooltipForLastItem("Add entry");

        var validationError = GetValidationError();
        if (validationError != null)
            CustomComponents.StylizedText(validationError, Fonts.FontSmall, UiColors.StatusAttention);

        if (_compileError != null)
        {
            FormInputs.AddVerticalSpace();
            CustomComponents.StylizedText("Compilation failed, nothing was changed.", Fonts.FontBold, UiColors.StatusAttention);
            using (new ChildWindowScope("compileError", new Vector2(0, 120 * T3Ui.UiScaleFactor), ImGuiWindowFlags.None, UiColors.BackgroundInputField, 4))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped(_compileError);
                ImGui.PopStyleColor();
            }
        }

        FormInputs.AddVerticalSpace();
        AlignCtaButtons("Cancel", "Apply");
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Apply", validationError == null && HasChanges()))
            Apply();
    }

    private void DrawRows()
    {
        var scale = T3Ui.UiScaleFactor;
        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = FormInputs.ListRowHeight;
        var gripWidth = FormInputs.ListRowGripWidth;
        var valueWidth = MathF.Round(ValueColumnWidth * scale);
        var listWidth = ImGui.GetContentRegionAvail().X;
        var nameWidth = MathF.Max(listWidth - gripWidth - valueWidth - rowHeight, 1);
        var fieldOffsetY = MathF.Floor((rowHeight - ImGui.GetFrameHeight()) * 0.5f);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, MathF.Round(RowGap * scale)));
        var listScreenX = ImGui.GetCursorScreenPos().X;
        var rowsTop = MathF.Floor(ImGui.GetCursorScreenPos().Y);
        var rowStride = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        var count = _rows.Count;
        var moveFromIndex = -1;
        var moveToIndex = -1;
        var deleteIndex = -1;

        for (var index = 0; index < count; index++)
        {
            var row = _rows[index];
            ImGui.PushID(row.Id);
            ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + index * rowStride));
            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowMax = rowMin + new Vector2(listWidth, rowHeight).Floor();

            var isDragging = FormInputs.DrawRowDragGrip(index, count, rowHeight, ref moveFromIndex, ref moveToIndex);
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging, drawGrip: count > 1);

            var isNameInvalid = !InputsAndOutputs.IsValidEnumEntryName(row.Name) || IsNameUsedBefore(index);
            if (isNameInvalid)
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);

            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + gripWidth, rowMin.Y + fieldOffsetY));
            ImGui.SetNextItemWidth(nameWidth);
            ImGui.InputText("##name", ref row.Name, 256);
            if (isNameInvalid)
                ImGui.PopStyleColor();

            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + gripWidth + nameWidth, rowMin.Y + fieldOffsetY));
            ImGui.SetNextItemWidth(valueWidth);
            ImGui.InputInt("##value", ref row.Value, 0, 0);
            CustomComponents.TooltipForLastItem("Value", "The int the parameter holds for this entry.");

            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - rowHeight, rowMin.Y));
            if (CustomComponents.IconButton(Icon.Trash, new Vector2(rowHeight)))
                deleteIndex = index;

            CustomComponents.TooltipForLastItem("Delete entry");
            ImGui.PopID();
        }

        ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + count * rowStride));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopStyleVar();

        if (moveFromIndex >= 0)
            (_rows[moveFromIndex], _rows[moveToIndex]) = (_rows[moveToIndex], _rows[moveFromIndex]);

        if (deleteIndex >= 0)
            _rows.RemoveAt(deleteIndex);
    }

    private void AddRow()
    {
        var maxValue = int.MinValue;
        foreach (var row in _rows)
        {
            maxValue = Math.Max(maxValue, row.Value);
        }

        int value;
        if (_rows.Count == 0)
        {
            value = _isFlagEnum ? 1 : 0;
        }
        else if (_isFlagEnum)
        {
            value = maxValue <= 0 ? 1 : (int)Math.Min(BitOperations.RoundUpToPowerOf2((uint)maxValue + 1), int.MaxValue);
        }
        else
        {
            value = maxValue == int.MaxValue ? maxValue : maxValue + 1;
        }

        var name = "NewEntry";
        for (var suffix = 2; IsNameUsed(name); suffix++)
        {
            name = "NewEntry" + suffix;
        }

        _rows.Add(new Row(_nextRowId++, name, value));
    }

    private void Apply()
    {
        var newEntries = new InputsAndOutputs.EnumEntry[_rows.Count];
        for (var index = 0; index < _rows.Count; index++)
        {
            newEntries[index] = new InputsAndOutputs.EnumEntry(_rows[index].Name, _rows[index].Value);
        }

        if (_isNewMapping)
        {
            var mapCommand = MapInputToEnumCommand.ForNewEnum(_symbolId, _inputId, _enumName, newEntries);
            if (!mapCommand.TryDo(out var mapError))
            {
                ShowFailure(mapError);
                return;
            }

            UndoRedoStack.Add(mapCommand);
            ApplyDisplayModeToInput();
            ImGui.CloseCurrentPopup();
            return;
        }

        var command = new ChangeEnumEntriesCommand(_symbolId, _enumName, _originalEntries, newEntries);
        if (!command.TryDo(out var error))
        {
            ShowFailure(error);
            return;
        }

        UndoRedoStack.Add(command);
        ImGui.CloseCurrentPopup();
    }

    private void ShowFailure(string error)
    {
        Log.Warning($"Editing enum {_symbolName}.{_enumName} failed: {error}");
        _compileError = error;
    }

    /** Resolved after the recompile, which may have replaced the input's ui. */
    private void ApplyDisplayModeToInput()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi)
            || !symbolUi.InputUis.TryGetValue(_inputId, out var inputUi)
            || inputUi is not IntInputUi intInputUi)
            return;

        intInputUi.SetDisplayMode(_displayModeOnApply);
        symbolUi.FlagAsModified();
    }

    private string? GetValidationError()
    {
        if (_rows.Count == 0)
            return "An enum needs at least one entry.";

        for (var index = 0; index < _rows.Count; index++)
        {
            if (!InputsAndOutputs.IsValidEnumEntryName(_rows[index].Name))
                return "Entry names must be valid C# identifiers.";

            if (IsNameUsedBefore(index))
                return "Entry names must be unique.";
        }

        return null;
    }

    private bool HasChanges()
    {
        if (_rows.Count != _originalEntries.Length)
            return true;

        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Name != _originalEntries[index].Name || _rows[index].Value != _originalEntries[index].Value)
                return true;
        }

        return false;
    }

    private bool IsNameUsedBefore(int index)
    {
        for (var other = 0; other < index; other++)
        {
            if (_rows[other].Name == _rows[index].Name)
                return true;
        }

        return false;
    }

    private bool IsNameUsed(string name)
    {
        foreach (var row in _rows)
        {
            if (row.Name == name)
                return true;
        }

        return false;
    }

    /** Mutable so ImGui can edit the fields by ref; the id keeps a row's widget state while rows move. */
    private sealed class Row(int id, string name, int value)
    {
        public readonly int Id = id;
        public string Name = name;
        public int Value = value;
    }

    private const float ValueColumnWidth = 80;
    private const int MaxEntriesFromRange = 16;
    private const float RowGap = 2;

    private readonly List<Row> _rows = [];
    private InputsAndOutputs.EnumEntry[] _originalEntries = [];
    private Guid _symbolId;
    private Guid _inputId;
    private bool _isNewMapping;
    private EnumDisplayMode _displayModeOnApply;
    private string _symbolName = string.Empty;
    private string _enumName = string.Empty;
    private bool _isFlagEnum;
    private bool _hasCustomCode;
    private string? _compileError;
    private int _nextRowId;
}
