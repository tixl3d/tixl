namespace Lib.point.generate;

[Guid("b2267122-4223-4eff-8ae4-91d149df535c")]
internal sealed class _DoyleSpiralRoot : Instance<_DoyleSpiralRoot>
{
    [Output(Guid = "CA14BDE7-B1A4-4E42-B685-6126AE724D64")]
    public readonly Slot<Vector2> A = new();

    [Output(Guid = "97441B22-FD51-4438-BE6A-3533E4FF81B5")]
    public readonly Slot<Vector2> B = new();

    [Output(Guid = "B750BB07-9820-4E6B-BCD9-1935598ECC05")]
    public readonly Slot<float> R = new();

    public _DoyleSpiralRoot()
    {
        A.UpdateAction += Update;
        B.UpdateAction += Update;
        B.UpdateAction += Update;
    }
        

    double _d(double z, double t, double p, double q)
    {
        // The square of the distance between z*e^(it) and z*e^(it)^(p/q).
        var w = Math.Pow(z, p / q);
        var s = (p * t + 2 * Math.PI) / q;
        return (
                   Math.Pow(z * Math.Cos(t) - w * Math.Cos(s), 2)
                   + Math.Pow(z * Math.Sin(t) - w * Math.Sin(s), 2)
               );
    }

    double ddz_d(double z, double t, double p, double q)
    {
        // The partial derivative of _d with respect to z.
        var w = Math.Pow(z, p / q);
        var s = (p * t + 2 * Math.PI) / q;
        var ddz_w = (p / q) * Math.Pow(z, (p - q) / q);

        return (
                   2 * (w * Math.Cos(s) - z * Math.Cos(t)) * (ddz_w * Math.Cos(s) - Math.Cos(t))
                   + 2 * (w * Math.Sin(s) - z * Math.Sin(t)) * (ddz_w * Math.Sin(s) - Math.Sin(t))
               );
    }

    double ddt_d(double z, double t, double p, double q)
    {
        // The partial derivative of _d with respect to t.
        var w = Math.Pow(z, p / q);
        var s = (p * t + 2 * Math.PI) / q;
        var dds_t = (p / q);
        return (
                   2 * (z * Math.Cos(t) - w * Math.Cos(s)) * (-z * Math.Sin(t) + w * Math.Sin(s) * dds_t)
                   + 2 * (z * Math.Sin(t) - w * Math.Sin(s)) * (z * Math.Cos(t) - w * Math.Cos(s) * dds_t)
               );
    }

    double _s(double z, double t, double p, double q)
    {
        // The square of the sum of the origin-distance of z*e^(it) and
        // the origin-distance of z*e^(it)^(p/q).
        return Math.Pow(z + Math.Pow(z, p / q), 2);
    }

    double ddz_s(double z, double t, double p, double q)
    {
        // The partial derivative of _s with respect to z.
        var w = Math.Pow(z, p / q);
        var ddz_w = (p / q) * Math.Pow(z, (p - q) / q);
        return 2 * (w + z) * (ddz_w + 1);
    }

    /*
    double ddt_s(z,t, p,q) {
        // The partial derivative of _s with respect to t.
        return 0;
    }
    */

    double _r(double z, double t, double p, double q)
    {
        // The square of the radius-ratio implied by having touching circles
        // centred at z*e^(it) and z*e^(it)^(p/q).
        return _d(z, t, p, q) / _s(z, t, p, q);
    }

    double ddz_r(double z, double t, double p, double q)
    {
        // The partial derivative of _r with respect to z.
        return (
                   ddz_d(z, t, p, q) * _s(z, t, p, q)
                   - _d(z, t, p, q) * ddz_s(z, t, p, q)
               ) / Math.Pow(_s(z, t, p, q), 2);
    }

    double ddt_r(double z, double t, double p, double q)
    {
        // The partial derivative of _r with respect to t.
        return (
                   ddt_d(z, t, p, q) * _s(z, t, p, q)
                   /* - _d(z,t,p,q) * ddt_s(z,t,p,q) */ // omitted because ddt_s is constant at zero
               ) / Math.Pow(_s(z, t, p, q), 2);
    }

    const double epsilon = 1e-7;

