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
using T3.Core.Utils;
using T3.Editor.Gui;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Animation;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Skills.Training;
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
    /// <param name="input">Null when <paramref name="readOnly"/> is set: a connected parameter is
    /// driven by its source and has no <see cref="Symbol.Child.Input"/> to read a default flag from.
    /// Implementations must not dereference it in the read-only branch.</param>
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

                // Opens on left-click (unlike the other parameter menus) because the multi-input header
                // has no other click action of its own.
                CustomComponents.ContextMenuForItem(() =>
                                                    {
                                                        CustomComponents.DrawMenuGroupLabel("Symbol");

                                                        if (CustomComponents.DrawMenuItem(_renameItemId, Icon.None, "Rename...",
                                                                                          isEnabled: ParameterWindow.IsAnyInstanceVisible()))
                                                        {
                                                            ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol,
                                                                                                           input.InputDefinition.Id);
                                                        }

                                                        if (CustomComponents.DrawMenuItem(_inputSettingsItemId, Icon.Settings2, "Input Settings"))
                                                            editState = InputEditStateFlags.ShowOptions;
                                                    },
                                                    id: "##parameterOptions",
                                                    flags: ImGuiPopupFlags.MouseButtonLeft);

                var multiInput = (MultiInputSlot<T>)typedInputSlot;
                var allInputs = multiInput.GetCollectedTypedInputs();

                for (var multiInputIndex = 0; multiInputIndex < allInputs.Count; multiInputIndex++)
                {
                    ImGui.PushID(multiInputIndex);
                    if (CustomComponents.RoundedButton(string.Empty, InputArea.ConnectionAreaWidth, ImDrawFlags.RoundCornersLeft))
                    {
                        // TODO: implement with proper SelectionManager
                    }

                    Icons.DrawIconOnLastItem(Icon.ConnectedInput, typeColor);
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
                /*if (ImGui.BeginPopupContextItem("##parameterOptions", 0))
                {
                    if (ImGui.MenuItem("Parameters settings"))
                        editState = InputEditStateFlags.ShowOptions;

                    ImGui.EndPopup();
                }*/

                CustomComponents.ContextMenuForItem(() =>
                                                    {
                                                        CustomComponents.DrawMenuGroupLabel("Parameter");

                                                        if (CustomComponents.DrawMenuItem(_extractItemId, Icon.ExtractInput, "Extract"))
                                                        {
                                                            ProjectView.Focused?.GraphView.ExtractAsConnectedOperator(typedInputSlot, symbolChildUi, input);
                                                        }

                                                        if (CustomComponents.DrawMenuItem(_publishAsInputItemId, Icon.None, "Publish as Input",
                                                                                          isEnabled: false))
                                                        {
                                                            InputArea.PublishAsInput(nodeSelection, inputSlot, symbolChildUi, input);
                                                        }

                                                        CustomComponents
                                                           .TooltipForLastItem("Publishing as input is not yet implemented. Please create a input of that type and connect manually.");

                                                        InputArea.DrawSnapshotControlMenuItem(compositionUi, symbolChildUi, input);

                                                        CustomComponents.SeparatorLine();
                                                        CustomComponents.DrawMenuGroupLabel("Symbol");

                                                        if (CustomComponents.DrawMenuItem(_setAsDefaultItemId, Icon.Pin, "Set as Default",
                                                                                          isEnabled: !input.IsDefault))
                                                        {
                                                            UndoRedoStack.AddAndExecute(new SetInputDefaultCommand(compositionSymbol, symbolChildUi.Id, input));
                                                        }

                                                        if (CustomComponents.DrawMenuItem(_resetItemId, Icon.Reset, "Reset to Default",
                                                                                          isEnabled: !input.IsDefault))
                                                        {
                                                            UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id,
                                                                                            input));
                                                        }

                                                        if (CustomComponents.DrawMenuItem(_renameItemId, Icon.None, "Rename...",
                                                                                          isEnabled: ParameterWindow.IsAnyInstanceVisible()))
                                                        {
                                                            ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                                                        }

                                                        if (CustomComponents.DrawMenuItem(_inputSettingsItemId, Icon.Settings2, "Input Settings"))
                                                            editState = InputEditStateFlags.ShowOptions;
                                                    });

                ImGui.PopStyleVar();
                ImGui.SameLine();

                ImGui.PushItemWidth(200.0f);
                ImGui.SetNextItemWidth(-1 - InputArea.ValueEditRightMargin);

                var connectedName = "???";
                if (typedInputSlot.TryGetFirstConnection(out var connectedSlot) && connectedSlot?.Parent != null)
                {
                    connectedName = !string.IsNullOrWhiteSpace(connectedSlot.Parent.SymbolChild.Name)
                        ? connectedSlot.Parent.SymbolChild.Name
                        : (!string.IsNullOrWhiteSpace(connectedSlot.Parent.Symbol.Name)
                            ? connectedSlot.Parent.Symbol.Name
                            : "???");
                }
                if (ImGui.IsItemHovered())
                {
                    var name = connectedName;
                    CustomComponents.TooltipForLastItem(() =>
                                                        {
                                                            ImGui.TextUnformatted("Input: " + name);
                                                            if (string.IsNullOrEmpty(Description))
                                                                return;

                                                            ImGui.Separator();
                                                            MarkdownTooltip.Draw(Description);
                                                        });
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
            // Curves are sampled in the op's local time, so the indicator and toggle must query/insert there.
            var playbackTime = Playback.Current.TimeInBars;
            var animationTime = Animator.GetLocalAnimationTime(inputSlot.Parent, playbackTime);

            // Exact-equality lookup fails once a time clip remaps: the clip's float ranges make map(playhead)
            // land fractionally off the key's quantized U. Treat a key within ~1/100 bar of playback time as
            // "at" the playhead (tolerance transformed into local space, so it follows the clip's rate).
            var tolerance = Animator.GetLocalTimeTolerance(inputSlot.Parent, playbackTime);
            var keyTimeAtPlayhead = animationTime;
            var hasKeyframeAtCurrentTime = false;
            if (curve.TryGetPreviousKey(animationTime + tolerance, out var nearKey) && nearKey.U >= animationTime - tolerance)
            {
                hasKeyframeAtCurrentTime = true;
                keyTimeAtPlayhead = nearKey.U;
            }

            var hasKeyframeBefore = curve.HasKeyBefore(animationTime - tolerance);
            var hasKeyframeAfter = curve.HasKeyAfter(animationTime + tolerance);

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
                    if (hasKeyframeAtCurrentTime)
                    {
                        // Remove the key actually found near the playhead, not the (fractionally off) mapped time.
                        AnimationOperations.RemoveKeyframeFromCurves(curves, keyTimeAtPlayhead);
                    }
                    else
                    {
                        AnimationOperations.InsertKeyframeToCurves(curves, animationTime);
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
                     CustomComponents.DrawMenuGroupLabel("Animation");

                     if (CustomComponents.DrawMenuItem(_jumpToPreviousKeyframeItemId, Icon.None, "Jump to Previous Keyframe",
                                                       isEnabled: hasKeyframeBefore))
                     {
                         UserActionRegistry.QueueAction(UserActions.PlaybackJumpToPreviousKeyframe);
                     }

                     if (CustomComponents.DrawMenuItem(_jumpToNextKeyframeItemId, Icon.None, "Jump to Next Keyframe",
                                                       isEnabled: hasKeyframeAfter))
                     {
                         UserActionRegistry.QueueAction(UserActions.PlaybackJumpToNextKeyframe);
                     }

                     if (hasKeyframeAtCurrentTime)
                     {
                         if (CustomComponents.DrawMenuItem(_removeKeyframeItemId, Icon.AddKeyframe, "Remove Keyframe")
                             && animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                         {
                             AnimationOperations.RemoveKeyframeFromCurves(curves,
                                                                          Playback.Current.TimeInBars);
                         }
                     }
                     else
                     {
                         if (CustomComponents.DrawMenuItem(_insertKeyframeItemId, Icon.AddKeyframe, "Insert Keyframe")
                             && animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                         {
                             AnimationOperations.InsertKeyframeToCurves(curves,
                                                                        Playback.Current.TimeInBars);
                         }
                     }

                     if (CustomComponents.DrawMenuItem(_removeAnimationItemId, Icon.Reset, "Remove Animation"))
                     {
                         UndoRedoStack.AddAndExecute(new RemoveAnimationsCommand(animator, new[] { inputSlot }));
                     }

                     CustomComponents.SeparatorLine();
                     CustomComponents.DrawMenuGroupLabel("Symbol");

                     if (CustomComponents.DrawMenuItem(_renameItemId, Icon.None, "Rename...",
                                                       isEnabled: ParameterWindow.IsAnyInstanceVisible()))
                     {
                         ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                     }

                     if (CustomComponents.DrawMenuItem(_inputSettingsItemId, Icon.Settings2, "Input Settings"))
                         editState = InputEditStateFlags.ShowOptions;
                 });
            ImGui.PopStyleVar();

            DrawInputTooltipAndResetIcon(input);

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

            ImGui.SetNextItemWidth(-1 - InputArea.ValueEditRightMargin);

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
            var showDimmed = InputArea.DimHighlightOverride ?? input.IsDefault;

            if (showDimmed)
            {
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                hasStyleCount = 2;
            }
            else if (InputArea.DimHighlightOverride == false)
            {
                // Extra contrast for parameters flagged as modified by the snapshot control view
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
                hasStyleCount = 1;
            }

            var isClicked = ImGui.Button($"{input.Name.AddSpacesForImGuiOutput()}##ParamName", new Vector2(ParameterNameWidth, 0.0f));
            var nameButtonHovered = ImGui.IsItemHovered();

            if (hasStyleCount > 0)
            {
                ImGui.PopStyleColor(hasStyleCount);
            }

            // SkillQuest feedback: only the focused "what's next" tip and all blockers are
            // visible (after a shared fade-in delay). Tooltip and icon use the same gate so a
            // non-focused Required parameter stays silent everywhere.
            var hasSkillHint = SkillQuestParameterHint.TryGetVisible(symbolChildUi.Id, input.InputDefinition.Id,
                                                                     out var skillHint, out var skillAlpha);

            DrawInputTooltipAndResetIcon(input, hasSkillHint ? skillHint : null);

            var revertIconWouldShow = nameButtonHovered && !input.IsDefault;
            if (hasSkillHint && !revertIconWouldShow)
                SkillQuestParameterHint.DrawIcon(symbolChildUi.Id, input.InputDefinition.Id, skillHint, skillAlpha);

            if (isClicked)
            {
                // In the snapshot control view the row highlight compares against the active
                // snapshot, so the name-click reverts to that instead of the default
                if (!SnapshotControlView.TryResetParameterToSnapshot(compositionSymbol, symbolChildUi, input))
                {
                    UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id, input));
                }
            }

            ImGui.SameLine();
            CustomComponents.ContextMenuForItem
                (() =>
                 {
                     CustomComponents.DrawMenuGroupLabel("Parameter");

                     if (CustomComponents.DrawMenuItem(_animateItemId, Icon.AddKeyframe, "Animate",
                                                       isEnabled: IsAnimatable))
                     {
                         var animateCommand = new MacroCommand("add animation",
                                                               new List<ICommand>
                                                                   {
                                                                       new ChangeInputValueCommand(compositionSymbol, symbolChildUi.Id, input,
                                                                                                   inputSlot.Input.Value, inputSlot.Parent),
                                                                       new AddAnimationCommand(animator, inputSlot),
                                                                   });
                         UndoRedoStack.AddAndExecute(animateCommand);
                     }

                     if (CustomComponents.DrawMenuItem(_createConnectedItemId, Icon.AddOpToInput, "Create Connected"))
                     {
                         ProjectView.Focused?.GraphView.CreatePlaceHolderConnectedToInput(symbolChildUi, input.InputDefinition);
                     }

                     if (CustomComponents.DrawMenuItem(_extractItemId, Icon.ExtractInput, "Extract",
                                                       isEnabled: ParameterExtraction.IsInputSlotExtractable(typedInputSlot)))
                     {
                         ProjectView.Focused?.GraphView.ExtractAsConnectedOperator(typedInputSlot, symbolChildUi, input);
                     }

                     if (CustomComponents.DrawMenuItem(_resetItemId, Icon.Reset, "Reset to Default",
                                                       isEnabled: !input.IsDefault))
                     {
                         UndoRedoStack.AddAndExecute(new ResetInputToDefault(compositionSymbol, symbolChildUi.Id,
                                                                             input));
                     }

                     if (InputArea.IsSnapshotControllable(symbolChildUi, input))
                     {
                         CustomComponents.DrawMenuGroupLabel("Snapshot Control");
                         InputArea.DrawSnapshotControlMenuItem(compositionUi, symbolChildUi, input);
                         SnapshotControlView.DrawSnapshotActionMenuItems(compositionSymbol, symbolChildUi, input, reserveCheckmarkColumn: true);
                     }

                     CustomComponents.SeparatorLine();
                     CustomComponents.DrawMenuGroupLabel("Symbol");

                     if (CustomComponents.DrawMenuItem(_setAsDefaultItemId, Icon.Pin, "Set as Default",
                                                       isEnabled: !input.IsDefault))
                     {
                         UndoRedoStack.AddAndExecute(new SetInputDefaultCommand(compositionSymbol, symbolChildUi.Id, input));
                     }

                     if (CustomComponents.DrawMenuItem(_renameItemId, Icon.None, "Rename...",
                                                       isEnabled: ParameterWindow.IsAnyInstanceVisible()))
                     {
                         ParameterWindow.RenameInputDialog.ShowNextFrame(symbolChildUi.SymbolChild.Symbol, input.InputDefinition.Id);
                     }

                     if (CustomComponents.DrawMenuItem(_inputSettingsItemId, Icon.Settings2, "Input Settings"))
                         editState = InputEditStateFlags.ShowOptions;
                 });
            ImGui.PopStyleVar();

            // Draw parameter value
            ImGui.SetNextItemWidth(-1 - InputArea.ValueEditRightMargin);
            ImGui.PushStyleColor(ImGuiCol.Text, showDimmed ? UiColors.TextMuted.Rgba : UiColors.ForegroundFull.Rgba);
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

    private static readonly int _animateItemId = "animateParam".GetHashCode();
    private static readonly int _createConnectedItemId = "createConnectedParam".GetHashCode();
    private static readonly int _extractItemId = "extractParam".GetHashCode();
    private static readonly int _resetItemId = "resetParam".GetHashCode();
    private static readonly int _setAsDefaultItemId = "setParamAsDefault".GetHashCode();
    private static readonly int _renameItemId = "renameParam".GetHashCode();
    private static readonly int _inputSettingsItemId = "paramInputSettings".GetHashCode();
    private static readonly int _publishAsInputItemId = "publishAsInput".GetHashCode();
    private static readonly int _jumpToPreviousKeyframeItemId = "jumpToPreviousKeyframe".GetHashCode();
    private static readonly int _jumpToNextKeyframeItemId = "jumpToNextKeyframe".GetHashCode();
    private static readonly int _removeKeyframeItemId = "removeKeyframe".GetHashCode();
    private static readonly int _insertKeyframeItemId = "insertKeyframe".GetHashCode();
    private static readonly int _removeAnimationItemId = "removeAnimation".GetHashCode();

    private void DrawInputTooltipAndResetIcon(Symbol.Child.Input input,
                                              SkillQuestParameterHint.Hint? skillQuestHint = null)
    {
        if (!ImGui.IsItemHovered())
            return;

        var text = Description ?? string.Empty;

        // In the snapshot control view the name-click reverts to the active snapshot's value,
        // matching the row highlight (which compares against the snapshot, not the default)
        var resetsToSnapshot = SnapshotControlView.IsResetToSnapshotActive;
        var canRevert = resetsToSnapshot
                            ? !(InputArea.DimHighlightOverride ?? input.IsDefault)
                            : !input.IsDefault;
        var additionalNotes = canRevert
                                  ? resetsToSnapshot ? "Click to reset to snapshot" : "Click to reset to default"
                                  : null;

        if (skillQuestHint.HasValue || !string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(additionalNotes))
        {
            var hint = skillQuestHint;
            CustomComponents.TooltipForLastItem(() =>
                                                {
                                                    if (hint.HasValue)
                                                        SkillQuestParameterHint.DrawTooltipPrefix(hint.Value);

                                                    if (!string.IsNullOrEmpty(text))
                                                        MarkdownTooltip.Draw(text);

                                                    if (!string.IsNullOrEmpty(additionalNotes))
                                                        ImGui.TextColored(UiColors.Text.Fade(0.7f), additionalNotes);
                                                });
        }

        if (canRevert)
        {
            Icons.DrawIconAtScreenPosition(
                Icon.Revert,
                ImGui.GetItemRectMin() + new Vector2(6, 4) * T3Ui.UiScaleFactor
            );
        }
    }
}

