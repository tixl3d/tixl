using T3.Core.Utils;

namespace Lib.render.@_;

[Guid("1cfe41c7-972e-4243-9ae4-a510ac038191")]
internal sealed class _LenseFlareHoopPosition : Instance<_LenseFlareHoopPosition>
{
    // [Output(Guid = "bc8076b8-0079-487f-b0ef-d2bc9baacdb3")]
    // public readonly Slot<StructuredList> OutBuffer = new Slot<StructuredList>();

    [Output(Guid = "1FC7D2A4-AB6C-433E-BE07-943560103C18", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> LightPosition = new();

    [Output(Guid = "2DF9C5B9-22CB-413B-A753-03FD0137488A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> TargetPosition = new();

    [Output(Guid = "87694759-9CF4-4E7F-85E0-AF7C42D46750", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector4> LightColor = new();

    public _LenseFlareHoopPosition()
    {
        LightPosition.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var worldToClipSpace = Matrix4x4.Multiply(context.WorldToCamera, context.CameraToClipSpace);
        var color = Color.GetValue(context);
        var randomizeColor = RandomizeColor.GetValue(context);
        var size = Size.GetValue(context);
            
        var randomizeSize = RandomizeSize.GetValue(context);
        var distance = Distance.GetValue(context);
        var positionFactor = PositionFactor.GetValue(context);

        var referencedLightIndex = LightIndex.GetValue(context);

        var innerFxZone = InnerFxZone.GetValue(context);
        var edgeFxZone = EdgeFxZone.GetValue(context);
        var zoneFxScale = FxZoneScale.GetValue(context);
        var zoneFxBrightness = FxZoneBrightness.GetValue(context);

        var rand = new Random(RandomSeed.GetValue(context));
        var fxZoneMode = (ZoneFxModes)FxZoneMode.GetValue(context);

        var pointLight = context.PointLights.GetPointLight(referencedLightIndex);
        var lightPosDx = new Vector4(pointLight.Position, 1);

        var posInViewDx = Vector4.Transform(lightPosDx, worldToClipSpace);
        posInViewDx /= posInViewDx.W;

            
        // Ignore light sources behind
        var hideFactor = posInViewDx.Z < 0 ? 0 : 1;

        posInViewDx /= posInViewDx.W;
        var lightPosInView2D = new Vector2(posInViewDx.X, posInViewDx.Y);
            
        //Vector2 objectScreenPos = lightPosInView2D * positionOnLine * positionFactor + (new System.Numerics.Vector2(1, 1) - positionFactor) * lightPosInView2D;
        Vector2 objectScreenPos = lightPosInView2D * positionFactor;
            
        var sizeWithRandom = size * (float)(1.0 + randomizeSize * (rand.NextDouble() - 0.5)) / 0.2f;

        var spriteColor = Vector4.Clamp(new Vector4(
                                                    color.X + randomizeColor.X * (float)(rand.NextDouble() - 0.5) * 4,
                                                    color.Y + randomizeColor.Y * (float)(rand.NextDouble() - 0.5) * 4,
                                                    color.Z + randomizeColor.Z * (float)(rand.NextDouble() - 0.5) * 4,
                                                    color.W * (1 - randomizeColor.W * (float)(rand.NextDouble() * 2))
                                                   ) * pointLight.Color, Vector4.Zero, Vector4.One);

        // Center Trigger
        if (fxZoneMode != ZoneFxModes.Off)
        {
            var triggerPosition = fxZoneMode == ZoneFxModes.Lights
                                      ? lightPosInView2D
                                      : objectScreenPos;

            var centerTriggerAmount = innerFxZone.X > 0.0001
                                          ? Math.Max(0, innerFxZone.X - triggerPosition.Length()) / innerFxZone.X
                                          : 0;
            float smoothEdgeTriggerAmount = 0;

            if (edgeFxZone.X > 0 && edgeFxZone.Y > 0)
            {
                var insideEdgeTriggerAmountY = Math.Min(edgeFxZone.X, Math.Max(0, Math.Abs(triggerPosition.Y) - 1 + edgeFxZone.X)) /
                                               edgeFxZone.X;
                var outsideEdgeTriggerAmountY = Math.Min(edgeFxZone.Y, Math.Max(0, -Math.Abs(triggerPosition.Y) + 1 + edgeFxZone.Y)) /
                                                edgeFxZone.Y;

                var insideEdgeTriggerAmountX = Math.Min(edgeFxZone.X, Math.Max(0, Math.Abs(triggerPosition.X) - 1 + edgeFxZone.X)) /
                                               edgeFxZone.X;
                var outsideEdgeTriggerAmountX = Math.Min(edgeFxZone.Y, Math.Max(0, -Math.Abs(triggerPosition.X) + 1 + edgeFxZone.Y)) /
                                                edgeFxZone.Y;

                var edgeTriggerAmount = Math.Max(insideEdgeTriggerAmountY * outsideEdgeTriggerAmountY,
                                                 insideEdgeTriggerAmountX * outsideEdgeTriggerAmountX);
                var t = edgeTriggerAmount;
                smoothEdgeTriggerAmount = t * t * (3 - 2 * t);
            }

            var totalTriggerAmount = centerTriggerAmount + smoothEdgeTriggerAmount;
            sizeWithRandom *= (1 + zoneFxScale * totalTriggerAmount).Clamp(0, 100);
            spriteColor.W += (zoneFxBrightness * totalTriggerAmount).Clamp(0, 1);
        }
            
        var aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;
        LightPosition.Value = objectScreenPos * new Vector2(0.5f * aspectRatio,-0.5f);
        TargetPosition.Value = objectScreenPos - Vector2.Normalize(objectScreenPos) * sizeWithRandom;
        LightColor.Value = spriteColor;
    }

    private enum ZoneFxModes
    {
        Off,
        Objects,
        Lights,
    }

    [Input(Guid = "a2408edd-6629-47e2-89c3-a6626218e049")]
    public readonly InputSlot<int> LightIndex = new();
        
    [Input(Guid = "2dffe933-1c5a-46df-aa7e-4e41a4cb2017")]
    public readonly InputSlot<float> Distance = new();

    [Input(Guid = "b28c80c4-e713-4635-a256-23211ea4bea9")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "0f5d8113-64cb-4022-a4b0-9f3ea4bd2596")]
    public readonly InputSlot<float> RandomizeSize = new();

    [Input(Guid = "b6163e2d-1344-4ac5-9704-98e849d3248e")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "92dec9db-e1b0-4f9e-aba5-b7e8900d5bc8")]
    public readonly InputSlot<Vector4> RandomizeColor = new();

    [Input(Guid = "e658e0ac-282b-4a56-bdf0-9b7efadc7e02")]
    public readonly InputSlot<int> RandomSeed = new();

    [Input(Guid = "0ebf8ac6-e075-47c9-ba7a-8a2fd7b0e342")]
    public readonly InputSlot<Vector2> PositionFactor = new();
        
    [Input(Guid = "937e6cd3-ea96-4d93-aa1d-32cf86180b4d")]
    public readonly InputSlot<int> FxZoneMode = new();

    [Input(Guid = "9aa570b6-06e6-4d92-8f5b-c9bba86a108e")]
    public readonly InputSlot<Vector2> EdgeFxZone = new();

    [Input(Guid = "f080db28-3d87-4525-8807-619a7a1c72fc")]
    public readonly InputSlot<Vector2> InnerFxZone = new();

    [Input(Guid = "a2f601e5-1b11-49ea-8d5e-d98dfea91ae5")]
    public readonly InputSlot<float> FxZoneScale = new();

    [Input(Guid = "64f0b189-1d1e-4c9c-bf6a-9c42072352cd")]
    public readonly InputSlot<float> FxZoneBrightness = new();
}