    // We want to find (z, t) such that:
    //    _r(z,t,0,1) = _r(z,t,p,q) = _r(Math.Pow(z, p/q), (p*t + 2*pi)/q, 0,1)
    //
    // so we define functions _f and _g to be zero when these equalities hold,
    // and use 2d Newton-Raphson to find a joint root of _f and _g.
    (float aMag, float aAng, float bMag, float bAng, float r) FindRootAngles(double p, double q)
    {
        double _f(double z, double t)
        {
            return _r(z, t, 0, 1) - _r(z, t, p, q);
        }

        double ddz_f(double z, double t)
        {
            return ddz_r(z, t, 0, 1) - ddz_r(z, t, p, q);
        }

        double ddt_f(double z, double t)
        {
            return ddt_r(z, t, 0, 1) - ddt_r(z, t, p, q);
        }

        double _g(double z, double t)
        {
            return _r(z, t, 0, 1) - _r(Math.Pow(z, p / q), (p * t + 2 * Math.PI) / q, 0, 1);
        }

        double ddz_g(double z, double t)
        {
            return ddz_r(z, t, 0, 1) - ddz_r(Math.Pow(z, p / q), (p * t + 2 * Math.PI) / q, 0, 1) * (p / q) * Math.Pow(z, (p - q) / q);
        }

        double ddt_g(double z, double t)
        {
            return ddt_r(z, t, 0, 1) - ddt_r(Math.Pow(z, p / q), (p * t + 2 * Math.PI) / q, 0, 1) * (p / q);
        }

        (bool ok, double z, double t, double r) FindRoot(double z, double t)
        {
            for (int loopIndex = 0; loopIndex < 100; loopIndex++)
            {
                var v_f = _f(z, t);
                var v_g = _g(z, t);

                if (-epsilon < v_f && v_f < epsilon && -epsilon < v_g && v_g < epsilon)
                {
                    return (true, z, t, Math.Sqrt(_r(z, t, 0, 1)));
                }

                var a = ddz_f(z, t);
                var b = ddt_f(z, t);
                var c = ddz_g(z, t);
                var d = ddt_g(z, t);

                var det = a * d - b * c;
                if (-epsilon < det && det < epsilon)
                    return (false, 0, 0, 0);

                z -= (d * v_f - b * v_g) / det;
                t -= (a * v_g - c * v_f) / det;

                if (z < epsilon)
                    return (false, 0, 0, 0);
            }

            Log.Debug("couldn't solve", this);
            return (false, 0, 0, 0);
        }

        var (ok, rootZ, rootT, rootR) = FindRoot(2, 0);
        if (!ok)
        {
            return (0, 0, 0, 0, 0);
        }

        // var a = [root.z * Math.Cos(t1), d1 * sin(t1) ],
        //     coroot = {z: Math.Pow(d1, p/q), t: (p*t1+2*pi)/q},
        //     b = [coroot.z * Math.Cos(coroot.t), coroot.z * sin(coroot.t) ];

        //Log.Debug($"{rootZ}  {rootT}  {rootR}", this);
        //rootT = Math.Max(rootT, 0.00000001);
        var r2 = (float)Math.Sqrt(_r(rootZ, rootT, 0, 1));
        var w = Math.Pow(rootZ, (p / q));
        var s = (p * rootT + 2 * Math.PI) / q;
        return (aMag: (float)rootZ,
                aAng: (float)rootT,
                bMag: (float)w,
                bAng: (float)s, r2);
    }
        
    private void Update(EvaluationContext context)
    {
        var p = P.GetValue(context);
        var q = Q.GetValue(context);

        var (aMag, aAng, bMag, bAng, r) = FindRootAngles(p, q);
            
        A.Value = new Vector2(aMag, aAng);
        B.Value = new Vector2(bMag, bAng);
        R.Value = r;

        A.DirtyFlag.Clear();
        B.DirtyFlag.Clear();
        R.DirtyFlag.Clear();

        //Log.Debug($" DoyleParams: {aMag}, {aAng} {bMag} {bAng}", this);
    }

    [Input(Guid = "e0ee8c5d-d8c2-4856-858c-d25570a71679")]
    public readonly InputSlot<float> P = new();

    [Input(Guid = "8a4c30b3-a189-4fa1-adc5-66e7a68c75ba")]
    public readonly InputSlot<float> Q = new();
}