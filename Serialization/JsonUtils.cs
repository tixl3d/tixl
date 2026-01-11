#nullable enable
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;

namespace T3.Serialization;

public static class JsonUtils
{
    public static T ReadEnum<T>(JToken? o, string name) where T : struct, Enum
    {
        if (o == null)
            return default;
        
        var dirtyFlagJson = o[name];
        return dirtyFlagJson != null
                   ? Enum.Parse<T>(dirtyFlagJson.Value<string>() ?? string.Empty)
                   : default;
    }

    /// <summary>
    /// A simple wrapper that prevents most exceptions on nulls on malformed definitions and instead returns a default
    /// </summary>
    public static T? ReadValueSafe<T>(this JToken? token, string name, T? defaultValue = default)
    {
        try
        {
            var t = token?[name];
            return t != null ? t.Value<T>() : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
    
    public static List<T> ReadListSafe<T>(
        this JToken? token,
        string name,
        Func<JToken, T>? elementConverter = null)
    {
        if (token is not JObject obj || obj[name] is not JArray arr)
            return [];

        var list = new List<T>(arr.Count);

        foreach (var e in arr)
        {
            if (e == null)
                continue;

            try
            {
                list.Add(
                         elementConverter != null
                             ? elementConverter(e)
                             : e.Value<T>()
                        );
            }
            catch
            {
                // skip malformed element
            }
        }

        return list;
    }


    public static T? TryLoadingJson<T>(string filepath) where T : class
    {
        if(!TryLoadingJson(filepath, out T? result))
        {
            return default;
        }
        
        return result;
    }

    public static bool TryLoadingJson<T>(string filepath, [NotNullWhen(true)] out T? result)
    {
        if (!File.Exists(filepath))
        {
            Log.Debug($"{filepath} doesn't exist yet");
            result = default;
            return false;
        }

        var jsonBlob = File.ReadAllText(filepath);
        var serializer = JsonSerializer.Create();
        var fileTextReader = new StringReader(jsonBlob);
        try
        {
            if (serializer.Deserialize(fileTextReader, typeof(T)) is T configurations)
            {
                result = configurations;
                return true;
            }

            Log.Error($"Can't load {filepath}");
            result = default;
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"Can't load {filepath}:" + e.Message);
            result = default;
            return false;
        }
    }




    
    
    public static bool TrySaveJson<T>(T dataObject, string filepath) 
    {
        if (string.IsNullOrEmpty(filepath))
        {
            Log.Warning($"Can't save {typeof(T)} to empty filename...");
            return false;
        }

        var serializer = JsonSerializer.Create();
        serializer.Formatting = Formatting.Indented;
        try
        {
            using var streamWriter = File.CreateText(filepath);
            serializer.Serialize(streamWriter, dataObject);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't create file {filepath} to save {typeof(T)} " + e.Message);
            return false;
        }
    }

    public static void WriteValue<T>(this JsonTextWriter writer, string name, T value) where T : struct
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }

    public static void WriteObject(this JsonTextWriter writer, string name, object value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value.ToString());
    }

    public static bool TryGetGuid(JToken? token, out Guid guid)
    {
        if (token == null)
        {
            guid = Guid.Empty;
            return false;
        }

        var guidString = token.Value<string>();
        return Guid.TryParse(guidString, out guid);
    }

    public static bool TryReadToken<T>(this JToken? token, [NotNullWhen(true)] out  T?  result)
    {
        result = default;
        if(token == null )
            return false;
        
        result = token.Value<T>() ?? default;
        return result != null;
    }
    
    
    public static bool TryGetEnumValue<T>(JToken? token, out T enumValue) where T : struct, Enum
    {
        enumValue = default;
        var stringValue = token?.Value<string>();
        if (string.IsNullOrEmpty(stringValue))
            return false;

        if (!Enum.TryParse<T>(stringValue, out var result)) 
            return false;
        
        enumValue = result;
        return true;
    }
    
    public static T GetEnumValue<T>(this JToken? token, T fallback = default) where T : struct, Enum
    {
        var s = token?.Value<string>();

        if (string.IsNullOrEmpty(s))
            return default; 

        return Enum.TryParse<T>(s, out var result) ? result : fallback;
    }
    
    public static float SafeFloatFromArray(JArray arr, int i)
    {
        if ( arr.Count <= i)
            return 0f;
        
        var t = arr[i];
        return  t.Type is JTokenType.Float or JTokenType.Integer
                   ? t.Value<float>()
                   : 0f;
    }
    
}

public sealed class SafeEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T ReadJson(JsonReader reader, Type objectType, T existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        try
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                {
                    var str = reader.Value?.ToString();
                    if (Enum.TryParse(str, ignoreCase: true, out T result) && Enum.IsDefined(typeof(T), result))
                        return result;
                    break;
                }
                case JsonToken.Integer:
                {
                    int intVal = Convert.ToInt32(reader.Value);
                    if (Enum.IsDefined(typeof(T), intVal))
                        return (T)Enum.ToObject(typeof(T), intVal);
                    break;
                }
            }
        }
        catch
        {
            Log.Warning($"Failed to read enum {reader.Value} as {typeof(T)}");
        }

        return default; // fallback value
    }

    public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString());
    }
}