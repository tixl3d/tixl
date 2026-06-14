#nullable enable
using T3.Core.IO;
using T3.Core.Operator.Interfaces;
using T3.Core.Utils;

namespace Lib.io.input;

[Guid("0a8497a1-8740-4561-b258-51475c44473b")]
internal sealed class BlendSnapshots : Instance<BlendSnapshots>, IStatusProvider
{
    [Output(Guid = "7d83da79-94a9-4ac8-bb30-c1efa5a0a205")]
    public readonly Slot<Command> Result = new();

    public BlendSnapshots()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Parent == null)
        {
            _statusLevel = IStatusProvider.StatusLevel.Warning;
            _statusMessage = "Place this operator inside a composition to blend its snapshots.";
            return;
        }

        var request = SnapShotBlendingData.GetOrCreateRequest(Parent.Symbol.Id);

        if (!Enable.GetValue(context))
        {
            request.Enabled = false;
            _statusLevel = IStatusProvider.StatusLevel.Notice;
            _statusMessage = "Disabled.";
            return;
        }

        request.Mode = (SnapShotBlendingData.IndexMode)Mode.GetValue(context).Clamp(0, _modeCount - 1);

        request.Indices.Clear();
        var indices = SnapshotIndices.GetValue(context);
        if (indices != null)
            request.Indices.AddRange(indices);

        request.WeightFactors.Clear();
        var weights = WeightFactors.GetValue(context);
        if (weights != null)
            request.WeightFactors.AddRange(weights);

        // Re-armed every frame; the editor consumes this and releases the blend if the operator stops evaluating.
        request.Enabled = true;

        // The editor resolves the request against the active snapshot pool and writes a verdict back.
        _statusLevel = request.ResolvedStatusLevel == IStatusProvider.StatusLevel.Undefined
                           ? IStatusProvider.StatusLevel.Success
                           : request.ResolvedStatusLevel;
        _statusMessage = request.ResolvedStatusMessage ?? "Blending snapshots.";
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string? GetStatusMessage() => _statusMessage;

    [Input(Guid = "e1d2c3b4-5a6f-4e10-9b20-1a2b3c4d5e60")]
    public readonly InputSlot<bool> Enable = new();

    [Input(Guid = "a3f4e5d6-7c8b-4a32-9d42-3c4d5e6f7a82")]
    public readonly InputSlot<List<int>> SnapshotIndices = new();

    [Input(Guid = "f2e3d4c5-6b7a-4f21-8c31-2b3c4d5e6f71")]
    public readonly InputSlot<List<float>> WeightFactors = new();

    [Input(Guid = "b4a5f6e7-8d9c-4b43-ae53-4d5e6f7a8b93", MappedType = typeof(SnapShotBlendingData.IndexMode))]
    public readonly InputSlot<int> Mode = new();

    private static readonly int _modeCount = Enum.GetValues<SnapShotBlendingData.IndexMode>().Length;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Undefined;
    private string? _statusMessage;
}
