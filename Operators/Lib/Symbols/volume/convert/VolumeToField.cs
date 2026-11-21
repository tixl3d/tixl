using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;

namespace Lib.volume.convert;

/// Wraps a volume in a CPU scalar field that samples it trilinearly in world space. The field plugs into everything that consumes ScalarFields: [DisplaceGeometry], [ScatterPointsInVolume] density, field SDF combinations.
/// The vector output carries the volume's vector grid (a cached velocity, say), or the gradient of its float grid when it has none, for consumers such as [RigidForce].
[Guid("30a13904-cff8-55af-bc36-16e7ea41d608")]
internal sealed class VolumeToField : Instance<VolumeToField>
{
    [Output(Guid = "bbb5490e-0f5d-5dfb-bb67-b3ac9c0109ec")]
    public readonly Slot<ScalarField> Field = new();

    [Output(Guid = "fae7c157-3915-47c0-9bb3-912ed4c17d5a")]
    public readonly Slot<VectorField> Vectors = new();

    public VolumeToField()
    {
        Field.UpdateAction = Update;
        Vectors.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var scale = Scale.GetValue(context);
        var offset = Offset.GetValue(context);
        var outsideValue = OutsideValue.GetValue(context);

        var grid = volume?.GetFloatGrid();
        var vectorGrid = volume?.GetVectorGrid();
        if (grid == null && vectorGrid == null)
        {
            Field.Value = null;
            Vectors.Value = null;
            _last = default;
            return;
        }

        // Equals instead of ==: a NaN input compares unequal with ==, which rebuilt the fields (and
        // re-ran every consumer keyed on the field instance) on every frame.
        var key = (volume, scale, offset, outsideValue);
        if (key.Equals(_last))
            return;

        try
        {
            if (grid != null)
            {
                var sampler = new VdbSampler(grid);
                var worldBBox = grid.ActiveWorldBBox();
                Field.Value = new ScalarField((in FieldSample sample) =>
                                                  {
                                                      var p = sample.Position;
                                                      if (p.X < worldBBox.Min.X || p.X > worldBBox.Max.X
                                                          || p.Y < worldBBox.Min.Y || p.Y > worldBBox.Max.Y
                                                          || p.Z < worldBBox.Min.Z || p.Z > worldBBox.Max.Z)
                                                          return outsideValue;

                                                      return sampler.SampleWorld(p) * scale + offset;
                                                  });
            }
            else
            {
                Field.Value = null;
            }

            // A stored vector grid is the data itself; without one the float grid's slope is the best vector it has.
            var vectorSampler = vectorGrid != null ? new VdbVectorSampler(vectorGrid) : new VdbVectorSampler(grid);
            var vectorBBox = vectorGrid != null ? vectorGrid.ActiveWorldBBox() : grid.ActiveWorldBBox();
            Vectors.Value = new VectorField((in FieldSample sample) =>
                                                {
                                                    var p = sample.Position;
                                                    if (p.X < vectorBBox.Min.X || p.X > vectorBBox.Max.X
                                                        || p.Y < vectorBBox.Min.Y || p.Y > vectorBBox.Max.Y
                                                        || p.Z < vectorBBox.Min.Z || p.Z > vectorBBox.Max.Z)
                                                        return Vector3.Zero;

                                                    return vectorSampler.SampleWorld(p) * scale;
                                                });
            _last = key;
            _lastError = null;
        }
        catch (Exception e)
        {
            // An exception escaping an update is fatal to the editor; degrade to an empty output.
            Field.Value = null;
            Vectors.Value = null;
            _last = key;  // a broken volume stays broken until the inputs change
            if (_lastError != e.Message)
                Log.Warning($"VolumeToField failed: {e.Message}", this);
            _lastError = e.Message;
        }
    }

    private (VdbVolume, float, float, float) _last = (null, float.NaN, float.NaN, float.NaN);
    private string _lastError;

    [Input(Guid = "b878129b-860f-5060-97e3-7ac2d09b3b9d")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "179afeb1-79ab-5473-b63e-6dabc122a107")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "5fa0d40a-3c96-5b7a-9b85-ab82725cb168")]
    public readonly InputSlot<float> Offset = new();

    [Input(Guid = "b7d33ffd-4de6-5f58-8c6b-b0b9d4a1dd5e")]
    public readonly InputSlot<float> OutsideValue = new();

}