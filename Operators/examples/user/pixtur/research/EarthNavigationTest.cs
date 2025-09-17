using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Examples.user.pixtur.research;

[Guid("b3f34926-e536-439b-b47b-2ab89a0bc94d")]
public class EarthNavigationTest : Instance<EarthNavigationTest>
{
    [Output(Guid = "cab5c207-a997-4934-8c53-5a9f740284e0")]
    public readonly Slot<System.Numerics.Vector4[]> Result = new();

    [Output(Guid = "8DBFC03F-7DCF-42EB-A8B1-301C0085BF40")]
    public readonly Slot<Vector3> P1 = new();

    [Output(Guid = "71E2EF72-12CA-4705-9F9C-9CDA4256FAE2")]
    public readonly Slot<Vector3> P2 = new();
        
    [Output(Guid = "3187B8B8-2D08-480D-9975-A09AE9EB140C")]
    public readonly Slot<Vector3> P3 = new();

    [Output(Guid = "F5244E55-9520-44C2-80E8-98BC3B97BC7F")]
    public readonly Slot<List<float>> P4 = new();

        
    public EarthNavigationTest()
    {
        Result.UpdateAction += Update;
        P1.UpdateAction += Update;
        P2.UpdateAction += Update;
        P3.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var activateControl = ActivateControl.GetValue(context);
        var controlsChanged = ForwardBackwards.DirtyFlag.IsDirty;
        var forward = ForwardBackwards.GetValue(context);
        var orientation = Orientation.GetValue(context);
        var radius = EarthRadius.GetValue(context);

        var height = HeightAboveSurface.GetValue(context);
            
        var latitude =  MathUtils.Fmod(Latitude.GetValue(context), 360 );
        var longitude =   Longitude.GetValue(context) ;

        var m = Matrix4x4.Identity;
            
        //m *= Matrix.RotationY(-latitude / 180 * MathF.PI);
        //m *= Matrix.RotationX(longitude / 180 * MathF.PI);
        m *= Matrix4x4.CreateRotationZ(orientation / 180 * MathF.PI);

        var mRot1 = m;
        m *= Matrix4x4.CreateTranslation(0, 0, -height - radius);


        var sign = orientation < 0 ? -1 : 1;
        var adjustment = MathF.Pow(longitude / 90, 12) * 90f * sign;
        var adjustedOrientation = orientation + adjustment;  
            
        var surfaceToWorld = GetSurfaceToWorldMatrix(orientation, radius, longitude, latitude);

        var pOnSurface = Vector3.Zero;
        var forwardOnSurface = Vector3.UnitX * 0.01f;
            
        // var pOnSurface = SharpDX.Vector4.Transform(new SharpDX.Vector4(0.0f,0,0,1), earthToSurface);
        // var p1 = new System.Numerics.Vector3(pOnSurface.X, pOnSurface.Y, pOnSurface.Z);
        P1.Value =  T(pOnSurface, surfaceToWorld);
            
        // var pOnSurface2 = SharpDX.Vector4.Transform(new SharpDX.Vector4(0.2f,0,0,1), earthToSurface);
        // var p2 = new System.Numerics.Vector3(pOnSurface2.X, pOnSurface2.Y, pOnSurface2.Z);
        // P2.Value = p2;
        var forwardInWorld = T(Vector3.Zero + forwardOnSurface, surfaceToWorld);
        P2.Value =  T(Vector3.Zero + forwardOnSurface * 10, surfaceToWorld);

        var n = Vector3.Normalize(forwardInWorld);
        var lat2 = -MathF.Atan2(n.X, n.Z) * 180/MathF.PI;
        var limit = 89f;
        var long2 = (MathF.Asin(n.Y) * 180/MathF.PI).Clamp(-limit, limit);
            
        var surfaceToWorld2 = GetSurfaceToWorldMatrix(orientation, radius, long2, lat2);
            
        //long2 = MathUtils.Clamp(long2, -179, 179);
            
        Log.Debug($"Long: {long2:0.0}, Lat:{lat2:0.0} + AdjL:{adjustment:0.00}", this);
        P2.Value =  T(Vector3.Zero + forwardOnSurface * 10, surfaceToWorld);
            
        // var pOnSurfaceForward = SharpDX.Vector3.TransformNormal(new SharpDX.Vector3(0,0,1), objectToSurface);             
        // P2.Value = pOnSurface.ToNumerics() * radius - Vector3.UnitZ *(height + radius) ;


        // var p1 = SharpDX.Vector3.TransformNormal(new SharpDX.Vector3(0,0,1), mRot1);
        // P1.Value = p1.ToNumerics() * radius - Vector3.UnitZ *(height + radius);
        //
        // var mRot2 = mRot1 * Matrix.RotationX(forward / 180 * MathF.PI);
        // var p2 = SharpDX.Vector3.TransformNormal(new SharpDX.Vector3(0,0,1), mRot2);
        // P2.Value = p2.ToNumerics() * radius - Vector3.UnitZ *(height + radius);
            
        //P1.Value = p1.ToNumerics() * radius - Vector3.UnitZ *(height + radius);
            
            
        if (activateControl)
        {
            Longitude.TypedInputValue.Value = (float)long2;
            Longitude.Input.IsDefault = false;
            Longitude.DirtyFlag.Invalidate();
                
            Latitude.TypedInputValue.Value = (float)lat2;// MathF.Sin(orientation / 180 * MathF.PI) * forward;
            Latitude.Input.IsDefault = false;
            Latitude.DirtyFlag.Invalidate();
        }

        // ActivateControl.TypedInputValue.Value = true;
        // ActivateControl.Input.IsDefault = false;
        // ActivateControl.DirtyFlag.Invalidate();
        // if (controlsChanged || activateControl)
        // {
        //
        //     (latitude, longitude) = GetPointByDistanceAndHeading(Latitude.GetValue(context), Longitude.GetValue(context), orientation, forward, radius);
        //     
        //     Longitude.TypedInputValue.Value = (float)longitude;
        //     Longitude.Input.IsDefault = false;
        //     Longitude.DirtyFlag.Invalidate();
        //
        //     Latitude.TypedInputValue.Value = (float)latitude;// MathF.Sin(orientation / 180 * MathF.PI) * forward;
        //     Latitude.Input.IsDefault = false;
        //     Latitude.DirtyFlag.Invalidate();
        // }

        //var longitude = Longitude.GetValue(context);
        //var latitude = Latitude.GetValue(context);
            
            

        // transpose all as mem layout in hlsl constant buffer is row based
        m.Transpose();

        _matrix[0] = m.Row1();
        _matrix[1] = m.Row2();
        _matrix[2] = m.Row3();
        _matrix[3] = m.Row4();
        Result.Value = _matrix;
    }

