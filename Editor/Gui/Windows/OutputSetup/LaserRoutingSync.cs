#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The graph → setup half of the laser routing: a LaserCamera that appears with no output routed
/// is attached to a free laser output of the active setup on its own, so it lights up in the
/// output setup without a manual drop. Each camera is only ever auto-routed once per session —
/// after that an empty OutputRef is respected (the user un-routed it, or no output was free yet).
/// Cameras are never stacked onto one output: a second camera stays unrouted until a second
/// laser output exists, because each camera is its own laser data. Runs from the frame loop next
/// to <see cref="ContentSourceSync.UpdateFrame"/>, so it keeps the wiring in step whether or not
/// the setup window is open.
/// </summary>
internal static class LaserRoutingSync
{
    public static void UpdateFrame()
    {
        var setup = ActiveSetup.Current;
        if (setup == null)
        {
            _autoRoutedChildIds.Clear();
            return;
        }

        var cameras = LaserContent.Cameras;
        for (var i = 0; i < cameras.Count; i++)
        {
            if (cameras[i] is not Instance instance || !ContentSourceSync.IsInScope(instance, requireExposure: false))
                continue;

            var childId = instance.SymbolChildId;
            var outputRef = LaserContent.OutputRefOf(instance);
            if (outputRef != Guid.Empty)
            {
                // Routed by user, drag or a previous pass — never touch it again.
                _autoRoutedChildIds.Add(childId);
                continue;
            }

            if (!_autoRoutedChildIds.Add(childId))
                continue; // seen unrouted before: the user cleared it deliberately this session

            var output = FindFreeLaserOutput(setup, instance);
            if (output == null)
            {
                // No free laser output yet — stay unmarked so the camera attaches itself as soon
                // as one appears. Two cameras are never routed onto one output: they are separate
                // cameras and must not share (or fight over) the same laser data.
                _autoRoutedChildIds.Remove(childId);
                continue;
            }

            LaserContent.SetOutputRef(instance, output.Id);
        }
    }

    /// <summary>The laser output no other in-scope camera feeds (the first one, when several are free).
    /// Null when every laser output is already fed — an output is never shared between cameras.</summary>
    private static OutputDefinition? FindFreeLaserOutput(Setup setup, Instance camera)
    {
        foreach (var output in setup.Outputs)
        {
            if (output.Kind != OutputDefinition.Kinds.Laser)
                continue;

            var taken = false;
            var cameras = LaserContent.Cameras;
            for (var i = 0; i < cameras.Count; i++)
            {
                if (cameras[i] is not Instance other || other == camera)
                    continue;

                if (LaserContent.OutputRefOf(other) == output.Id)
                {
                    taken = true;
                    break;
                }
            }

            if (!taken)
                return output;
        }

        return null;
    }

    private static readonly HashSet<Guid> _autoRoutedChildIds = [];
}
