using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Slots;
using T3.Core.Rendering.Material;
using T3.Serialization;
using Buffer = SharpDX.Direct3D11.Buffer;
using Int3 = T3.Core.DataTypes.Vector.Int3;
using Point = T3.Core.DataTypes.Point;
using GeometryShader = T3.Core.DataTypes.GeometryShader;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using PixelShader = T3.Core.DataTypes.PixelShader;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Texture3D = T3.Core.DataTypes.Texture3D;
using VertexShader = T3.Core.DataTypes.VertexShader;

// todo - Default Value Creators should be removed for types that are using their "default" values, i.e. "null" or zero

namespace T3.Core.Model;

public static class JsonToTypeValueConverters
{
    public static Dictionary<Type, Func<JToken, object>> Entries { get; } = new();
}

public static class TypeValueToJsonConverters
{
    public static Dictionary<Type, Action<JsonTextWriter, object>> Entries { get; } = new();
}

public static class InputValueCreators
{
    public static Dictionary<Type, Func<InputValue>> Entries { get; } = new();
}

public static class TypeNameRegistry
{
    public static Dictionary<Type, string> Entries { get; } = new(20);
}

public partial class SymbolPackage
{
    private static void RegisterTypes()
    {
        InputValue InputDefaultValueCreator<T>() => new InputValue<T>();

        // build-in default types
        RegisterType(typeof(float), "float",
                     InputDefaultValueCreator<float>,
                     (writer, obj) => writer.WriteValue((float)obj),
                     jsonToken =>
                     {
                         if (jsonToken.Type == JTokenType.Float || jsonToken.Type == JTokenType.Integer)
                             return jsonToken.Value<float>();

                         return 0;
                     });
        RegisterType(typeof(int), "int",
                     InputDefaultValueCreator<int>,
                     (writer, obj) => writer.WriteValue((int)obj),
                     jsonToken => jsonToken?.Value<int>()??0);
        RegisterType(typeof(bool), "bool",
                     InputDefaultValueCreator<bool>,
                     (writer, obj) => writer.WriteValue((bool)obj),
                     jsonToken => jsonToken?.Value<bool>()??false);
        RegisterType(typeof(double), "double",
                     InputDefaultValueCreator<double>,
                     (writer, obj) => writer.WriteValue((double)obj),
                     jsonToken => jsonToken?.Value<double>()??0);

        RegisterType(typeof(string), "string",
                     () => new InputValue<string>(string.Empty),
                     (writer, value) => writer.WriteValue((string)value),
                     jsonToken => jsonToken?.Value<string>()??string.Empty);

        // system types
        RegisterType(typeof(System.Numerics.Vector2), "Vector2",
                     InputDefaultValueCreator<System.Numerics.Vector2>,
                     (writer, obj) =>
                     {
                         var vec = (System.Numerics.Vector2)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", vec.X);
                         writer.WriteValue("Y", vec.Y);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         float x = jsonToken.ReadValueSafe<float>("X");
                         float y = jsonToken.ReadValueSafe<float>("Y");
                         return new System.Numerics.Vector2(x, y);
                     });
        RegisterType(typeof(System.Numerics.Vector3), "Vector3",
                     InputDefaultValueCreator<System.Numerics.Vector3>,
                     (writer, obj) =>
                     {
                         var vec = (System.Numerics.Vector3)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", vec.X);
                         writer.WriteValue("Y", vec.Y);
                         writer.WriteValue("Z", vec.Z);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         float x = jsonToken.ReadValueSafe<float>("X");
                         float y = jsonToken.ReadValueSafe<float>("Y");
                         float z = jsonToken.ReadValueSafe<float>("Z");
                         return new System.Numerics.Vector3(x, y, z);
                     });
        RegisterType(typeof(System.Numerics.Vector4), "Vector4",
                     () => new InputValue<Vector4>(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)),
                     (writer, obj) =>
                     {
                         var vec = (Vector4)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", vec.X);
                         writer.WriteValue("Y", vec.Y);
                         writer.WriteValue("Z", vec.Z);
                         writer.WriteValue("W", vec.W);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         float x = jsonToken.ReadValueSafe<float>("X");
                         float y = jsonToken.ReadValueSafe<float>("Y");
                         float z = jsonToken.ReadValueSafe<float>("Z");
                         float w = jsonToken.ReadValueSafe<float>("W");
                         return new Vector4(x, y, z, w);
                     });
        RegisterType(typeof(System.Numerics.Quaternion), "Quaternion",
                     () => new InputValue<System.Numerics.Quaternion>(System.Numerics.Quaternion.Identity),
                     (writer, obj) =>
                     {
                         var quaternion = (System.Numerics.Quaternion)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", quaternion.X);
                         writer.WriteValue("Y", quaternion.Y);
                         writer.WriteValue("Z", quaternion.Z);
                         writer.WriteValue("W", quaternion.W);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         float x = jsonToken.ReadValueSafe<float>("X");
                         float y = jsonToken.ReadValueSafe<float>("Y");
                         float z = jsonToken.ReadValueSafe<float>("Z");
                         float w = jsonToken.ReadValueSafe<float>("W");
                         return new System.Numerics.Quaternion(x, y, z, w);
                     });

