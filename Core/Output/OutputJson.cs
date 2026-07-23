#nullable enable
using System;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>Shared JSON helpers for Setup serialization: vectors as compact arrays, Guids as strings.</summary>
internal static class OutputJson
{
    // JsonUtils.WriteValue<T> is struct-constrained, so strings need their own overload
    public static void WriteString(this JsonTextWriter writer, string name, string value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    public static void WriteVector2(this JsonTextWriter writer, string name, Vector2 v)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteValue(v.X);
        writer.WriteValue(v.Y);
        writer.WriteEndArray();
    }

    public static void WriteVector3(this JsonTextWriter writer, string name, Vector3 v)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteValue(v.X);
        writer.WriteValue(v.Y);
        writer.WriteValue(v.Z);
        writer.WriteEndArray();
    }

    public static void WriteVector4(this JsonTextWriter writer, string name, Vector4 v)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteValue(v.X);
        writer.WriteValue(v.Y);
        writer.WriteValue(v.Z);
        writer.WriteValue(v.W);
        writer.WriteEndArray();
    }

    public static void WriteQuaternion(this JsonTextWriter writer, string name, Quaternion q)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteValue(q.X);
        writer.WriteValue(q.Y);
        writer.WriteValue(q.Z);
        writer.WriteValue(q.W);
        writer.WriteEndArray();
    }

    public static void WriteInt2(this JsonTextWriter writer, string name, Int2 v)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteValue(v.Width);
        writer.WriteValue(v.Height);
        writer.WriteEndArray();
    }

    public static void WriteQuad(this JsonTextWriter writer, string name, Vector2[] quad)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var p in quad)
        {
            writer.WriteStartArray();
            writer.WriteValue(p.X);
            writer.WriteValue(p.Y);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    public static Vector2 ReadVector2(JToken? token, Vector2 fallback = default)
    {
        if (token is not JArray { Count: >= 2 } arr)
            return fallback;

        return new Vector2(JsonUtils.SafeFloatFromArray(arr, 0),
                           JsonUtils.SafeFloatFromArray(arr, 1));
    }

    public static Vector3 ReadVector3(JToken? token, Vector3 fallback = default)
    {
        if (token is not JArray { Count: >= 3 } arr)
            return fallback;

        return new Vector3(JsonUtils.SafeFloatFromArray(arr, 0),
                           JsonUtils.SafeFloatFromArray(arr, 1),
                           JsonUtils.SafeFloatFromArray(arr, 2));
    }

    public static Vector4 ReadVector4(JToken? token, Vector4 fallback = default)
    {
        if (token is not JArray { Count: >= 4 } arr)
            return fallback;

        return new Vector4(JsonUtils.SafeFloatFromArray(arr, 0),
                           JsonUtils.SafeFloatFromArray(arr, 1),
                           JsonUtils.SafeFloatFromArray(arr, 2),
                           JsonUtils.SafeFloatFromArray(arr, 3));
    }

    public static Quaternion ReadQuaternion(JToken? token)
    {
        if (token is not JArray { Count: >= 4 } arr)
            return Quaternion.Identity;

        return new Quaternion(JsonUtils.SafeFloatFromArray(arr, 0),
                              JsonUtils.SafeFloatFromArray(arr, 1),
                              JsonUtils.SafeFloatFromArray(arr, 2),
                              JsonUtils.SafeFloatFromArray(arr, 3));
    }

    public static Int2 ReadInt2(JToken? token, Int2 fallback)
    {
        if (token is not JArray { Count: >= 2 } arr)
            return fallback;

        return new Int2((int)JsonUtils.SafeFloatFromArray(arr, 0),
                        (int)JsonUtils.SafeFloatFromArray(arr, 1));
    }

    public static Vector2[] ReadQuad(JToken? token)
    {
        var quad = new Vector2[4];
        if (token is not JArray arr)
            return quad;

        for (var i = 0; i < Math.Min(4, arr.Count); i++)
            quad[i] = ReadVector2(arr[i]);

        return quad;
    }

    public static Guid ReadGuid(JToken? token)
    {
        return JsonUtils.TryGetGuid(token, out var guid) ? guid : Guid.Empty;
    }
}