internal static class InputArea
{
    internal static float ConnectionAreaWidth => 28.0f * T3Ui.UiScaleFactor;

    /// <summary>
    /// Lets a view redefine what the dimmed/highlighted row styling means: by default rows
    /// dim when the value is at default; the snapshot control view dims rows that match the
    /// selected snapshot instead. Set around DrawParameterEdit and reset to null afterwards.
    /// </summary>
    internal static bool? DimHighlightOverride;

    /// <summary>
    /// Space in pixels kept free right of the value edit, e.g. for the snapshot control
    /// view's per-row revert button. Set around DrawParameterEdit and reset to 0 afterwards.
    /// </summary>
    internal static float ValueEditRightMargin;

    /// <summary>
    /// Context-menu toggle for per-parameter snapshot control. Hidden for parameters the
    /// snapshot system can't capture (non-blendable, excluded from presets) and for
    /// ParameterCollection children.
    /// </summary>
    internal static void DrawSnapshotControlMenuItem(SymbolUi compositionUi, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        if (!IsSnapshotControllable(symbolChildUi, input))
            return;

        var isEnabled = symbolChildUi.IsInputEnabledForSnapshots(input.InputDefinition.Id);
        if (CustomComponents.DrawMenuItem(_controlWithSnapshotsItemId, Icon.Knob, "Control with Snapshots", null, isChecked: isEnabled))
        {
            VariationHandling.ToggleParameterSnapshotControl(compositionUi, symbolChildUi, input, !isEnabled);
        }
    }

