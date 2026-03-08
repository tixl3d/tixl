#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Handles copy and pasting of keyframes between animation parameters.
///
/// To allow this we store additional attributes with the copied curves and when pasting try to find matches
/// with decreasing precision. 
/// </summary>
internal static class KeyframeCopyAndPasting
{
    internal record struct CurveWithDetails(
        Curve Curve,
        Guid ChildId, // We assume that symbolChildId is unique across compositions
        Guid InputId, // We assume that inputId is unique across symbol types
        int CurveIndex);

    public static void SetClipboard(List<CurveWithDetails>? curvesForCopy)
    {
        _copiedCurvesWithDetails = curvesForCopy;
    }

    public static bool  HasValidClipboard => _copiedCurvesWithDetails is { Count: > 0 };

    public static bool TryPasteTo(List<TimeLineCanvas.AnimationParameter> animParams, [NotNullWhen(true)] out HashSet<VDefinition>? newKeyframes)
    {
        newKeyframes = null;
        
        if (_copiedCurvesWithDetails == null)
        {
            Log.Debug("No keyframes copied");
            return false;
        }

        if (TimeLineCanvas.Current == null)
        {
            Log.Warning("Can't paste keyframes without active timeline");
            return false;
        }

        var foundCopiedKeys = false;
        var minTime = double.PositiveInfinity;
        var maxTime = double.NegativeInfinity;

        foreach (var copy in _copiedCurvesWithDetails)
        {
            foreach (var k in copy.Curve.GetVDefinitions())
            {
                minTime = double.Min(minTime, k.U);
                maxTime = double.Max(maxTime, k.U);
                foundCopiedKeys = true;
            }
        }

        var dt = TimeLineCanvas.Current.Playback.TimeInBars - minTime;

        if (!foundCopiedKeys)
        {
            Log.Debug("No keyframes to paste");
            return false;
        }

        if (TryPasteToSameChildren(animParams, dt, MatchType.SameSymbolChildInput, out  newKeyframes))
            return  true;
        
        if (TryPasteToSameChildren(animParams, dt, MatchType.SameSymbolInput, out  newKeyframes))
            return true;

        if (TryPasteToSameChildren(animParams, dt, MatchType.SameIndex, out  newKeyframes))
            return true;

        return false;
    }

    private static bool TryPasteToSameChildren(List<TimeLineCanvas.AnimationParameter> parm, double dt, KeyframeCopyAndPasting.MatchType matchType, out HashSet<VDefinition> newKeyframes)
    {
        newKeyframes = [];
        
        if (_copiedCurvesWithDetails == null || _copiedCurvesWithDetails.Count == 0)
            return false;

        var commands = new List<ICommand>();

        var used = new bool[_copiedCurvesWithDetails.Count];
        
        foreach (var animParam in parm)
        {
            var curveIndex = 0;
            if (!animParam.Curves.Any())
                continue;
            
            foreach (var curve in animParam.Curves)
            {
                for (var copyPartIndex = 0; copyPartIndex < _copiedCurvesWithDetails.Count; copyPartIndex++)
                {
                    var copy = _copiedCurvesWithDetails[copyPartIndex];
                    var match = Compare(copy, animParam, curveIndex);
                    if (match != matchType)
                        continue;

                    if (used[copyPartIndex])
                        continue;

                    used[copyPartIndex] = true;

                    foreach (var k in copy.Curve.Keys)
                    {
                        var newKey = k.Clone();
                        newKey.U += dt;
                        newKeyframes.Add(newKey);
                        commands.Add(new AddKeyframesCommand(curve, newKey));
                    }

                    break;
                }

                curveIndex++;
            }
        }

        if (commands.Count == 0)
            return false;
        
        UndoRedoStack.AddAndExecute(new MacroCommand("Paste keyframes", commands));
        return true;
    }

    
    
    private static MatchType Compare(KeyframeCopyAndPasting.CurveWithDetails copied, TimeLineCanvas.AnimationParameter param, int curveIndex)
    {
        var symbolChildMatches = param.Instance.SymbolChild.Id == copied.ChildId;
        var inputMatches = param.Input.Id == copied.InputId;
        var indexMatches = curveIndex == copied.CurveIndex;

        if (indexMatches && inputMatches && symbolChildMatches)
            return MatchType.SameSymbolChildInput;

        if (indexMatches && inputMatches)
            return MatchType.SameSymbolInput;

        if (indexMatches)
            return MatchType.SameIndex;

        return MatchType.None;
    }

    private enum MatchType
    {
        SameSymbolChildInput,
        SameSymbolInput,
        SameIndex,
        None,
    }

    private static List<KeyframeCopyAndPasting.CurveWithDetails>? _copiedCurvesWithDetails;
}