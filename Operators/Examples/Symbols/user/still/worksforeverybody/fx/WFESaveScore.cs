using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Utils;

namespace Examples.user.still.worksforeverybody.fx;

[Guid("40676c51-ecca-4bc3-bd4a-eeb80fc0b937")]
internal sealed class WFESaveScore : Instance<WFESaveScore>
{
    [Output(Guid = "54E534A7-3659-486C-8B27-67296D1DB7CC")]
    public readonly Slot<List<string>> Results = new();

    public WFESaveScore()
    {
        Results.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        if (!_initialized)
        {
            _greetings = GreetingScores.GetValue(context);
            _initialized = true;
            InsertFallbackScores();
            UpdateHighScores();
            //Log.Debug("here2 " + _allScores.Count, this);
            FetchScores();
            //Log.Debug("high score strings:" + string.Join(" ",_highScoreStrings));
        }
            
        var score = Score.GetValue(context).Clamp(0,100000);
        var saveNewTriggered = MathUtils.WasTriggered(TriggerSave.GetValue(context), ref _triggerSave);
        if (saveNewTriggered)
            SaveAndUploadScore(score);

        Results.Value = _highScoreStrings;
    }

    private void UpdateHighScores()
    {
        _highScoreStrings.Clear();
        var index = 0;
        foreach (var s in _allScores.OrderByDescending(s => s.Score))
        {
            const int maxPadding = 12;
            var filler = (maxPadding - s.PlayerName.Length).Clamp(0, maxPadding);
            var dots = new string('.', filler);
            var lastGameMarker = s.Hash == _lastScoreHash ? "> " : "  ";
            _highScoreStrings.Add($"{lastGameMarker}{s.PlayerName} {dots} {s.Score:00000}");

            index++;
            if (index == MaxHighScoreCount - 1)
                break;
        } 
    }

    private async void FetchScores()
    {
        try
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            var request = new HttpRequestMessage
                              {
                                  RequestUri = new Uri("https://oyjobypyozkufrbrmqnt.supabase.co/rest/v1/scores?select=data"),
                                  Method = HttpMethod.Get,
                              };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("apikey", Token);
            request.Headers.Add("Authorization", "Bearer " + Token);
            var httpResponseTask = await client.SendAsync(request);
            var jsonString = await httpResponseTask.Content.ReadAsStringAsync();

            ParseAndAppendData(jsonString);
            Results.Value = _highScoreStrings;
        }
        catch (Exception e)
        {
            Log.Warning("Download failed: " + e.Message);
        }
    }

    private void ParseAndAppendData(string jsonString)
    {
        using var streamReader = new StringReader(jsonString);
        using var jsonReader = new JsonTextReader(streamReader);

        try
        {
            var o = JToken.ReadFrom(jsonReader);
            foreach (var childJson in (JArray)o)
            {
                var dataToken = childJson["data"];
                var s = dataToken?.ToObject<HighScoreEntry>();
                if (s == null)
                    continue;

                Log.Debug("Found parameters! " + s.PlayerName);
                _allScores.Add(s);
            }
            UpdateHighScores();
        }
        catch (Exception e)
        {
            Log.Warning("parsing data failed:" + e.Message);
        }
    }

    private void InsertFallbackScores()
    {
        if (string.IsNullOrEmpty(_greetings))
            return;

        var lines = _greetings.Split("\n");
        for (var index = 0; index < lines.Length; index++)
        {
            var g = lines[index];
            _allScores.Add(new HighScoreEntry
                               {
                                   PlayerName = g,
                                   Score = (lines.Length - index + 1) * 10,
                                   Hash = -1
                               });
        }
    }

    private void SaveAndUploadScore(int score)
    {
        if (score <= 0)
        {
            Log.Debug($"Score {score} not good enough to save...", this);
            return;
        }

        var userName = System.Environment.UserName.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = "Random";
        }

        var shortenedPlayerName = Regex.Replace(userName.ToUpperInvariant(), @"[^ABCDEFGHIJKLMNOPQRSTUVWXYZ0-9]", "",
                                                RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        const int maxLength = 5;

        do
        {
            var randomIndex = (int)(_random.NextDouble() * shortenedPlayerName.Length).Clamp(1, shortenedPlayerName.Length - 2);
            shortenedPlayerName = shortenedPlayerName.Remove(randomIndex, 1);
        }
        while (shortenedPlayerName.Length > maxLength);

        var hash = shortenedPlayerName.GetHashCode() + score; // yay... crypto baby
        if (hash == _lastScoreHash)
        {
            Log.Debug("already added score", this);
            return; 
        }
            
        Log.Debug($"Save score {score} , {shortenedPlayerName}", this);
        _lastScoreHash = hash;
        var newEntry = new HighScoreEntry
                           {
                               PlayerName = shortenedPlayerName,
                               Score = score,
                               Hash = hash
                           };
        _allScores.Add(newEntry);
        UpdateHighScores();
        SendDataAsync(newEntry);
    }

    private static Random _random = new(42);
    private int _lastScoreHash;

    private async void SendDataAsync(HighScoreEntry newEntry)
    {
        Log.Debug("Saving new scores...", this);
        try
        {
            var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
            var request = new HttpRequestMessage
                              {
                                  RequestUri = new Uri("https://oyjobypyozkufrbrmqnt.supabase.co/rest/v1/scores"),
                                  Method = HttpMethod.Post,
                              };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            request.Headers.Add("apikey", Token);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("Prefer", "return=minimal");

            var jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(newEntry);
            var content = new StringContent("{\"data\":" + jsonString + "}", Encoding.UTF8, "application/json");
            request.Content = content;

            var httpTask = await client.SendAsync(request);
            var result = await httpTask.Content.ReadAsStringAsync();
            Log.Debug("Result saving score: " + result);
        }
        catch (Exception e)
        {
            Log.Warning("Saving failed: " + e.Message);
        }
    }

    private const string Token =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im95am9ieXB5b3prdWZyYnJtcW50Iiwicm9sZSI6ImFub24iLCJpYXQiOjE2ODAwMDA4MTUsImV4cCI6MTk5NTU3NjgxNX0.9Km2SmPnOQre_obeKr_DSr2l33PECtMWIF1VuxE1zck";

    private bool _triggerSave;
    private readonly List<string> _highScoreStrings = new(20);
    private readonly List<HighScoreEntry> _allScores = new(100);

    private const int MaxHighScoreCount = 21;
    private bool _initialized;
    private string _greetings;

 
    [Serializable]
    private class HighScoreEntry
    {
        public string PlayerName;
        public int Score;
        public int Hash; // No. That's not safe :)
    }

    [Input(Guid = "c0a2bbea-663b-4e15-8861-57b7db7a7300")]
    public readonly InputSlot<bool> TriggerSave = new();

    [Input(Guid = "7bb45f86-c144-4cf0-a13f-9a60fe4d83cf")]
    public readonly InputSlot<int> Score = new();
        
    [Input(Guid = "A5713C6D-DF65-4E36-851E-01DFFFCDEB9B")]
    public readonly InputSlot<string> GreetingScores = new();
}