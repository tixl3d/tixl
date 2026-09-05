using T3.Core.Utils;

namespace Lib.io.json;

[Guid("6535edc3-a4ed-46c7-ae71-d3974612b448")]
internal sealed class RequestUrl : Instance<RequestUrl>
{
    [Output(Guid = "664c217c-8c7e-4bc8-870e-409f6f67ad33")]
    public readonly Slot<string> Result = new();

    public RequestUrl()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var wasTriggered = MathUtils.WasTriggered( TriggerRequest.GetValue(context), ref _triggered);
        var isUrlDirty = Url.DirtyFlag.IsDirty;
        var url = Url.GetValue(context);

        if(wasTriggered || isUrlDirty)
            Download2(url);
    }

    private bool _triggered;
        
    private async void Download2(string url)
    {
        try
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            var request = new HttpRequestMessage
                              {
                                  RequestUri = new Uri(url),
                                  Method = HttpMethod.Get,
                              };
            //request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // request.Headers.Add("apikey",
            //                     "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im95am9ieXB5b3prdWZyYnJtcW50Iiwicm9sZSI6ImFub24iLCJpYXQiOjE2ODAwMDA4MTUsImV4cCI6MTk5NTU3NjgxNX0.9Km2SmPnOQre_obeKr_DSr2l33PECtMWIF1VuxE1zck");
            // request.Headers.Add("Authorization",
            //                     "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im95am9ieXB5b3prdWZyYnJtcW50Iiwicm9sZSI6ImFub24iLCJpYXQiOjE2ODAwMDA4MTUsImV4cCI6MTk5NTU3NjgxNX0.9Km2SmPnOQre_obeKr_DSr2l33PECtMWIF1VuxE1zck");
            var httpResponseTask = await client.SendAsync(request);
            Result.Value =await httpResponseTask.Content.ReadAsStringAsync();;
            Result.DirtyFlag.Invalidate();
        }
        catch (Exception e)
        {
            Log.Warning("Download failed: " + e.Message);
        }
    }
        
    [Input(Guid = "8c7a6493-af56-4a03-a41f-796e30902cfd")]
    public readonly InputSlot<string> Url = new();
        
    [Input(Guid = "4DC9DEF5-958B-4A9C-8755-827BF4E5EAA6")]
    public readonly InputSlot<bool> TriggerRequest = new();
}