        RegisterType(typeof(System.Numerics.Matrix4x4), "Matrix4x4",
                     () => new InputValue<System.Numerics.Matrix4x4>(System.Numerics.Matrix4x4.Identity),
                     (writer, obj) =>
                     {
                         var matrix = (System.Numerics.Matrix4x4)obj;
                         //writer.WriteStartObject();
                         writer.WriteStartArray();
                         writer.WriteValue(matrix.M11);
                         writer.WriteValue(matrix.M12);
                         writer.WriteValue(matrix.M13);
                         writer.WriteValue(matrix.M14);

                         writer.WriteValue(matrix.M21);
                         writer.WriteValue(matrix.M22);
                         writer.WriteValue(matrix.M23);
                         writer.WriteValue(matrix.M24);

                         writer.WriteValue(matrix.M31);
                         writer.WriteValue(matrix.M32);
                         writer.WriteValue(matrix.M33);
                         writer.WriteValue(matrix.M34);

                         writer.WriteValue(matrix.M41);
                         writer.WriteValue(matrix.M42);
                         writer.WriteValue(matrix.M43);
                         writer.WriteValue(matrix.M44);
                         writer.WriteEndArray();
                     },
                     jsonToken =>
                     {
                         if (jsonToken is not JArray arr || arr.Count != 16)
                             return System.Numerics.Matrix4x4.Identity;

                         return new Matrix4x4(
                                              JsonUtils.SafeFloatFromArray(arr,0),  JsonUtils.SafeFloatFromArray(arr,1),  JsonUtils.SafeFloatFromArray(arr,2),  JsonUtils.SafeFloatFromArray(arr,3),
                                              JsonUtils.SafeFloatFromArray(arr,4),  JsonUtils.SafeFloatFromArray(arr,5),  JsonUtils.SafeFloatFromArray(arr,6),  JsonUtils.SafeFloatFromArray(arr,7),
                                              JsonUtils.SafeFloatFromArray(arr,8),  JsonUtils.SafeFloatFromArray(arr,9),  JsonUtils.SafeFloatFromArray(arr,10), JsonUtils.SafeFloatFromArray(arr,11),
                                              JsonUtils.SafeFloatFromArray(arr,12), JsonUtils.SafeFloatFromArray(arr,13), JsonUtils.SafeFloatFromArray(arr,14), JsonUtils.SafeFloatFromArray(arr,15)
                                             );     
                     });

