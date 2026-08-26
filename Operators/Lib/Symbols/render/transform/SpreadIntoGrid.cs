using T3.Core.Utils.Geometry;

namespace Lib.render.transform;

[Guid("50cc7ae4-7064-436e-862e-b8f2a709409c")]
internal sealed class SpreadIntoGrid : Instance<SpreadIntoGrid>
{
    [Output(Guid = "24fd79e3-ad3a-4c27-9b97-48226dd990e0")]
    public readonly Slot<Command> Output = new();

    public SpreadIntoGrid()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var spread = Spread.GetValue(context) * SpreadScale.GetValue(context);
        var commands = Commands.CollectedInputs;
        if (commands.Count == 1)
            spread = Vector3.Zero;

        var count = commands.Count;
        var gridSize = GridSize.GetValue(context);

        if (gridSize.X < 1) gridSize.X = 1;
        if (gridSize.Y < 1) gridSize.Y = 1;
        if (gridSize.Z < 1) gridSize.Z = 1;

        var previousWorldTobject = context.ObjectToWorld;
        var originalObjectToWorld = context.ObjectToWorld;

        for (var spreadIndex = 0; spreadIndex < commands.Count; spreadIndex++)
        {
            var t1 = commands[spreadIndex];

            var spreadXIndex = spreadIndex % (gridSize.X);
            var spreadYIndex = spreadIndex / gridSize.X;
            var spreadZIndex = spreadIndex / (gridSize.X * gridSize.Y);

            // var countX = Math.Min(gridSize.X, count);
            // var countY = count / (gridSize.X);
            // var countZ = count / (gridSize.X * gridSize.Z);

            //var f = count <= 1 ? 0:  (0.5f - ((float)spreadIndex / (count-1) - 0.5f));
            
            var fX = gridSize.X <= 1 ? 0 : (float)spreadXIndex / (gridSize.X - 1)-0.5f ;
            var fY = gridSize.Y <= 1 ? 0 : 0.5f - (float)spreadYIndex / (gridSize.Y - 1);
            var fZ = gridSize.Z <= 1 ? 0 : 0.5f - (float)spreadZIndex / (gridSize.Z - 1);

            var tSpreaded = spread * new Vector3(fX, fY, fZ);
            
            // Build and set transform matrix
            var objectToParentObject
                = GraphicsMath.CreateTransformationMatrix(scalingCenter: Vector3.Zero,
                                                          scalingRotation: Quaternion.Identity,
                                                          scaling: Vector3.One,
                                                          rotationCenter: Vector3.Zero,
                                                          rotation: Quaternion.Identity,
                                                          translation: tSpreaded);

            context.ObjectToWorld = Matrix4x4.Multiply(objectToParentObject, originalObjectToWorld);
            t1.Value?.PrepareAction?.Invoke(context);
            t1.GetValue(context);
            t1.Value?.RestoreAction?.Invoke(context);
        }

        context.ObjectToWorld = previousWorldTobject;

        Commands.DirtyFlag.Clear();
    }

    [Input(Guid = "bf7c70a5-7b3b-4245-8066-ad17b4eb72cf")]
    public readonly MultiInputSlot<Command> Commands = new();

    [Input(Guid = "8ed3e486-134a-4047-8f8c-7427f1bde6b9")]
    public readonly InputSlot<Vector3> Spread = new();

    [Input(Guid = "7C3E1BE9-0EE5-4762-9ABF-8AB908FED1B1")]
    public readonly InputSlot<float> SpreadScale = new();

    [Input(Guid = "E7C44D76-44A1-47D7-B31D-7F3A8ED759F7")]
    public readonly InputSlot<Int3> GridSize = new();
}