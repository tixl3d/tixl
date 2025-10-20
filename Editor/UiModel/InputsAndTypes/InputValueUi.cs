#nullable enable
using System.Diagnostics;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui;
using T3.Editor.Gui.Graph.Interaction;
using T3.Editor.Gui.Graph.Legacy.Interaction.Connections;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Animation;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.Serialization;

namespace T3.Editor.UiModel.InputsAndTypes;

/// <summary>
/// The abstract implementation for drawing and serializing parameters. 
/// </summary>
public abstract class InputValueUi<T> : IInputUi
{
    #region Serialized parameter properties
    /** Defines position of inputNode within graph */
    public Vector2 PosOnCanvas { get; set; } = Vector2.Zero;

    public Vector2 Size { get; set; } = SymbolUi.Child.DefaultOpSize;

    /** Defines when input slots are visible in graph */
    public Relevancy Relevancy { get; set; } = Relevancy.Optional;

    /** If not empty adds a group headline above parameter */
    public string? GroupTitle { get; set; }

    /** Adds a gap above parameter */
    public bool AddPadding { get; set; }

    public string? Description { get; set; }

    public bool ExcludedFromPresets { get; set; }
    #endregion

    private static float ParameterNameWidth => MathF.Max(ImGui.GetTextLineHeight() * 130.0f / 16, ImGui.GetWindowWidth() * 0.35f);

    public SymbolUi? Parent { get; set; }
    public Symbol.InputDefinition InputDefinition { get; set; } = default!; // should be not null after initialization
    public Guid Id => InputDefinition.Id;
    public virtual bool IsAnimatable => false;
    protected Type? MappedType { get; private set; }

    public abstract IInputUi Clone();

