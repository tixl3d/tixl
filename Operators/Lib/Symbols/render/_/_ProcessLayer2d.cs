using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.@_;

[Guid("d8699da1-13aa-42f7-816a-88abb1d0ba06")]
internal sealed class _ProcessLayer2d : Instance<_ProcessLayer2d>
{
    [Output(Guid = "D81A2DB8-D72D-48B1-9201-0EE87822097E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector4[]> Result = new();
        
    public _ProcessLayer2d()
    {
        Result.UpdateAction += Update;
    }

        
    private void Update(EvaluationContext context)
    {
        var imageTexture = ImageTexture.GetValue(context);
        var imageSize = new Int2(1,1);
        if (imageTexture != null)
        {
            try
            {
                imageSize = new Int2(imageTexture.Description.Width, imageTexture.Description.Height);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to get texture description: " + e.Message, this);
            }
        }

        var sizeF = new Vector2(imageSize.Width, imageSize.Height);
        var imageAspect = sizeF.X / sizeF.Y;
            
        float viewAspect = context.CameraToClipSpace.M22 / context.CameraToClipSpace.M11;
            
            
        var scale = Scale.GetValue(context) * Stretch.GetValue(context);

        var rz = RotationZ.GetValue(context);
        var yaw = 0;
        var pitch = 0;
        var roll = rz.ToRadians();
        var posXy = PositionXy.GetValue(context);
        var posZ = PositionZ.GetValue(context);
            
        var mode = ScaleMode.GetEnumValue<ScaleModes>(context);
        if (mode == ScaleModes.FitBoth)
        {
            mode = imageAspect < viewAspect ? ScaleModes.FitHeight : ScaleModes.FitWidth;
        }
            
        switch (mode)
        {
            case ScaleModes.FitHeight:
                scale.X *= imageAspect;
                break;
                
            case ScaleModes.FitWidth:
                scale.X *= viewAspect;
                scale.Y *= viewAspect / imageAspect;
                break;
                
            case ScaleModes.Cover:
                var ratio = viewAspect / imageAspect;
                if (ratio > 1 )
                {
                    scale.X *= viewAspect;
                    scale.Y *= viewAspect / imageAspect;
                }
                else
                {
                    scale.X *= viewAspect / ratio;
                    scale.Y *= viewAspect / imageAspect / ratio;
                }
                break;
            case ScaleModes.Stretch:
                scale.X *= viewAspect;
                break;
                
            case ScaleModes.MatchPixelResolution:
                var f = context.RequestedResolution.Height / (float) imageSize.Height;
                scale.X = scale.X * imageAspect / f;
                scale.Y /= f;

                var viewHeight = context.RequestedResolution.Height;
                var viewWidth = context.RequestedResolution.Width;
                    
                if ((viewWidth - imageSize.Width) % 2 !=0)
                {
                    posXy.X += (viewAspect) / viewWidth;
                }
                    
                if ((viewHeight - imageSize.Height) % 2 !=0)
                {
                    posXy.Y -= 1f / viewHeight;
                }
                break;
        }
            
            


        var t = new Vector3(posXy.X, posXy.Y, posZ);
        var objectToParentObject = GraphicsMath.CreateTransformationMatrix(scalingCenter: Vector3.Zero, scalingRotation: Quaternion.Identity, scaling: new Vector3(scale.X, scale.Y, 1), rotationCenter: Vector3.Zero,
                                                                           rotation: Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll), translation: new Vector3(t.X, t.Y, t.Z));
            
        // Transpose all as mem layout in hlsl constant buffer is row based
        objectToParentObject.Transpose();
            
        _matrix[0] = objectToParentObject.Row1();
        _matrix[1] = objectToParentObject.Row2();
        _matrix[2] = objectToParentObject.Row3();
        _matrix[3] = objectToParentObject.Row4();
        Result.Value = _matrix;
    }

    private readonly Vector4[] _matrix = new Vector4[4];
        
        
    [Input(Guid = "674E048E-5A6C-4D3E-B1E0-E44603775E02")]
    public readonly InputSlot<Vector2> PositionXy = new();
        
    [Input(Guid = "0060AFE4-642B-4DA4-B6A5-ACFAB84DE205")]
    public readonly InputSlot<float> PositionZ = new();
        
    [Input(Guid = "28D90F8C-CEAE-46B8-9711-E62BA7826CE1")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "91191320-FB10-4AE7-908C-2E2A03027F9E")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "1ECD57A7-F5DD-49EA-B950-ACB86AF78FF9")]
    public readonly InputSlot<float> RotationZ = new();
        
    [Input(Guid = "4D8E40C0-F4E4-4FDF-A4E0-24EB12C518D6", MappedType = typeof(ScaleModes))]
    public readonly InputSlot<int> ScaleMode = new ();
        
    [Input(Guid = "39122251-DBC0-449E-8A56-5305CBBC3DAA")]
    public readonly InputSlot<Texture2D> ImageTexture = new ();
        
    public enum ScaleModes
    {
        FitHeight,
        FitWidth,
        FitBoth,
        Cover,
        Stretch,
        MatchPixelResolution,
    }
}