        RegisterType(typeof(System.Collections.Generic.List<float>), "List<float>",
                     () => new InputValue<List<float>>([]),
                     (writer, obj) =>
                     {
                         var list = (List<float>)obj;
                         writer.WriteStartObject();
                         writer.WritePropertyName("Values");
                         writer.WriteStartArray();
                         list.ForEach(writer.WriteValue);
                         writer.WriteEndArray();
                         writer.WriteEndObject();
                     },
                     jsonToken => jsonToken.ReadListSafe<float>("Values"));
        RegisterType(typeof(System.Collections.Generic.List<int>), "List<int>",
                     () => new InputValue<List<int>>([]),
                     (writer, obj) =>
                     {
                         var list = (List<int>)obj;
                         writer.WriteStartObject();
                         writer.WritePropertyName("Values");
                         writer.WriteStartArray();
                         list.ForEach(writer.WriteValue);
                         writer.WriteEndArray();
                         writer.WriteEndObject();
                     },
                     jsonToken => jsonToken.ReadListSafe<int>("Values"));

        RegisterType(typeof(System.Collections.Generic.List<string>), "List<string>",
                     () => new InputValue<List<string>>([]),
                     (writer, obj) =>
                     {
                         var list = (List<string>)obj;
                         writer.WriteStartObject();
                         writer.WritePropertyName("Values");
                         writer.WriteStartArray();
                         list.ForEach(writer.WriteValue);
                         writer.WriteEndArray();
                         writer.WriteEndObject();
                     },
                     jsonToken => jsonToken.ReadListSafe<string>("Values"));

        RegisterType(typeof(Int2), nameof(Int2),
                     InputDefaultValueCreator<Int2>,
                     (writer, obj) =>
                     {
                         Int2 vec = (Int2)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", vec.X);
                         writer.WriteValue("Y", vec.Y);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         var widthJson = jsonToken["Width"] ?? jsonToken["X"];
                         var heightJson = jsonToken["Height"] ?? jsonToken["Y"];
                         int width = widthJson.Value<int>();
                         int height = heightJson.Value<int>();
                         return new Int2(width, height);
                     });

        RegisterType(typeof(Int3), "Int3",
                     InputDefaultValueCreator<Int3>,
                     (writer, obj) =>
                     {
                         Int3 vec = (Int3)obj;
                         writer.WriteStartObject();
                         writer.WriteValue("X", vec.X);
                         writer.WriteValue("Y", vec.Y);
                         writer.WriteValue("Z", vec.Z);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         int x = jsonToken.ReadValueSafe<int>("X");
                         int y = jsonToken.ReadValueSafe<int>("Y");
                         int z = jsonToken.ReadValueSafe<int>("Z");
                         return new Int3(x, y, z);
                     });

        // TODO: this is an unfortunate overlap with List<Vector4> and should be resolved.
        RegisterType(typeof(Vector4[]), "Vector4[]",
                     () => new InputValue<Vector4[]>([]));

        RegisterType(typeof(List<Vector4>), "List<Vector4>",
                     () => new InputValue<List<Vector4>>([]),
                     (writer, obj) =>
                     {
                         var list = (List<Vector4>)obj;
                         writer.WriteStartObject();
                         writer.WritePropertyName("Values");
                         writer.WriteStartArray();
                         foreach (var vec in list)
                         {
                             writer.WriteStartObject();
                             writer.WriteValue("X", vec.X);
                             writer.WriteValue("Y", vec.Y);
                             writer.WriteValue("Z", vec.Z);
                             writer.WriteValue("W", vec.W);
                             writer.WriteEndObject();
                         }

                         writer.WriteEndArray();
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         if (jsonToken is not { HasValues: true })
                         {
                             return new List<Vector4>();
                         }

                         var entries = jsonToken["Values"];
                         var list = new List<Vector4>(entries.Count());
                         foreach (var vec4Token in entries)
                         {
                             if (vec4Token == null)
                                 continue;

                             float x = vec4Token.ReadValueSafe<float>("X");
                             float y = vec4Token.ReadValueSafe<float>("Y");
                             float z = vec4Token.ReadValueSafe<float>("Z");
                             float w = vec4Token.ReadValueSafe<float>("W");
                             list.Add(new Vector4(x, y, z, w));
                         }

                         //list.AddRange(entries.Select(entry => entry.Value<Vector4>()));
                         return list;
                     });

        RegisterType(typeof(Dict<float>), "Dict<float>",
                     () => new InputValue<Dict<float>>());

