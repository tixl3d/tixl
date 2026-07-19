using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace T3.Core.Output;

/// <summary>
/// One L3 constraint: "this projected pixel lands on this physical point".
/// Six or more spanning two planes constrain a projector's camera.
/// </summary>
public sealed class CalibrationPoint
{
    public Vector3 StagePosition;

    /// <summary>Output-canvas pixel, top-left origin, y down.</summary>
    public Vector2 OutputPixel;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteVector3("StagePosition", StagePosition);
        writer.WriteVector2("OutputPixel", OutputPixel);
        writer.WriteEndObject();
    }

    public static CalibrationPoint ReadFromJson(JToken token)
    {
        return new CalibrationPoint
                   {
                       StagePosition = OutputJson.ReadVector3(token["StagePosition"]),
                       OutputPixel = OutputJson.ReadVector2(token["OutputPixel"]),
                   };
    }
}
