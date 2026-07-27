#nullable enable
using System.Numerics;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Changes a surface's rectangle: its physical size together with every corner-pin quad that had to be
/// re-projected to follow it. Setup entities are plain data (not Symbol instances), so the surface is
/// resolved by GUID against the active setup and the state captured by value — undo/redo stays independent
/// of later reload state.
/// </summary>
internal sealed class ResizeSurfaceCommand : ICommand
{
    public string Name => "Resize surface";
    public bool IsUndoable => true;

    public ResizeSurfaceCommand(Guid surfaceId, State oldState, State newState)
    {
        _setupId = ActiveSetup.Current?.Id ?? Guid.Empty;
        _surfaceId = surfaceId;
        _oldState = oldState;
        _newState = newState;
    }

    public void Do() => Apply(_newState);
    public void Undo() => Apply(_oldState);

    /// <summary>Snapshot of a surface's rectangle — its size plus each mapping's quad, all by value.</summary>
    internal readonly struct State
    {
        public State(Surface surface)
        {
            Size = surface.SizeInMeters;

            // A Layout child's rectangle is size + where it sits in its parent, so both travel together.
            LocalPosition = surface.LocalPosition;

            // The pivot belongs to the rectangle: resizing counter-moves it to hold the anchor in place, so it
            // has to travel with the snapshot — both for undo and to re-base a live drag.
            HasPlacement = surface.Placement != null;
            Pivot = surface.Placement?.Pivot ?? Vector2.Zero;

            Quads = new (Guid, Vector2[])[surface.OutputMappings.Count];
            for (var i = 0; i < surface.OutputMappings.Count; i++)
            {
                var mapping = surface.OutputMappings[i];
                Quads[i] = (mapping.OutputId, (Vector2[])mapping.Quad.Clone());
            }

            // A crop re-bases the surface's own frame, which shifts the measuring lines with it. Snapshotting
            // them keeps the live re-base (restore-then-apply each frame) from compounding, and lets a crop undo.
            Annotations = new (Vector2, Vector2)[surface.Annotations.Count];
            for (var i = 0; i < surface.Annotations.Count; i++)
                Annotations[i] = (surface.Annotations[i].P1, surface.Annotations[i].P2);
        }

        public readonly Vector2 Size;
        public readonly Vector2 LocalPosition;
        public readonly Vector2 Pivot;
        public readonly bool HasPlacement;
        public readonly (Guid OutputId, Vector2[] Quad)[] Quads;
        public readonly (Vector2 P1, Vector2 P2)[] Annotations;

        public bool TryGetQuad(Guid outputId, out Vector2[] quad)
        {
            foreach (var entry in Quads)
            {
                if (entry.OutputId == outputId)
                {
                    quad = entry.Quad;
                    return true;
                }
            }

            quad = [];
            return false;
        }

        /// <summary>
        /// Puts the snapshot back onto the surface. Besides undo, a live edge drag re-bases from this every
        /// frame: cropping rewrites the surface's own coordinate frame, so editing the live rectangle
        /// incrementally would feed back on itself.
        /// </summary>
        public void Restore(Surface surface)
        {
            surface.SizeInMeters = Size;
            surface.LocalPosition = LocalPosition;

            if (!HasPlacement)
                surface.Placement = null; // it can only have appeared via the anchor counter-move
            else if (surface.Placement != null)
                surface.Placement.Pivot = Pivot;

            foreach (var (outputId, quad) in Quads)
            {
                var mapping = surface.OutputMappings.Find(m => m.OutputId == outputId);
                if (mapping != null && mapping.Quad.Length >= 4 && quad.Length >= 4)
                    Array.Copy(quad, mapping.Quad, 4);
            }

            // Measuring lines ride the frame; restore them from the same snapshot (count is stable across a crop).
            for (var i = 0; i < Annotations.Length && i < surface.Annotations.Count; i++)
            {
                surface.Annotations[i].P1 = Annotations[i].P1;
                surface.Annotations[i].P2 = Annotations[i].P2;
            }
        }
    }

    private void Apply(State state)
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var activeSetup))
            return;

        var surface = activeSetup.FindSurface(_surfaceId);
        if (surface == null)
        {
            Log.Warning($"Surface {_surfaceId} no longer exists — skipping resize.");
            return;
        }

        state.Restore(surface);
        OutputSetupHandling.SaveActive();
    }

    private readonly Guid _setupId;
    private readonly Guid _surfaceId;
    private readonly State _oldState;
    private readonly State _newState;
}