    private static Matrix4x4 GetSurfaceToWorldMatrix(float orientation, float radius, float longitude, float latitude)
    {
        var surfaceToWorld = Matrix4x4.Identity;
        surfaceToWorld *= Matrix4x4.CreateRotationZ(orientation / 180 * MathF.PI);
        surfaceToWorld *= Matrix4x4.CreateTranslation(new System.Numerics.Vector3(0, 0, radius));
        surfaceToWorld *= Matrix4x4.CreateRotationX(-longitude / 180 * MathF.PI);
        surfaceToWorld *= Matrix4x4.CreateRotationY(-latitude / 180 * MathF.PI);
        return surfaceToWorld;
    }

    private System.Numerics.Vector4[] _matrix = new System.Numerics.Vector4[4];

    private Vector3 T(Vector3 p, Matrix4x4 m)
    {
        var pt = Vector4.Transform(new System.Numerics.Vector4(p.X,p.Y,p.Z,1), m);
        return new System.Numerics.Vector3(pt.X, pt.Y, pt.Z) / pt.W;
    }
        
    // lat =asin(sin(lat1)*cos(d)+cos(lat1)*sin(d)*cos(tc))
    // dlon=atan2(sin(tc)*sin(d)*cos(lat1),cos(d)-sin(lat1)*sin(lat))
    // lon=mod( lon1-dlon +pi,2*pi )-pi            
    // public Tuple<float, float> GetPointByDistanceAndHeading(double fmLat, double fmLon, double heading, double distanceKm, double earthRadius)
    // {
    //     double bearingR = heading.ToRadians();
    //     double latR = fmLat.ToRadians();
    //     double lonR = fmLon.ToRadians();
    //
    //     double distanceToRadius = distanceKm / earthRadius;
    //
    //     double newLatR = Math.Asin(Math.Sin(latR) * Math.Cos(distanceToRadius)
    //                                + Math.Cos(latR) * Math.Sin(distanceToRadius) * Math.Cos(bearingR));
    //
    //     double newLonR = lonR + Math.Atan2(
    //                                        Math.Sin(bearingR) * Math.Sin(distanceToRadius) * Math.Cos(latR),
    //                                        Math.Cos(distanceToRadius) - Math.Sin(latR) * Math.Sin(newLatR)
    //                                       );
    //
    //     newLonR = MathUtils.Fmod(newLonR + MathF.PI, 2 * MathF.PI) - MathF.PI;
    //     //newLatR = MathUtils.Fmod(newLatR + MathF.PI, 2 * MathF.PI) - MathF.PI;
    //     return new Tuple<float, float>((float)newLatR.ToDegrees(), (float)newLonR.ToDegrees());
    // }

        
    [Input(Guid = "F5B2A750-9C2E-488F-90CD-9905E75382A7")]
    public readonly InputSlot<float> Longitude = new();

