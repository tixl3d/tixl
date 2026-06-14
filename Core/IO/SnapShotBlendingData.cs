#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator.Interfaces;

namespace T3.Core.IO;

/// <summary>
/// Lets a [BlendSnapshots] operator drive the editor's snapshot cross-fade procedurally: the operator
/// writes a <see cref="BlendRequest"/> for the composition it lives in, and the editor applies it each
/// frame and writes a resolved status back for the operator to surface via IStatusProvider.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="ITapProvider"/> forwarding pattern. This is editor-only by design — the
/// snapshot/variation system lives in the editor, so an exported player has no consumer for these
/// requests and the operator has no effect there.
/// </remarks>
public static class SnapShotBlendingData
{
    public enum IndexMode
    {
        /// <summary>Address snapshots by their controller/launchpad activation index — stable across reordering.</summary>
        ControllerIndices,

        /// <summary>Address snapshots by their ordinal position in reading order — shifts when snapshots are reordered.</summary>
        SnapshotIndices,
    }

    /// <summary>
    /// One reused request per composition symbol, keyed by the composition the operator lives in.
    /// Reuse avoids per-frame allocations.
    /// </summary>
    public static readonly Dictionary<Guid, BlendRequest> RequestsByComposition = new();

    /// <summary>
    /// One-shot snapshot activations forwarded by [ActivateSnapshot] operators: composition symbol id →
    /// the raw index to activate (the editor wraps it modulo the snapshot count). The editor consumes
    /// and clears these each frame, so an entry means "activate now".
    /// </summary>
    public static readonly Dictionary<Guid, int> PendingActivationsByComposition = new();

    public static BlendRequest GetOrCreateRequest(Guid compositionSymbolId)
    {
        if (RequestsByComposition.TryGetValue(compositionSymbolId, out var request))
            return request;

        request = new BlendRequest();
        RequestsByComposition[compositionSymbolId] = request;
        return request;
    }

    /// <summary>
    /// The operator fills the request fields and re-arms <see cref="Enabled"/> on every evaluation;
    /// the editor consumes <see cref="Enabled"/> after applying the request, so a deleted or
    /// no-longer-evaluated operator releases its blend after one frame.
    /// </summary>
    public sealed class BlendRequest
    {
        public bool Enabled;
        public IndexMode Mode;
        public readonly List<int> Indices = new();
        public readonly List<float> WeightFactors = new();

        // Written by the editor's resolution step, read by the operator for IStatusProvider.
        public IStatusProvider.StatusLevel ResolvedStatusLevel = IStatusProvider.StatusLevel.Undefined;
        public string? ResolvedStatusMessage;
    }
}