        RegisterType(typeof(System.Text.StringBuilder), "StringBuilder",
                     () => new InputValue<StringBuilder>(new StringBuilder()));

        RegisterType(typeof(DateTime), "DateTime",
                     () => new InputValue<DateTime>(new DateTime()));

        // t3 core types
        RegisterType(typeof(BufferWithViews), "BufferWithViews",
                     () => new InputValue<BufferWithViews>(null));

        RegisterType(typeof(Command), "Command",
                     () => new InputValue<Command>(null));

        RegisterType(typeof(Curve), "Curve",
                     InputDefaultValueCreator<Curve>,
                     (writer, obj) =>
                     {
                         Curve curve = (Curve)obj;
                         writer.WriteStartObject();
                         curve?.Write(writer);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         Curve curve = new Curve();
                         if (jsonToken == null || !jsonToken.HasValues)
                         {
                             curve.AddOrUpdateV(0, new VDefinition() { Value = 0 });
                             curve.AddOrUpdateV(1, new VDefinition() { Value = 1 });
                         }
                         else
                         {
                             curve.Read(jsonToken);
                         }

                         return curve;
                     });

        RegisterType(typeof(DataTypes.Gradient), "Gradient",
                     InputDefaultValueCreator<Gradient>,
                     (writer, obj) =>
                     {
                         Gradient gradient = (Gradient)obj;
                         writer.WriteStartObject();
                         gradient?.Write(writer);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         Gradient gradient = new Gradient();
                         if (jsonToken == null || !jsonToken.HasValues)
                         {
                             gradient = new Gradient();
                         }
                         else
                         {
                             gradient.Read(jsonToken);
                         }

                         return gradient;
                     });
        RegisterType(typeof(LegacyParticleSystem), "LegacyParticleSystem",
                     () => new InputValue<LegacyParticleSystem>(null));

        RegisterType(typeof(ParticleSystem), "ParticleSystem",
                     () => new InputValue<ParticleSystem>(null));

        RegisterType(typeof(T3.Core.Operator.GizmoVisibility), "GizmoVisibility",
                     InputDefaultValueCreator<T3.Core.Operator.GizmoVisibility>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<T3.Core.Operator.GizmoVisibility>);

        RegisterType(typeof(Point[]), "Point",
                     () => new InputValue<Point[]>());
        RegisterType(typeof(RenderTargetReference), "RenderTargetRef",
                     () => new InputValue<RenderTargetReference>());

        // An untyped object that can be used as hack to connect data of unknown type.
        // This can be useful to avoid creating one-off ConnectionTypes for very narrow usecases. 
        RegisterType(typeof(Object), "Object",
                     () => new InputValue<Object>());

        RegisterType(typeof(StructuredList), "StructuredList",
                     () => new InputValue<StructuredList>(),
                     (writer, obj) =>
                     {
                         if (obj is StructuredList l)
                         {
                             l.Write(writer);
                         }
                     },
                     token =>
                     {
                         // This is currently a proof-of-concept implementation.
                         // TODO: support generic structure types
                         try
                         {
                             return new StructuredList<Point>().Read(token);
                         }
                         catch (Exception)
                         {
                             //Log.Warning("Failed to load structured list:" + e.Message);
                             return null;
                         }
                     }
                    );

        // Rendering
        RegisterType(typeof(Texture3dWithViews), "Texture3dWithViews",
                     () => new InputValue<Texture3dWithViews>(new Texture3dWithViews()));

        RegisterType(typeof(MeshBuffers), "MeshBuffers",
                     () => new InputValue<MeshBuffers>(null));

        RegisterType(typeof(DataSet), "DataSet",
                     () => new InputValue<DataSet>());

        RegisterType(typeof(PbrMaterial), "Material",
                     () => new InputValue<PbrMaterial>());

        RegisterType(typeof(ShaderGraphNode), "ShaderGraphNode",
                     () => new InputValue<ShaderGraphNode>());

