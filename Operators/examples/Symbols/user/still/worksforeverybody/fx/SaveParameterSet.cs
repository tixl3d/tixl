using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Utils;

namespace Examples.user.still.worksforeverybody.fx;

[Guid("3246cf5a-3c9b-4765-89d1-68852a3dd7a1")]
internal sealed class SaveParameterSet : Instance<SaveParameterSet>
{
    [Output(Guid = "90F4D983-685E-499B-B121-0D7F34669490")]
    public readonly Slot<StructuredList> Points = new();

    [Output(Guid = "a75a50ac-9361-4cac-8208-3c79f329bad2")]
    public readonly Slot<string> Result = new();
        
    public SaveParameterSet()
    {
        Result.UpdateAction += Update;
        Points.UpdateAction += Update;
        UpdatePointLists();
    }

    private void Update(EvaluationContext context)
    {
        var sceneIndex = SceneIndex.GetValue(context).Clamp(0, MaxSceneCount - 1);
        var saveNewTriggered = MathUtils.WasTriggered(TriggerSaveParameters.GetValue(context), ref _triggerSaveParameters);
            
        //Log.Debug("  save new triggered " + saveNewTriggered, this);
        if (!_initialized)
        {
            _initialized = true;
            FetchParameters();
        }
        else
        {
            if (saveNewTriggered)
                SaveNewEntry(context, sceneIndex);

            var points = _parameterPoints[sceneIndex];
            if (points.NumElements == 0)
                points = _emptyList;
                
            Points.Value = points;
        }
            
        // Avoid double evaluation
        Points.DirtyFlag.Clear();
        Result.DirtyFlag.Clear();
    }

        
    private async void FetchParameters()
    {
        if (_requesting)
            return;

        _requesting = true;
        try
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            var request = new HttpRequestMessage
                              {
                                  RequestUri = new Uri("https://oyjobypyozkufrbrmqnt.supabase.co/rest/v1/params?select=data"),
                                  Method = HttpMethod.Get,
                              };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("apikey", Token);
            request.Headers.Add("Authorization", "Bearer " + Token);
            var httpResponseTask = await client.SendAsync(request);
            var jsonString = await httpResponseTask.Content.ReadAsStringAsync();

            ParseData(jsonString);
            Result.Value = jsonString;
            Result.DirtyFlag.Invalidate();
        }
        catch (Exception e)
        {
            Log.Warning("Download failed: " + e.Message);
        }

        _initialized = true;
    }

    private void ParseData(string jsonString)
    {
        _parameterSets.Clear();
        using var streamReader = new StringReader(jsonString);
        using var jsonReader = new JsonTextReader(streamReader);

        try
        {
            var o = JToken.ReadFrom(jsonReader);
            foreach (var childJson in (JArray)o)
            {
                var dataToken = childJson["data"];
                var s = dataToken?.ToObject<ParameterSet>();
                if (s == null)
                    continue;

                _parameterSets.Add(s);
            }

            UpdatePointLists();
        }
        catch (Exception e)
        {
            Log.Warning("parsing data failed:" + e.Message);
        }
    }

    private void UpdatePointLists()
    {
        var paramsForScenes = new List<ParameterSet>[MaxSceneCount];
        for (var i = 0; i < MaxSceneCount ; i++)
        {
            paramsForScenes[i] = new List<ParameterSet>();
        }

        // Count parameters for all scenes
        foreach (var p in _parameterSets)
        {
            if (p.SceneIndex < 0 && p.SceneIndex >= MaxSceneCount)
            {
                Log.Warning($"Skipping parameter set for invalid scene index {p.SceneIndex}", this);
                continue;
            }

            paramsForScenes[p.SceneIndex].Add(p);
        }

        // Initialize point buffers
        for (var sceneIndex = 0; sceneIndex < MaxSceneCount ; sceneIndex++)
        {
            var pointList = new StructuredList<Point>(paramsForScenes[sceneIndex].Count);

            var parameters = paramsForScenes[sceneIndex];
            for (var paramIndex = 0; paramIndex < parameters.Count; paramIndex++)
            {
                var parameter = parameters[paramIndex];
                pointList.TypedElements[paramIndex] = new Point()
                                                          {
                                                              Position = new System.Numerics.Vector3(parameter.X, -parameter.Y, 0),
                                                              F1 = 1,
                                                          };
            }

            _parameterPoints[sceneIndex] = pointList;
        }
    }

    private void SaveNewEntry(EvaluationContext context, int sceneIndex)
    {
        Log.Debug("Save triggered", this);
        var position = Position.GetValue(context);
        var newSet = new ParameterSet
                         {
                             X = position.X,
                             Y = position.Y,
                             SceneIndex = sceneIndex,
                             UserHash = 0
                         };

        SendDataAsync(newSet);
        _parameterSets.Add(newSet);

        UpdatePointLists();
    }

    private static async void SendDataAsync(ParameterSet parameterSet)
    {
        Log.Debug("Saving parameterSet...");
        try
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            var request = new HttpRequestMessage
                              {
                                  RequestUri = new Uri("https://oyjobypyozkufrbrmqnt.supabase.co/rest/v1/params"),
                                  Method = HttpMethod.Post,
                              };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            request.Headers.Add("apikey", Token);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("Prefer", "return=minimal");

            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(parameterSet);
            var content = new StringContent("{\"data\":" + jsonString + "}", Encoding.UTF8, "application/json");
            request.Content = content;

            var httpTask = await client.SendAsync(request);
            var readAsStringAsync = await httpTask.Content.ReadAsStringAsync();
            Log.Debug("Response for saving: " + readAsStringAsync);
        }
        catch (Exception e)
        {
            Log.Warning("Saving failed: " + e.Message);
        }
    }

    private const int MaxSceneCount = 40;

    private const string Token =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im95am9ieXB5b3prdWZyYnJtcW50Iiwicm9sZSI6ImFub24iLCJpYXQiOjE2ODAwMDA4MTUsImV4cCI6MTk5NTU3NjgxNX0.9Km2SmPnOQre_obeKr_DSr2l33PECtMWIF1VuxE1zck";

    private bool _triggerSaveParameters;
    private readonly StructuredList<Point> _emptyList = new(1);
    private readonly StructuredList<Point>[] _parameterPoints = new StructuredList<Point>[MaxSceneCount];
    private readonly List<ParameterSet> _parameterSets = new();

    private bool _requesting;
    private bool _initialized;

    [Serializable]
    private class ParameterSet
    {
        public float X;
        public float Y;
        public int SceneIndex;
        public int UserHash; // Todo
    }

    [Input(Guid = "D6A017AF-DF4E-4532-8A4A-208DF224D60A")]
    public readonly InputSlot<bool> TriggerSaveParameters = new();

    [Input(Guid = "BDF7D9AB-0E73-4323-816D-AF5CA0A60989")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "4AA59A00-B56E-4634-AD96-A72EAC3EB859")]
    public readonly InputSlot<int> SceneIndex = new();

    // [Input(Guid = "A01BF21F-7673-4D4A-AB65-C2333BD4C8DA")]
    // public readonly InputSlot<int> TimeStampInSeconds = new();
    //
    // [Input(Guid = "BE977A53-7B32-4EB7-B23A-338B0D1C7B6E")]
    // public readonly InputSlot<int> UserHash = new();
}