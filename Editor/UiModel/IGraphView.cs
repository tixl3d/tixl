#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;

namespace T3.Editor.UiModel;

/// <summary>
/// Abstraction different graph views (I.e. legacy and magnetic)
/// </summary>
internal interface IGraphView 
{
    bool Destroyed { get; set;  }
    ScalableCanvas Canvas { get; }

    void FocusViewToSelection();
    void OpenAndFocusInstance(IReadOnlyList<Guid> path);
    void OpenAndFocusAnnotation(IReadOnlyList<Guid> compositionPath, Guid annotationId);
    public CanvasScope GetTargetScope();
    void BeginDraw(bool backgroundActive, bool bgHasInteractionFocus);
    void DrawGraph(ImDrawListPtr drawList, float graphOpacity);
    
    /// <summary>
    /// Should be active during actions like dragging a connection.
    /// </summary>
    bool HasActiveInteraction { get; }

    void Close();
    void CreatePlaceHolderConnectedToInput(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition);
    void StartDraggingFromInputSlot(SymbolUi.Child symbolChildUi, Symbol.InputDefinition inputInputDefinition);
    void ExtractAsConnectedOperator<T>(InputSlot<T> inputSlot, SymbolUi.Child symbolChildUi, Symbol.Child.Input input);
}