        RegisterType(typeof(SceneSetup), nameof(SceneSetup),
                     InputDefaultValueCreator<SceneSetup>,
                     (writer, obj) =>
                     {
                         var sceneSetup = (SceneSetup)obj;
                         writer.WriteStartObject();
                         sceneSetup?.Write(writer);
                         writer.WriteEndObject();
                     },
                     jsonToken =>
                     {
                         var sceneSetup = new SceneSetup();
                         if (jsonToken == null || !jsonToken.HasValues)
                         {
                             sceneSetup = new SceneSetup(); // empty
                         }
                         else
                         {
                             sceneSetup.Read(jsonToken);
                         }

                         return sceneSetup;
                     });

        #region sharpdx types
        // todo - add these to CsProject as DefaultUsings dynamically

        // sharpdx types
        RegisterType(typeof(SharpDX.Direct3D.PrimitiveTopology), "PrimitiveTopology",
                     InputDefaultValueCreator<PrimitiveTopology>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<PrimitiveTopology>);
        RegisterType(typeof(SharpDX.Direct3D11.BindFlags), "BindFlags",
                     InputDefaultValueCreator<BindFlags>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<BindFlags>);
        RegisterType(typeof(SharpDX.Direct3D11.BlendOperation), "BlendOperation",
                     InputDefaultValueCreator<BlendOperation>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<BlendOperation>);
        RegisterType(typeof(SharpDX.Direct3D11.BlendOption), "BlendOption",
                     InputDefaultValueCreator<BlendOption>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<BlendOption>);
        RegisterType(typeof(SharpDX.Direct3D11.BlendState), "BlendState",
                     () => new InputValue<BlendState>(null));
        RegisterType(typeof(SharpDX.Direct3D11.Buffer), "Buffer",
                     () => new InputValue<Buffer>(null));
        RegisterType(typeof(SharpDX.Direct3D11.ColorWriteMaskFlags), "ColorWriteMaskFlags",
                     InputDefaultValueCreator<ColorWriteMaskFlags>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<ColorWriteMaskFlags>);
        RegisterType(typeof(SharpDX.Direct3D11.Comparison), "Comparison",
                     InputDefaultValueCreator<Comparison>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<Comparison>);
        RegisterType(typeof(ComputeShader), "ComputeShader",
                     () => new InputValue<ComputeShader>(null));
        RegisterType(typeof(SharpDX.Direct3D11.CpuAccessFlags), "CpuAccessFlags",
                     InputDefaultValueCreator<CpuAccessFlags>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<CpuAccessFlags>);
        RegisterType(typeof(SharpDX.Direct3D11.CullMode), "CullMode",
                     InputDefaultValueCreator<CullMode>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<CullMode>);
        RegisterType(typeof(SharpDX.Direct3D11.DepthStencilState), "DepthStencilState",
                     () => new InputValue<DepthStencilState>(null));
        RegisterType(typeof(SharpDX.Direct3D11.DepthStencilView), "DepthStencilView",
                     () => new InputValue<DepthStencilView>(null));
        RegisterType(typeof(SharpDX.Direct3D11.FillMode), "FillMode",
                     InputDefaultValueCreator<FillMode>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<FillMode>);
        RegisterType(typeof(SharpDX.Direct3D11.Filter), "Filter",
                     InputDefaultValueCreator<Filter>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<Filter>);
        RegisterType(typeof(GeometryShader), "GeometryShader",
                     () => new InputValue<GeometryShader>(null));
        RegisterType(typeof(SharpDX.Direct3D11.InputLayout), "InputLayout",
                     () => new InputValue<InputLayout>(null));
        RegisterType(typeof(PixelShader), "PixelShader",
                     () => new InputValue<PixelShader>(null));
        RegisterType(typeof(SharpDX.Direct3D11.RenderTargetBlendDescription), "RenderTargetBlendDescription",
                     () => new InputValue<RenderTargetBlendDescription>());
        RegisterType(typeof(SharpDX.Direct3D11.RasterizerState), "RasterizerState",
                     () => new InputValue<RasterizerState>(null));
        RegisterType(typeof(SharpDX.Direct3D11.RenderTargetView), "RenderTargetView",
                     () => new InputValue<RenderTargetView>(null));
        RegisterType(typeof(SharpDX.Direct3D11.ResourceOptionFlags), "ResourceOptionFlags",
                     InputDefaultValueCreator<ResourceOptionFlags>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<ResourceOptionFlags>);
        RegisterType(typeof(SharpDX.Direct3D11.ResourceUsage), "ResourceUsage",
                     InputDefaultValueCreator<ResourceUsage>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<ResourceUsage>);
        RegisterType(typeof(SharpDX.Direct3D11.SamplerState), "SamplerState",
                     () => new InputValue<SamplerState>(null));
        RegisterType(typeof(SharpDX.Direct3D11.ShaderResourceView), "ShaderResourceView",
                     () => new InputValue<ShaderResourceView>(null));
        RegisterType(typeof(Texture2D), "Texture2D",
                     () => new InputValue<Texture2D>(null));
        RegisterType(typeof(Texture3D), "Texture3D",
                     () => new InputValue<Texture3D>(null));
        RegisterType(typeof(SharpDX.Direct3D11.TextureAddressMode), "TextureAddressMode",
                     InputDefaultValueCreator<TextureAddressMode>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<TextureAddressMode>);
        RegisterType(typeof(SharpDX.Direct3D11.UnorderedAccessView), "UnorderedAccessView",
                     () => new InputValue<UnorderedAccessView>(null));
        RegisterType(typeof(SharpDX.Direct3D11.UnorderedAccessViewBufferFlags), "UnorderedAccessViewBufferFlags",
                     InputDefaultValueCreator<UnorderedAccessViewBufferFlags>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<UnorderedAccessViewBufferFlags>);
        RegisterType(typeof(VertexShader), "VertexShader",
                     () => new InputValue<VertexShader>(null));
        RegisterType(typeof(SharpDX.DXGI.Format), "Format",
                     InputDefaultValueCreator<Format>,
                     (writer, obj) => writer.WriteValue(obj.ToString()),
                     JsonToEnumValue<Format>);
        RegisterType(typeof(SharpDX.Mathematics.Interop.RawRectangle), "RawRectangle",
                     () => new InputValue<RawRectangle>(new RawRectangle { Left = -100, Right = 100, Bottom = -100, Top = 100 }));
        RegisterType(typeof(SharpDX.Mathematics.Interop.RawViewportF), "RawViewportF",
                     () => new InputValue<RawViewportF>(new RawViewportF
                                                            { X = 0.0f, Y = 0.0f, Width = 100.0f, Height = 100.0f, MinDepth = 0.0f, MaxDepth = 10000.0f }));
        #endregion