    /// <summary>
    /// True if the parameter can be snapshot-controlled: a blendable, non-excluded input on a
    /// child that isn't a ParameterCollection.
    /// </summary>
    internal static bool IsSnapshotControllable(SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        if (symbolChildUi.SnapshotGroupIndex > 1)
            return false;

        if (!ValueUtils.BlendMethods.ContainsKey(input.DefaultValue.ValueType))
            return false;

        var symbolUi = symbolChildUi.SymbolChild.Symbol.GetSymbolUi();
        return !(symbolUi.InputUis.TryGetValue(input.InputDefinition.Id, out var inputUi) && inputUi.ExcludedFromPresets);
    }

    private static readonly int _controlWithSnapshotsItemId = "controlWithSnapshots".GetHashCode();

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
            var io = ImGui.GetIO();
            if (io.KeyCtrl && io.KeyAlt && IsSnapshotControllable(symbolChildUi, input))
            {
                inputOperation = InputOperations.ToggleSnapshotControl;
            }
            else if (isAnimatable && io.KeyAlt)
            {
                inputOperation = InputOperations.Animate;
            }
            else if (io.KeyCtrl && ParameterExtraction.IsInputSlotExtractable(inputSlot))
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
                                                                                   inputSlot.Input.Value, inputSlot.Parent),
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

                case InputOperations.ToggleSnapshotControl:
                {
                    var enabled = symbolChildUi.IsInputEnabledForSnapshots(input.InputDefinition.Id);
                    VariationHandling.ToggleParameterSnapshotControl(compositionUi, symbolChildUi, input, !enabled);
                    break;
                }
            }
        }

        if (inputOperation == InputOperations.ToggleSnapshotControl)
        {
            // Green knob preview for the snapshot-control action (matches the controlled indicator).
            Icons.DrawIconOnLastItem(Icon.Knob, UiColors.StatusControlled);
        }
        else if (inputOperation != InputOperations.None)
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
        else if (symbolChildUi.IsInputEnabledForSnapshots(input.InputDefinition.Id))
        {
            Icons.DrawIconOnLastItem(Icon.Knob, UiColors.StatusControlled);
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

            if (IsSnapshotControllable(symbolChildUi, input))
            {
                ImGui.TextUnformatted("CTRL+ALT+Click to control with snapshots");
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
        ToggleSnapshotControl,
    }


    public static void PublishAsInput(NodeSelection selection, IInputSlot originalInputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input)
    {
        var composition = selection.GetSelectedComposition() ?? originalInputSlot.Parent.Parent;

        if (composition == null)
        {
            Log.Warning("Can't publish input to undefined composition");
            return;
        }

        if (!InputsAndOutputs.AddInputToSymbol(Guid.NewGuid(), input.Name, input.IsMultiInput, input.DefaultValue.ValueType, composition.Symbol))
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

        if (symbolChildUi.IsInputEnabledForSnapshots(inputSlot.Id))
        {
            Icons.DrawIconOnLastItem(Icon.Knob, UiColors.StatusControlled.Rgba);
        }
        else
        {
            Icons.DrawIconOnLastItem(Icon.ConnectedInput, typeColor.Rgba);
        }

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