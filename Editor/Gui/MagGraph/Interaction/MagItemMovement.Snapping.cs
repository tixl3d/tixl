#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.UiModel.InputsAndTypes;
using MagGraphView = T3.Editor.Gui.MagGraph.Ui.MagGraphView;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal sealed partial class MagItemMovement
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    private sealed class Snapping
    {
        public float BestDistance;
        public MagGraphItem? TargetItem;
        public MagGraphItem? SourceItem;
        public MagGraphItem.Directions Direction;
        public int InputLineIndex;
        public int MultiInputIndex;
        public int OutLineIndex;
        public Vector2 OutAnchorPos;
        public Vector2 InputAnchorPos;
        public bool Reverse;
        public bool IsSnapped => BestDistance < MagGraphItem.LineHeight * (IsInsertion ? 0.35 : 0.5f);
        public bool IsInsertion;
        public SpliceLink? InsertionPoint;

        public void TestItemsForSnap(MagGraphItem outputItem, MagGraphItem inputItem, bool revert, MagGraphView view)
        {
            MagGraphConnection? inConnection;

            int aOutLineIndex, bInputLineIndex;
            for (bInputLineIndex = 0; bInputLineIndex < inputItem.InputLines.Length; bInputLineIndex++)
            {
                ref var bInputLine = ref inputItem.InputLines[bInputLineIndex];
                inConnection = bInputLine.ConnectionIn;

                for (aOutLineIndex = 0; aOutLineIndex < outputItem.OutputLines.Length; aOutLineIndex++)
                {
                    ref var outputLine = ref outputItem.OutputLines[aOutLineIndex]; // Avoid copying data from array
                    if (bInputLine.Type != outputLine.Output.ValueType)
                        continue;

                    // vertical
                    if (aOutLineIndex == 0 && bInputLineIndex == 0)
                    {
                        TestAndKeepPositionsForSnapping(ref outputLine,
                                                        0,
                                                        MagGraphItem.Directions.Vertical,
                                                        new Vector2(outputItem.Area.Min.X + MagGraphItem.WidthHalf, outputItem.Area.Max.Y),
                                                        new Vector2(inputItem.Area.Min.X + MagGraphItem.WidthHalf, inputItem.Area.Min.Y));
                    }

                    // horizontal
                    if (outputLine.Output.ValueType == bInputLine.Input.ValueType)
                    {
                        TestAndKeepPositionsForSnapping(ref outputLine,
                                                        bInputLine.MultiInputIndex,
                                                        MagGraphItem.Directions.Horizontal,
                                                        new Vector2(outputItem.Area.Max.X,
                                                                    outputItem.Area.Min.Y + (0.5f + outputLine.VisibleIndex) * MagGraphItem.LineHeight),
                                                        new Vector2(inputItem.Area.Min.X,
                                                                    inputItem.Area.Min.Y + (0.5f + bInputLine.VisibleIndex) * MagGraphItem.LineHeight));
                    }
                }
            }

            //Direction = MagGraphItem.Directions.Horizontal;
            return;

            void TestAndKeepPositionsForSnapping(ref MagGraphItem.OutputLine outputLine,
                                                 int multiInputIndexIfValid,
                                                 MagGraphItem.Directions directionIfValid,
                                                 Vector2 outPos,
                                                 Vector2 inPos)
            {
                // If input is connected the only valid output is the one with the connection line
                if (inConnection != null && outputLine.ConnectionsOut.All(c => c != inConnection))
                    return;

                var d = Vector2.Distance(outPos, inPos);
                if (d >= BestDistance)
                    return;

                ShowDebugLine(outPos, inPos, outputItem.PrimaryType);

                BestDistance = d;
                OutAnchorPos = outPos;
                InputAnchorPos = inPos;
                OutLineIndex = aOutLineIndex;
                InputLineIndex = bInputLineIndex;
                TargetItem = outputItem;
                SourceItem = inputItem;
                Reverse = revert;
                Direction = directionIfValid;
                IsInsertion = false;
                MultiInputIndex = multiInputIndexIfValid;
            }

            void ShowDebugLine(Vector2 outPos, Vector2 inPos, Type connectionType)
            {
                if (!view.ShowDebug)
                    return;

                var drawList = ImGui.GetForegroundDrawList();
                var uiPrimaryColor = TypeUiRegistry.GetPropertiesForType(connectionType).Color;
                drawList.AddLine(view.TransformPosition(outPos),
                                 view.TransformPosition(inPos),
                                 uiPrimaryColor.Fade(0.4f));

                drawList.AddCircleFilled(view.TransformPosition(inPos), 6, uiPrimaryColor.Fade(0.4f));
            }
        }

        public void TestItemsForInsertion(MagGraphItem inputItem, MagGraphItem insertionAnchorItem, SpliceLink insertionPoint, MagGraphView view)
        {
            if (inputItem.InputLines.Length < 1)
                return;

            var mainInputLine = inputItem.InputLines[0];
            var mainInputConnectionIn = mainInputLine.ConnectionIn;

            // Main input
            if (mainInputConnectionIn != null && mainInputLine.Type == insertionPoint.Type)
            {
                // Vertical
                if (mainInputConnectionIn.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedVertical &&
                    insertionPoint.Direction == MagGraphItem.Directions.Vertical)
                {
                    var inputPos = inputItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X / 2, 0);
                    var insertionAnchorPos = insertionAnchorItem.PosOnCanvas + new Vector2(MagGraphItem.GridSize.X / 2, 0);
                    var d = Vector2.Distance(insertionAnchorPos, inputPos);

                    if (d >= BestDistance)
                        return;

                    BestDistance = d;
                    OutAnchorPos = inputPos;
                    InputAnchorPos = insertionAnchorPos;
                    OutLineIndex = 0;
                    InputLineIndex = 0;
                    TargetItem = inputItem;
                    SourceItem = mainInputConnectionIn.SourceItem;
                    Reverse = false;
                    Direction = MagGraphItem.Directions.Vertical;
                    MultiInputIndex = 0;
                    IsInsertion = true;
                    InsertionPoint = insertionPoint;
                }
                // Horizontal
                else if (mainInputConnectionIn.Style == MagGraphConnection.ConnectionStyles.MainOutToMainInSnappedHorizontal
                         && insertionPoint.Direction == MagGraphItem.Directions.Horizontal)
                {
                    var inputAnchorOffset = new Vector2(0, MagGraphItem.GridSize.Y / 2);
                    var inputPos = inputItem.PosOnCanvas + inputAnchorOffset;
                    var insertionPos = insertionAnchorItem.PosOnCanvas
                                       + new Vector2(insertionPoint.DragPositionWithinBlock.X, MagGraphItem.GridSize.Y / 2);
                    
                    ShowDebugLine(insertionPos, inputPos, Color.Red);
                    
                    var d = Vector2.Distance(insertionPos, inputPos);
                    if (d >= BestDistance)
                        return;

                    BestDistance = d;
                    OutAnchorPos = inputPos;
                    InputAnchorPos = insertionPos;
                    OutLineIndex = 0;
                    InputLineIndex = 0;
                    TargetItem = inputItem;
                    SourceItem = mainInputConnectionIn.SourceItem;
                    Reverse = false;
                    Direction = MagGraphItem.Directions.Horizontal;
                    MultiInputIndex = 0;
                    IsInsertion = true;
                    InsertionPoint = insertionPoint;
                }
            }

            // Additional input lines
            for (int i = 1; i < inputItem.InputLines.Length; i++)
            {
                var inputLine = inputItem.InputLines[i];
                var connectionIn = inputLine.ConnectionIn;

                if (connectionIn == null 
                    || inputLine.Type != insertionPoint.Type
                    || connectionIn.Style != MagGraphConnection.ConnectionStyles.MainOutToInputSnappedHorizontal
                    || insertionPoint.Direction != MagGraphItem.Directions.Horizontal) 
                    continue;
                
                //var inputAnchorOffset = new Vector2(0, MagGraphItem.GridSize.Y / 2);
                
                var inputPos = inputItem.PosOnCanvas + new Vector2(0, MagGraphItem.GridSize.Y * (i + 0.5f));
                
                
                var insertionPos = insertionAnchorItem.PosOnCanvas
                                   + new Vector2(insertionPoint.DragPositionWithinBlock.X,  MagGraphItem.GridSize.Y * ( 0+ 0.5f));
                
                ShowDebugLine(insertionPos, inputPos, Color.Cyan);
                
                var d = Vector2.Distance(insertionPos, inputPos) - 1;

                if (d >= BestDistance)
                    return;

                BestDistance = d;
                OutAnchorPos = inputPos;
                InputAnchorPos = insertionPos;
                OutLineIndex = 0;
                InputLineIndex = i;
                TargetItem = inputItem;
                SourceItem = connectionIn.SourceItem;
                Reverse = false;
                Direction = MagGraphItem.Directions.Horizontal;
                MultiInputIndex = connectionIn.MultiInputIndex;
                IsInsertion = true;
                InsertionPoint = insertionPoint;
            }
            void ShowDebugLine(Vector2 outPos, Vector2 inPos, Color color)
            {
                if (!view.ShowDebug)
                    return;

                var drawList = ImGui.GetForegroundDrawList();
                
                drawList.AddLine(view.TransformPosition(outPos),
                                 view.TransformPosition(inPos),
                                 color.Fade(0.4f));

                drawList.AddCircleFilled(view.TransformPosition(inPos), 6, color.Fade(0.4f));
            }
        }

        public void Reset()
        {
            BestDistance = float.PositiveInfinity;
        }
    }
}