    public virtual void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        Log.Warning(IsAnimatable
                        ? "Input type has no implementation to insert values into animation curves"
                        : "Should only be called for animated input types");
    }

    /// <summary>
    /// Wraps the implementation of an parameter control to handle <see cref="InputEditStateFlags"/>
    /// </summary>
    protected abstract InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref T? value, bool readOnly);

    protected abstract void DrawReadOnlyControl(string name, ref T? value);

    protected virtual string GetSlotValueAsString(ref T value)
    {
        return string.Empty;
    }

    protected virtual InputEditStateFlags DrawAnimatedValue(string name, InputSlot<T> inputSlot, Animator animator)
    {
        Log.Warning("Animated type didn't not implement DrawAnimatedValue");
        return InputEditStateFlags.Nothing;
    }

    public virtual string GetSlotValue(IInputSlot inputSlot)
    {
        if (inputSlot is InputSlot<T> typedInputSlot)
        {
            return GetSlotValueAsString(ref typedInputSlot.Value);
        }

        return string.Empty;
    }

    private readonly Icon[] _keyframeButtonIcons = new[]
                                                       {
                                                           Icon.KeyframeToggleOffNone,
                                                           Icon.KeyframeToggleOffLeft,
                                                           Icon.KeyframeToggleOffRight,
                                                           Icon.KeyframeToggleOffBoth,
                                                           Icon.KeyframeToggleOnNone,
                                                           Icon.KeyframeToggleOnLeft,
                                                           Icon.KeyframeToggleOnRight,
                                                           Icon.KeyframeToggleOnBoth,
                                                       };

    public InputEditStateFlags DrawParameterEdit(IInputSlot inputSlot, SymbolUi compositionUi, SymbolUi.Child symbolChildUi, bool hideNonEssentials,
                                                 bool skipIfDefault)
    {
        var editState = InputEditStateFlags.Nothing;
        if ((inputSlot.HasInputConnections || inputSlot.IsMultiInput) && hideNonEssentials)
            return editState;

        if (inputSlot.Input == null)
            return InputEditStateFlags.Nothing;

        var typeColor = TypeUiRegistry.GetPropertiesForType(Type).Color;
        var compositionSymbol = compositionUi.Symbol;
        var animator = compositionSymbol.Animator;

        Curve? animationCurve = null;
        var isAnimated = IsAnimatable && animator.TryGetFirstInputAnimationCurve(inputSlot, out animationCurve);
        MappedType = inputSlot.MappedType;

        if (inputSlot is not InputSlot<T> typedInputSlot)
        {
            Debug.Assert(false);
            return editState;
        }

        var input = inputSlot.Input;
        if (input.IsDefault && skipIfDefault)
            return InputEditStateFlags.Nothing;

        //var window = GraphWindow.Focused;
        var components = ProjectView.Focused;
        if (components == null)
            return InputEditStateFlags.Nothing;

        var nodeSelection = components.NodeSelection;
        IReadOnlyList<ConnectionMaker.TempConnection> tempConnections = ConnectionMaker.GetTempConnectionsFor(components.GraphView);

        var name = inputSlot.Input.Name;

        if (inputSlot.HasInputConnections)
        {
            editState = DrawConnectedParameter();
        }
        else if (isAnimated && animationCurve != null)
        {
            editState = DrawAnimatedParameter(animationCurve);
        }
        else
        {
            editState = DrawNormalParameter();
        }

        return editState;

        #region draw parameter types --------------------------------------------------------
        InputEditStateFlags DrawConnectedParameter()
        {
            if (inputSlot.IsMultiInput)
            {
                // Just show actual values

                InputArea.DrawConnectedMultiInputHeader(name, ParameterNameWidth);

                if (ImGui.BeginPopupContextItem("##parameterOptions", 0))
                {
                    if (ParameterWindow.IsAnyInstanceVisible() && ImGui.MenuItem("Rename input"))
                        ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                    
                    if (ImGui.MenuItem("Parameters settings"))
                        editState = InputEditStateFlags.ShowOptions;

                    ImGui.EndPopup();
                }

                var multiInput = (MultiInputSlot<T>)typedInputSlot;
                var allInputs = multiInput.GetCollectedTypedInputs();

                for (var multiInputIndex = 0; multiInputIndex < allInputs.Count; multiInputIndex++)
                {
                    ImGui.PushID(multiInputIndex);
                    if (CustomComponents.RoundedButton(string.Empty, InputArea.ConnectionAreaWidth, ImDrawFlags.RoundCornersLeft))
                    {
                        // TODO: implement with proper SelectionManager
                    }

                    Icons.DrawIconOnLastItem(Icon.ConnectedParameter, typeColor);
                    ImGui.SameLine();

                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(1.0f, 0.5f));

                    var slot = allInputs[multiInputIndex];
                    var connectedName = slot?.Parent?.Symbol.Name ?? "???";

                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.Button($"{multiInputIndex}.", new Vector2(ParameterNameWidth, 0.0f));
                    ImGui.PopStyleColor();

                    ImGui.PopStyleVar();
                    ImGui.SameLine();

                    ImGui.SetNextItemWidth(-1);
                    ImGui.PushStyleColor(ImGuiCol.Text, typeColor.Rgba);
                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));

                    var dummy = slot != null ? slot.Value : default;
                    DrawReadOnlyControl(connectedName, ref dummy);
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor();
                    ImGui.PopID();
                }

                ImGui.Spacing();
            }
            else
            {
                InputArea.DrawConnectedSingleInputArea(nodeSelection, inputSlot, compositionUi, typeColor, compositionSymbol, symbolChildUi);

                // Draw Name
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(1.0f, 0.5f));
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
                ImGui.Button($"{input.Name.AddSpacesForImGuiOutput()}##ParamName", new Vector2(ParameterNameWidth, 0.0f));
                ImGui.PopStyleColor();
                if (ImGui.BeginPopupContextItem("##parameterOptions", 0))
                {
                    if (ImGui.MenuItem("Parameters settings"))
                        editState = InputEditStateFlags.ShowOptions;

                    ImGui.EndPopup();
                }

                CustomComponents.ContextMenuForItem(() =>
                                                    {
                                                        if (ImGui.MenuItem("Set as default", !input.IsDefault))
                                                        {
                                                            // Todo: Implement Undo/Redo Command
                                                            input.SetCurrentValueAsDefault();
                                                            if (SymbolUiRegistry.TryGetSymbolUi(symbolChildUi.SymbolChild.Symbol.Id, out var symbolUi))
                                                            {
                                                                symbolUi.Symbol.InvalidateInputDefaultInInstances(inputSlot);
                                                                symbolUi.FlagAsModified();
                                                            }
                                                        }

                                                        if (ImGui.MenuItem("Reset to default", !input.IsDefault))
                                                        {
                                                            UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id,
                                                                                            input));
                                                        }

                                                        //var graphWindow = ProjectEditing.Components;
                                                        //var nodeSelection = components.NodeSelection;
                                                        //var structure = components.Structure;
                                                        if (ImGui.MenuItem("Extract as connection operator"))
                                                        {
                                                            ProjectView.Focused?.GraphView.ExtractAsConnectedOperator(typedInputSlot, symbolChildUi, input);
                                                        }

                                                        if (ImGui.MenuItem("Publish as Input", null, false, false))
                                                        {
                                                            InputArea.PublishAsInput(nodeSelection, inputSlot, symbolChildUi, input);
                                                        }

                                                        CustomComponents
                                                           .TooltipForLastItem("Publishing as input is not yet implemented. Please create a input of that type and connect manually.");

                                                        if (ParameterWindow.IsAnyInstanceVisible() && ImGui.MenuItem("Rename input"))
                                                        {
                                                            ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                                                        }
                                                        
                                                        if (ImGui.MenuItem("Parameters settings"))
                                                            editState = InputEditStateFlags.ShowOptions;
                                                    });

                ImGui.PopStyleVar();
                ImGui.SameLine();

                ImGui.PushItemWidth(200.0f);
                ImGui.SetNextItemWidth(-1);

                string connectedName;
                if (typedInputSlot.TryGetFirstConnection(out var connectedSlot))
                {
                    connectedName = connectedSlot.Parent.Symbol.Name;
                }
                else
                {
                    connectedName = "???";
                }

                ImGui.PushStyleColor(ImGuiCol.Text, typeColor.Rgba);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));

                DrawReadOnlyControl(connectedName, ref typedInputSlot.Value!);
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(1);
                ImGui.PopItemWidth();
            }

            return editState;
        }

        InputEditStateFlags DrawAnimatedParameter(Curve curve)
        {
            var hasKeyframeAtCurrentTime = curve.HasVAt(Playback.Current.TimeInBars);
            var hasKeyframeBefore = curve.HasKeyBefore(Playback.Current.TimeInBars);
            var hasKeyframeAfter = curve.HasKeyAfter(Playback.Current.TimeInBars);

            var iconIndex = 0;
            const int leftBit = 1 << 0;
            const int rightBit = 1 << 1;
            const int onBit = 1 << 2;

            if (hasKeyframeBefore) iconIndex |= leftBit;
            if (hasKeyframeAfter) iconIndex |= rightBit;
            if (hasKeyframeAtCurrentTime) iconIndex |= onBit;
            var icon = _keyframeButtonIcons[iconIndex];

            if (CustomComponents.RoundedButton("##icon", InputArea.ConnectionAreaWidth, ImDrawFlags.RoundCornersLeft))
            {
                if (animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                {
                    // TODO: this should use Undo/Redo commands
                    if (hasKeyframeAtCurrentTime)
                    {
                        AnimationOperations.RemoveKeyframeFromCurves(curves, Playback.Current.TimeInBars);
                    }
                    else
                    {
                        AnimationOperations.InsertKeyframeToCurves(curves, Playback.Current.TimeInBars);
                    }
                }
            }

            Icons.DrawIconOnLastItem(icon, Color.White);

            ImGui.SameLine();

            // Draw Name
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(1.0f, 0.5f));
            var isClicked = ImGui.Button($"{input.Name.AddSpacesForImGuiOutput()}##ParamName", new Vector2(ParameterNameWidth, 0.0f));
            CustomComponents.ContextMenuForItem
                (() =>
                 {
                     if (ImGui.MenuItem("Jump To Previous Keyframe", hasKeyframeBefore))
                     {
                         UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);
                     }

                     if (ImGui.MenuItem("Jump To Next Keyframe", hasKeyframeBefore))
                     {
                         UserActionRegistry.QueueAction(UserActions.PlaybackJumpToNextKeyframe);
                     }

                     if (hasKeyframeAtCurrentTime)
                     {
                         if (ImGui.MenuItem("Remove keyframe")
                             && animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                         {
                             AnimationOperations.RemoveKeyframeFromCurves(curves,
                                                                          Playback.Current.TimeInBars);
                         }
                     }
                     else
                     {
                         if (ImGui.MenuItem("Insert keyframe")
                             && animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                         {
                             AnimationOperations.InsertKeyframeToCurves(curves,
                                                                        Playback.Current.TimeInBars);
                         }
                     }

                     ImGui.Separator();

                     if (ImGui.MenuItem("Remove Animation"))
                     {
                         UndoRedoStack.AddAndExecute(new RemoveAnimationsCommand(animator, new[] { inputSlot }));
                     }

                     if (ParameterWindow.IsAnyInstanceVisible() && ImGui.MenuItem("Rename input"))
                     {
                         ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                     }

                     if (ImGui.MenuItem("Parameters settings"))
                         editState = InputEditStateFlags.ShowOptions;
                 });
            ImGui.PopStyleVar();

            if (ImGui.IsItemHovered())
                Icons.DrawIconAtScreenPosition(Icon.Revert, ImGui.GetItemRectMin() + new Vector2(6, 4) * T3Ui.UiScaleFactor);

            if (isClicked)
            {
                var commands = new List<ICommand>();
                commands.Add(new RemoveAnimationsCommand(animator, new[] { inputSlot }));
                commands.Add(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id, input));
                var marcoCommand = new MacroCommand("Reset animated " + input.Name, commands);
                UndoRedoStack.AddAndExecute(marcoCommand);
            }

            ImGui.SameLine();

            ImGui.PushItemWidth(200.0f);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAnimated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiColors.BackgroundFull.Rgba);

            ImGui.SetNextItemWidth(-1);

            editState |= DrawAnimatedValue(name, typedInputSlot, animator);

            ImGui.PopStyleColor(2);
            ImGui.PopItemWidth();
            return editState;
        }

        InputEditStateFlags DrawNormalParameter()
        {
            // Connection area...
            InputArea.DrawNormalInputArea(typedInputSlot, compositionUi, symbolChildUi, input,
                                          IsAnimatable, typeColor, tempConnections);

            ImGui.SameLine();

            // Draw Name Button
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(1.0f, 0.5f));

            var hasStyleCount = 0;

            if (input.IsDefault)
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                hasStyleCount = 2;
            }

            var isClicked = ImGui.Button($"{input.Name.AddSpacesForImGuiOutput()}##ParamName", new Vector2(ParameterNameWidth, 0.0f));

            if (hasStyleCount > 0)
            {
                ImGui.PopStyleColor(hasStyleCount);
            }

            if (ImGui.IsItemHovered())
            {
                var text = "";
                if (!string.IsNullOrEmpty(Description))
                {
                    text += Description;
                }

                var additionalNotes = input.IsDefault ? null : "Click to reset to default";

                if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(additionalNotes))
                {
                    CustomComponents.TooltipForLastItem(text,
                                                        additionalNotes);
                }

                if (!input.IsDefault)
                    Icons.DrawIconAtScreenPosition(Icon.Revert, ImGui.GetItemRectMin() + new Vector2(6, 4) * T3Ui.UiScaleFactor);
            }

            if (isClicked)
            {
                UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id, input));
            }

            ImGui.SameLine();
            CustomComponents.ContextMenuForItem
                (() =>
                 {
                     if (ImGui.MenuItem("Set as default", !input.IsDefault))
                     {
                         // Todo: Implement Undo/Redo Command
                         input.SetCurrentValueAsDefault();
                         if (SymbolUiRegistry.TryGetSymbolUi(symbolChildUi.SymbolChild.Symbol.Id, out var symbolUi))
                         {
                             symbolUi.Symbol.InvalidateInputDefaultInInstances(inputSlot);
                             symbolUi.FlagAsModified();
                         }
                     }

                     if (ImGui.MenuItem("Reset to default", !input.IsDefault))
                     {
                         UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id,
                                                                             input));
                     }

                     if (ImGui.MenuItem("Extract as connection operator"))
                     {
                         ProjectView.Focused?.GraphView.ExtractAsConnectedOperator(typedInputSlot, symbolChildUi, input);
                     }

                     if (ImGui.MenuItem("Publish as Input", null, false, false))
                     {
                         InputArea.PublishAsInput(nodeSelection, inputSlot, symbolChildUi, input);
                     }

                     CustomComponents
                        .TooltipForLastItem("Publishing as input is not yet implemented. Please create a input of that type and connect manually.");

                     if (ParameterWindow.IsAnyInstanceVisible() && ImGui.MenuItem("Rename input"))
                     {
                         ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                     }

                     if (ImGui.MenuItem("Parameters settings"))
                         editState = InputEditStateFlags.ShowOptions;
                 });
            ImGui.PopStyleVar();

            // Draw parameter value
            ImGui.SetNextItemWidth(-1);
            ImGui.PushStyleColor(ImGuiCol.Text, input.IsDefault ? UiColors.TextMuted.Rgba : UiColors.ForegroundFull.Rgba);
            if (input.IsDefault)
            {
                input.Value.Assign(input.DefaultValue);
            }

            editState |= DrawEditControl(name, input, ref typedInputSlot.TypedInputValue.Value!, false);
            if ((editState & InputEditStateFlags.Modified) == InputEditStateFlags.Modified ||
                (editState & InputEditStateFlags.Finished) == InputEditStateFlags.Finished)
            {
                compositionSymbol.InvalidateInputInAllChildInstances(inputSlot);
            }

            if ((editState & InputEditStateFlags.ResetToDefault) == InputEditStateFlags.ResetToDefault)
            {
                input.ResetToDefault();
                compositionSymbol.InvalidateInputInAllChildInstances(inputSlot);
            }

            input.IsDefault &= (editState & InputEditStateFlags.Modified) != InputEditStateFlags.Modified;

            ImGui.PopStyleColor();
            return editState;
        }
        #endregion
    }

    public virtual bool DrawSettings()
    {
        return false;
    }

    public virtual void Write(JsonTextWriter writer)
    {
        if (Relevancy != DefaultRelevancy)
            writer.WriteObject(nameof(Relevancy), Relevancy.ToString());

        var vec2Writer = TypeValueToJsonConverters.Entries[typeof(Vector2)];
        writer.WritePropertyName("Position");
        vec2Writer(writer, PosOnCanvas);

        if (ExcludedFromPresets)
            writer.WriteObject(nameof(ExcludedFromPresets), ExcludedFromPresets);

        if (!string.IsNullOrEmpty(GroupTitle))
            writer.WriteObject(nameof(GroupTitle), GroupTitle);

        if (!string.IsNullOrEmpty(Description))
            writer.WriteObject(nameof(Description), Description);

        if (AddPadding)
            writer.WriteObject(nameof(AddPadding), AddPadding);
    }

    public virtual void Read(JToken? inputToken)
    {
        if (inputToken == null)
            return;

        Relevancy = JsonUtils.TryGetEnumValue<Relevancy>(inputToken["Relevancy"], out var relevancy)
                        ? relevancy
                        : DefaultRelevancy;

        // Keeping for reference...
        // Relevancy = (inputToken[nameof(Relevancy)] == null)
        //                 ? DefaultRelevancy
        //                 : (Relevancy)Enum.Parse(typeof(Relevancy), inputToken["Relevancy"].ToString());

        var positionToken = inputToken["Position"];
        if (positionToken != null)
            PosOnCanvas = new Vector2((positionToken["X"] ?? 0).Value<float>(),
                                      (positionToken["Y"] ?? 0).Value<float>());

        GroupTitle = inputToken[nameof(GroupTitle)]?.Value<string>();
        Description = inputToken[nameof(Description)]?.Value<string>();

        AddPadding = inputToken[nameof(AddPadding)]?.Value<bool>() ?? false;
        ExcludedFromPresets = inputToken[nameof(ExcludedFromPresets)]?.Value<bool>() ?? false;
    }

    public Type Type { get; } = typeof(T);

    private const Relevancy DefaultRelevancy = Relevancy.Optional;
}

