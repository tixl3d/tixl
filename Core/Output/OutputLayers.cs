#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace T3.Core.Output;

/// <summary>
/// What lands on an output's canvas, as layers in composite order: its patches, and the surfaces mapped onto it
/// (each carrying the regions that ride its pin). Read by the compositor and listed on the output's card.
/// </summary>
public static class OutputLayers
{
    /// <summary>One layer: a patch, or a surface with its mapping onto the output.</summary>
    public readonly record struct Layer(Guid Id, OutputDefinition.Patch? Patch, Surface? Surface, Surface.OutputMapping? Mapping)
    {
        public Vector4 Color => Patch?.LayerColor ?? Mapping?.LayerColor ?? Vector4.One;
        public LayerBlendModes BlendMode => Patch?.BlendMode ?? Mapping?.BlendMode ?? LayerBlendModes.Normal;
    }

    /// <summary>
    /// Fills <paramref name="layers"/> back to front. <see cref="OutputDefinition.LayerOrder"/> sets the order;
    /// a patch it doesn't name goes to the back and a surface to the front, which is the order every output had
    /// before layers could be ordered. Ids it names that no longer land here are skipped.
    /// </summary>
    public static void Collect(Setup setup, OutputDefinition output, List<Layer> layers)
    {
        layers.Clear();
        var order = output.LayerOrder;
        foreach (var patch in output.Patches)
        {
            if (!order.Contains(patch.Id))
                layers.Add(new Layer(patch.Id, patch, null, null));
        }

        foreach (var id in order)
        {
            if (Contains(layers, id))
                continue;

            if (FindPatch(output, id) is { } patch)
            {
                layers.Add(new Layer(id, patch, null, null));
            }
            else if (setup.FindSurface(id) is { } surface && surface.FindMapping(output.Id) is { } mapping)
            {
                layers.Add(new Layer(id, null, surface, mapping));
            }
        }

        foreach (var surface in setup.Surfaces)
        {
            if (!order.Contains(surface.Id) && surface.FindMapping(output.Id) is { } mapping)
                layers.Add(new Layer(surface.Id, null, surface, mapping));
        }
    }

    /// <summary>Moves a layer from one back-to-front index to another, writing the whole order so it stays as shown.</summary>
    public static void Move(OutputDefinition output, List<Layer> layers, int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= layers.Count || toIndex < 0 || toIndex >= layers.Count || fromIndex == toIndex)
            return;

        var moved = layers[fromIndex];
        layers.RemoveAt(fromIndex);
        layers.Insert(toIndex, moved);
        output.LayerOrder.Clear();
        foreach (var layer in layers)
            output.LayerOrder.Add(layer.Id);
    }

    private static bool Contains(List<Layer> layers, Guid id)
    {
        foreach (var layer in layers)
        {
            if (layer.Id == id)
                return true;
        }

        return false;
    }

    private static OutputDefinition.Patch? FindPatch(OutputDefinition output, Guid id)
    {
        foreach (var patch in output.Patches)
        {
            if (patch.Id == id)
                return patch;
        }

        return null;
    }
}
