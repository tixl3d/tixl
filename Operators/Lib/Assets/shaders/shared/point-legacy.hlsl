struct LegacyPoint
{
    float3 position;
    float w;
    float4 rotation;
    float4 color;
    float3 velocity;
    float extra;
};

// struct Particle
// {
//     LegacyPoint p;

//     float3 velocity;
//     float birthTime;

//     float radius;
//     float3 __extra;
// };


#define QUATERNION_IDENTITY float4(0, 0, 0, 1)

#ifndef PI
#define PI 3.14159265359f
#endif 

const static float NAN = sqrt(-1);

#ifndef mod
#define mod(x, y) ((x) - (y) * floor((x) / (y)))
#endif 

inline float q_separate_v(float4 q, out float4 normalized ) 
{
    float l = length(q);
    normalized = q / l; 
    return l - 1;
}

inline float4 q_encode_v(float4 q, float v ) 
{
    return q * (v + 1);
}


float4 qMul(float4 q1, float4 q2)
{ 
    return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

// Vector rotation with a quaternion
// http://mathworld.wolfram.com/Quaternion.html
inline float3 qRotateVec3(float3 v, float4 quat)
{
    float4 r_c = quat * float4(-1, -1, -1, 1);
    return qMul(quat, qMul(float4(v, 0), r_c)).xyz;
}

// https://blog.molecular-matters.com/2013/05/24/a-faster-quaternion-vector-multiplication/
inline float3 qRotateVec3(float3 v, float4 q)
{
    float3 t = 2 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}


float4 qConjugate(float4 q)
{
    return float4(-q.x, -q.y, -q.z, q.w);
}

// https://jp.mathworks.com/help/aeroblks/quaternioninverse.html
float4 qInverse(float4 q)
{
    float4 conj = qConjugate(q);
    return conj / (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
}


// A given angle of rotation about a given axis
float4 qFromAngleAxis(float angle, float3 axis)
{
    float sn = sin(angle * 0.5);
    float cs = cos(angle * 0.5);
    return float4(axis * sn, cs);
}

// https://stackoverflow.com/questions/1171849/finding-quaternion-representing-the-rotation-from-one-vector-to-another
float4 qFromVectors(float3 v1, float3 v2)
{
    float4 q;
    float d = dot(v1, v2);
    if (d < -0.999999)
    {
        float3 right = float3(1, 0, 0);
        float3 up = float3(0, 1, 0);
        float3 tmp = cross(right, v1);
        if (length(tmp) < 0.000001)
        {
            tmp = cross(up, v1);
        }
        tmp = normalize(tmp);
        q = qFromAngleAxis(PI, tmp);
    } else if (d > 0.999999) {
        q = QUATERNION_IDENTITY;
    } else {
        q.xyz = cross(v1, v2);
        q.w = 1 + d;
        q = normalize(q);
    }
    return q;
}

float4 qLookAt(float3 forward, float3 up)
{
    float3 right = normalize(cross(forward, up));
    up = normalize(cross(forward, right));

    float m00 = right.x;
    float m01 = right.y;
    float m02 = right.z;
    float m10 = up.x;
    float m11 = up.y;
    float m12 = up.z;
    float m20 = forward.x;
    float m21 = forward.y;
    float m22 = forward.z;

    float num8 = (m00 + m11) + m22;
    float4 q = QUATERNION_IDENTITY;
    if (num8 > 0.0)
    {
        float num = sqrt(num8 + 1.0);
        q.w = num * 0.5;
        num = 0.5 / num;
        q.x = (m12 - m21) * num;
        q.y = (m20 - m02) * num;
        q.z = (m01 - m10) * num;
        return q;
    }

    if ((m00 >= m11) && (m00 >= m22))
    {
        float num7 = sqrt(((1.0 + m00) - m11) - m22);
        float num4 = 0.5 / num7;
        q.x = 0.5 * num7;
        q.y = (m01 + m10) * num4;
        q.z = (m02 + m20) * num4;
        q.w = (m12 - m21) * num4;
        return q;
    }

    if (m11 > m22)
    {
        float num6 = sqrt(((1.0 + m11) - m00) - m22);
        float num3 = 0.5 / num6;
        q.x = (m10 + m01) * num3;
        q.y = 0.5 * num6;
        q.z = (m21 + m12) * num3;
        q.w = (m20 - m02) * num3;
        return q;
    }

    float num5 = sqrt(((1.0 + m22) - m00) - m11);
    float num2 = 0.5 / num5;
    q.x = (m20 + m02) * num2;
    q.y = (m21 + m12) * num2;
    q.z = 0.5 * num5;
    q.w = (m01 - m10) * num2;
    return q;
}

float4 qSlerp(in float4 a, in float4 b, float t)
{
    // if either input is zero, return the other.
    if (length(a) == 0.0)
    {
        if (length(b) == 0.0)
        {
            return QUATERNION_IDENTITY;
        }
        return b;
    }
    else if (length(b) == 0.0)
    {
        return a;
    }

    float cosHalfAngle = a.w * b.w + dot(a.xyz, b.xyz);

    if (cosHalfAngle >= 1.0 || cosHalfAngle <= -1.0)
    {
        return a;
    }
    else if (cosHalfAngle < 0.0)
    {
        b.xyz = -b.xyz;
        b.w = -b.w;
        cosHalfAngle = -cosHalfAngle;
    }

    float blendA;
    float blendB;
    if (cosHalfAngle < 0.99)
    {
        // do proper slerp for big angles
        float halfAngle = acos(cosHalfAngle);
        float sinHalfAngle = sin(halfAngle);
        float oneOverSinHalfAngle = 1.0 / sinHalfAngle;
        blendA = sin(halfAngle * (1.0 - t)) * oneOverSinHalfAngle;
        blendB = sin(halfAngle * t) * oneOverSinHalfAngle;
    }
    else
    {
        // do lerp if angle is really small.
        blendA = 1.0 - t;
        blendB = t;
    }

    float4 result = float4(blendA * a.xyz + blendB * b.xyz, blendA * a.w + blendB * b.w);
    if (length(result) > 0.0)
    {
        return normalize(result);
    }
    return QUATERNION_IDENTITY;
}


float4 qFromEuler(float yaw, float pitch, float roll)
{
return float4(
        sin(roll/2) * cos(pitch/2) * cos(yaw/2) - cos(roll/2) * sin(pitch/2) * sin(yaw/2),
        cos(roll/2) * sin(pitch/2) * cos(yaw/2) + sin(roll/2) * cos(pitch/2) * sin(yaw/2),
        cos(roll/2) * cos(pitch/2) * sin(yaw/2) - sin(roll/2) * sin(pitch/2) * cos(yaw/2),
        cos(roll/2) * cos(pitch/2) * cos(yaw/2) + sin(roll/2) * sin(pitch/2) * sin(yaw/2));
}

float4x4 qToMatrix(float4 quat)
{
    float4x4 m = 0; //float4x4(float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0));

    float x = quat.x, y = quat.y, z = quat.z, w = quat.w;
    float x2 = x + x, y2 = y + y, z2 = z + z;
    float xx = x * x2, xy = x * y2, xz = x * z2;
    float yy = y * y2, yz = y * z2, zz = z * z2;
    float wx = w * x2, wy = w * y2, wz = w * z2;

    m[0][0] = 1.0 - (yy + zz);
    m[0][1] = xy - wz;
    m[0][2] = xz + wy;

    m[1][0] = xy + wz;
    m[1][1] = 1.0 - (xx + zz);
    m[1][2] = yz - wx;

    m[2][0] = xz - wy;
    m[2][1] = yz + wx;
    m[2][2] = 1.0 - (xx + yy);

    m[3][3] = 1.0;

    return m;
}

// Transposed matrix
float4x4 quaternion_to_tmatrix(float4 quat)
{
    float4x4 m = 0;

    float x = quat.x, y = quat.y, z = quat.z, w = quat.w;
    float x2 = x + x, y2 = y + y, z2 = z + z;
    float xx = x * x2, xy = x * y2, xz = x * z2;
    float yy = y * y2, yz = y * z2, zz = z * z2;
    float wx = w * x2, wy = w * y2, wz = w * z2;

    m[0][0] = 1.0 - (yy + zz);
    m[1][0] = xy - wz;
    m[2][0] = xz + wy;

    m[0][1] = xy + wz;
    m[1][1] = 1.0 - (xx + zz);
    m[2][1] = yz - wx;

    m[0][2] = xz - wy;
    m[1][2] = yz + wx;
    m[2][2] = 1.0 - (xx + yy);

    m[3][3] = 1.0;

    return m;
}



float4 qFromMatrix3 (float3x3 m) 
{   
    float w = sqrt( 1.0 + m._m00 + m._m11 + m._m22) / 2.0;
    float w4 = (4.0 * w);
    float x = (m._m21 - m._m12) / w4 ;
    float y = (m._m02 - m._m20) / w4 ;
    float z = (m._m10 - m._m01) / w4 ;
    return float4(x,y,z,w);
}

float4 qFromMatrix3Precise (float3x3 m) 
{   
    float tr = m._m00 + m._m11 + m._m22;

    if (tr > 0) { 
        float S = sqrt(tr+1.0) * 2; // S=4*qw 
        return float4(
            (m._m21 - m._m12) / S,
            (m._m02 - m._m20) / S,
            (m._m10 - m._m01) / S, 
            0.25 * S
        );
    } else if ((m._m00 > m._m11)&(m._m00 > m._m22)) { 
        float S = sqrt(1.0 + m._m00 - m._m11 - m._m22) * 2; // S=4*qx 
        return float4(
            0.25 * S,
            (m._m01 + m._m10) / S ,
            (m._m02 + m._m20) / S ,
            (m._m21 - m._m12) / S
        );
    } else if (m._m11 > m._m22) { 
        float S = sqrt(1.0 + m._m11 - m._m00 - m._m22) * 2; // S=4*qy
        return float4(
            (m._m01 + m._m10) / S,
            0.25 * S,
            (m._m12 + m._m21) / S,
            (m._m02 - m._m20) / S
        );
    } else { 
        float S = sqrt(1.0 + m._m22 - m._m00 - m._m11) * 2; // S=4*qz
        return float4(
            (m._m02 + m._m20) / S,
            (m._m12 + m._m21) / S,
            0.25 * S,
            (m._m10 - m._m01) / S
        );
    }
}


