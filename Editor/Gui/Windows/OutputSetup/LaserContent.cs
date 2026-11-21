#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Laser cameras as output-setup content: listing from the laser content registry, OutputRef
/// reading without evaluation, and undoable routing writes — the bridge between the graph-side
/// cameras and the flow outliner's CONTENT column.
/// </summary>
internal static class LaserContent
{
    public static readonly Guid LaserCameraSymbolId = new("2CCC2F86-FE71-42FF-B91D-812763F5C6CF");

    public static IReadOnlyList<ILaserContentSupplier> Cameras => LaserContentRegistry.Suppliers;

    public static bool IsCamera(Instance instance) => instance is ILaserContentSupplier;

    /// <summary>The live laser camera with this symbol-child id, or null (e.g. after deletion).</summary>
    public static Instance? FindCamera(Guid childId)
    {
        for (var i = 0; i < Cameras.Count; i++)
        {
            if (Cameras[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    /// <summary>
    /// The camera's OutputRef input value, without evaluating anything — reads the symbol-child
    /// input so it also works for cameras the graph has never run.
    /// </summary>
    public static Guid OutputRefOf(Instance camera)
    {
        if (TryGetInput(camera, out var input) && input.Value is InputValue<Guid> guidValue)
            return guidValue.Value;

        return Guid.Empty;
    }

    /// <summary>
    /// Routes a camera to a laser output (or nowhere, with an empty id) — the one routing write
    /// that lands on an op input (the camera's OutputRef) rather than on setup data. Undoable
    /// like every graph edit.
    /// </summary>
    public static void SetOutputRef(Instance camera, Guid outputId)
    {
        if (!TryGetInput(camera, out var input) || input.Value.Clone() is not InputValue<Guid> newValue)
            return;

        newValue.Value = outputId;
        var parent = camera.Parent;
        if (parent == null)
            return;

        SetupUndo.ExecuteCompanion(new ChangeInputValueCommand(parent.Symbol, camera.SymbolChildId, input, newValue, camera));
    }

    private static bool TryGetInput(Instance camera, out Symbol.Child.Input input)
    {
        input = null!;

        // The supplier declares its own routing input (each laser supplier op names its OutputRef
        // input in its implementation), so the setup doesn't hard-code any op's input id here.
        return camera is ILaserContentSupplier supplier && supplier.TryGetRoutingInput(out input);
    }
}