    [Input(Guid = "5CC61C18-E4D2-4042-8A30-06DC107D864F")]
    public readonly InputSlot<float> Latitude =
        new();

    [Input(Guid = "E1BB90F8-B9F2-441E-ACCA-C2CF5CA56258")]
    public readonly InputSlot<float> HeightAboveSurface =
        new();

    [Input(Guid = "E8015F0A-DF3D-4ACA-A1DC-B3ABD2473F9F")]
    public readonly InputSlot<float> Orientation =
        new();

    [Input(Guid = "97903119-6773-4819-A373-D32C03A946BD")]
    public readonly InputSlot<float> ForwardBackwards =
        new();

    [Input(Guid = "76E8A7F1-F24C-451E-967E-01D6C40D19D7")]
    public readonly InputSlot<float> RightLeft = new();

    [Input(Guid = "CFB98977-1F6A-4002-8BEE-EBCF0F9D68B2")]
    public readonly InputSlot<float> UpDown = new();

    [Input(Guid = "D2356A3F-030B-42B1-B699-3E4FACAD5BB4")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "9305E2F7-B51C-4A99-A462-948CEC25C4C0")]
    public readonly InputSlot<float> EarthRadius = new();

    [Input(Guid = "c0dbc42d-bf79-417b-af75-840611eba4c5")]
    public readonly InputSlot<System.Numerics.Vector3> Translation = new();

    [Input(Guid = "e6c462ce-52d1-4680-867d-6b1e52bb52cf")]
    public readonly InputSlot<System.Numerics.Vector3> Rotation = new();

    [Input(Guid = "97c5c461-c3ee-4385-8057-1f4ec575d52b")]
    public readonly InputSlot<System.Numerics.Vector3> Scale = new();

    [Input(Guid = "d33bcd6a-b25f-4381-b342-93eb6da6eb68")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "5f6d286d-8583-48ba-a9d2-4cc3af79052d")]
    public readonly InputSlot<bool> ActivateControl = new();
}
    
// public static class NumericExtensions
// {
//     public static double ToRadians(this double degrees)
//     {
//         return (Math.PI / 180) * degrees;
//     }
//
//     public static double ToDegrees(this double radians)
//     {
//         return (180 / Math.PI) * radians;
//     }
// }    