        return;

        // generic enum value from json function, must be local function
        object JsonToEnumValue<T>(JToken jsonToken) where T : struct // todo: use 7.3 and replace with enum
        {
            var value = jsonToken.Value<string>();

            if (Enum.TryParse(value, out T enumValue))
            {
                return enumValue;
            }

            return null;
        }
    }

    private static void RegisterType(Type type, string typeName,
                                     Func<InputValue> defaultValueCreator,
                                     Action<JsonTextWriter, object> valueToJsonConverter,
                                     Func<JToken, object> jsonToValueConverter)
    {
        typeName ??= type.Name;
        RegisterType(type, typeName, defaultValueCreator);
        TypeValueToJsonConverters.Entries.Add(type, valueToJsonConverter);
        JsonToTypeValueConverters.Entries.Add(type, jsonToValueConverter);
    }

    private static void RegisterType(Type type, string typeName, Func<InputValue> defaultValueCreator)
    {
        TypeNameRegistry.Entries.Add(type, typeName);
        InputValueCreators.Entries.Add(type, defaultValueCreator);
    }

    /// <summary>
    /// Dependencies to other packages based on resources
    /// </summary>
    public IEnumerable<SymbolPackage> ResourceDependencies => Array.Empty<SymbolPackage>();

    /// <summary>
    /// Dependencies to other packages based on symbols
    /// </summary>
    public IEnumerable<SymbolPackage> SymbolDependencies => Array.Empty<SymbolPackage>();
}