internal static class InputArea
{
    internal const float ConnectionAreaWidth = 25.0f;

    internal static void DrawNormalInputArea<T>(InputSlot<T> inputSlot,
                                                SymbolUi compositionUi,
                                                SymbolUi.Child symbolChildUi,
                                                Symbol.Child.Input input,
                                                bool isAnimatable, Color typeColor, IReadOnlyList<ConnectionMaker.TempConnection> tempConnections)
    {
        var buttonClicked = CustomComponents.RoundedButton(string.Empty, ConnectionAreaWidth, ImDrawFlags.RoundCornersLeft);

        var inputOperation = InputOperations.None;

        if (tempConnections.Count == 0)
        {
            if (isAnimatable && ImGui.GetIO().KeyAlt)
            {
                inputOperation = InputOperations.Animate;
            }
            else if (ImGui.GetIO().KeyCtrl && ParameterExtraction.IsInputSlotExtractable(inputSlot))
            {
                inputOperation = InputOperations.Extract;
            }
            else if (ImGui.IsItemHovered())
            {
                inputOperation = InputOperations.ConnectWithSearch;
            }
        }

        if (buttonClicked)
        {
            switch (inputOperation)
            {
                case InputOperations.Animate:
                {
                    var cmd = new MacroCommand("add animation",
                                               new List<ICommand>()
                                                   {
                                                       new ChangeInputValueCommand(compositionUi.Symbol, symbolChildUi.SymbolChild.Id, input,
                                                                                   inputSlot.Input.Value),
                                                       new AddAnimationCommand(compositionUi.Symbol.Animator, inputSlot),
                                                   });

                    UndoRedoStack.AddAndExecute(cmd);
                    break;
                }
                case InputOperations.Extract:
                    ProjectView.Focused?.GraphView.ExtractAsConnectedOperator(inputSlot, symbolChildUi, input);
                    break;

                case InputOperations.ConnectWithSearch:
                {
                    ProjectView.Focused?.GraphView.CreatePlaceHolderConnectedToInput(symbolChildUi, input.InputDefinition);
                    break;
                }
            }
        }

        if (inputOperation != InputOperations.None)
        {
            var icon = inputOperation switch
                           {
                               //InputOperations.None              => Icon.AddKeyframe,
                               InputOperations.Animate           => Icon.AddKeyframe,
                               InputOperations.ConnectWithSearch => Icon.AddOpToInput,
                               InputOperations.Extract           => Icon.ExtractInput,
                               _                                 => throw new ArgumentOutOfRangeException()
                           };

            Icons.DrawIconOnLastItem(icon, typeColor);
        }
        else
        {
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            var dl = ImGui.GetWindowDrawList();
            dl.AddCircleFilled(center, 3, typeColor.Fade(0.5f));
        }

        // Drag out connection lines
        if (ImGui.IsItemActive() && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() > UserSettings.Config.ClickThreshold)
        {
            if (tempConnections.Count == 0)
            {
                ProjectView.Focused?.GraphView.StartDraggingFromInputSlot(symbolChildUi, input.InputDefinition);
                //ConnectionMaker.StartFromInputSlot(canvas, compositionUi.Symbol, symbolChildUi, input.InputDefinition);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            if (!TypeNameRegistry.Entries.TryGetValue(input.DefaultValue.ValueType, out var typeName))
            {
                typeName = input.DefaultValue.ValueType.ToString();
            }

            ImGui.TextUnformatted($"{typeName} - Input");

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.PushFont(Fonts.FontSmall);
            FormInputs.AddVerticalSpace(4);
            ImGui.TextUnformatted("Click to add input connection");
            if (isAnimatable)
            {
                ImGui.TextUnformatted("ALT+Click to animate");
            }

            if (ParameterExtraction.IsInputSlotExtractable(inputSlot))
            {
                ImGui.TextUnformatted("CTRL+Click to extract");
            }

            ImGui.PopFont();
            ImGui.PopStyleColor();
            ImGui.EndTooltip();
        }
    }

    private enum InputOperations
    {
        None,
        Animate,
        ConnectWithSearch,
        Extract,
    }

    public static void PublishAsInput(NodeSelection selection, IInputSlot originalInputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        var composition = selection.GetSelectedComposition() ?? originalInputSlot.Parent.Parent;

        if (composition == null)
        {
            Log.Warning("Can't publish input to undefined composition");
            return;
        }

        if (!InputsAndOutputs.AddInputToSymbol(input.Name, input.IsMultiInput, input.DefaultValue.ValueType, composition.Symbol))
            return;

        // FIXME: Adding the input will trigger a recompile and thus discard the previous composition
        // This would only be available after reloading with the next frame update. I currently have
        // no idea how to create the connection line without this.

        var updatedComposition = selection.GetSelectedComposition();
        if (updatedComposition == null)
        {
            Log.Warning("Sadly, we currently can't create the connection lines and set the default values.");
            return;
        }

        var newInputDefinition = updatedComposition.Symbol.InputDefinitions.SingleOrDefault(i => i.Name == input.Name);
        if (newInputDefinition == null)
        {
            Log.Warning("Publishing wasn't possible");
            return;
        }

        var cmd = new AddConnectionCommand(updatedComposition.Symbol,
                                           new Symbol.Connection(sourceParentOrChildId: ConnectionMaker.UseSymbolContainerId,
                                                                 sourceSlotId: newInputDefinition.Id,
                                                                 targetParentOrChildId: symbolChildUi.Id,
                                                                 targetSlotId: input.Id),
                                           0);
        cmd.Do();

        newInputDefinition.DefaultValue.Assign(input.Value.Clone());
        originalInputSlot.Input.Value.Assign(input.Value.Clone());
        originalInputSlot.DirtyFlag.Invalidate();

        var newSlot = updatedComposition.Inputs.FirstOrDefault(i => i.Id == newInputDefinition.Id);
        if (newSlot != null)
        {
            newSlot.Input.Value.Assign(input.Value.Clone());
            newSlot.Input.IsDefault = false;
        }

        UndoRedoStack.Clear();
    }

    public static void DrawConnectedSingleInputArea(NodeSelection nodeSelection, IInputSlot inputSlot, SymbolUi compositionUi, Color typeColor,
                                                    Symbol compositionSymbol, SymbolUi.Child symbolChildUi)
    {
        // Connected single inputs
        if (CustomComponents.RoundedButton(String.Empty, ConnectionAreaWidth, ImDrawFlags.RoundCornersLeft))
        {
            var sourceUi = FindConnectedSymbolChildUi(inputSlot.Id, compositionUi, symbolChildUi);
            // Try to find instance
            if (sourceUi is SymbolUi.Child sourceSymbolChildUi)
            {
                var selectedInstance = nodeSelection.GetFirstSelectedInstance();
                if (selectedInstance?.Parent != null)
                {
                    var selectionTargetInstance = selectedInstance.Parent.Children[sourceUi.Id];
                    nodeSelection.SetSelection(sourceSymbolChildUi, selectionTargetInstance);
                    FitViewToSelectionHandling.FitViewToSelection();
                }
            }
        }

        Icons.DrawIconOnLastItem(Icon.ConnectedParameter, typeColor.Rgba);
        ImGui.SameLine();
    }

    private static ISelectableCanvasObject? FindConnectedSymbolChildUi(Guid inputSlotId, SymbolUi compositionUi, SymbolUi.Child targetChildUi)
    {
        var connection = compositionUi.Symbol.Connections.FirstOrDefault(c => c.IsTargetOf(targetChildUi.Id, inputSlotId));

        if (connection == null)
            return null;

        return compositionUi.GetSelectables()
                            .First(ui => ui.Id == connection.SourceParentOrChildId || ui.Id == connection.SourceSlotId);
    }

    public static bool DrawConnectedMultiInputHeader(string name, float parameterNameWidth)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.0f, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Fonts.FontBold);
        CustomComponents.RoundedButton("##paramName", ConnectionAreaWidth, ImDrawFlags.RoundCornersTopLeft);
        ImGui.SameLine();

        var wasClicked = ImGui.Button($"{name.AddSpacesForImGuiOutput()}...##paramName", new Vector2(parameterNameWidth, 0));
        ImGui.PopFont();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        return wasClicked;
    }
}