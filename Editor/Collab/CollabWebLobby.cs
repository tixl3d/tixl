#nullable enable
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Collab;

/// <summary>
/// The HTTP session browser: talks to a self-hosted collab backend (CollabBackend/api.php) that
/// keeps the party-matching list, whispers and WebRTC signaling. The browser prefers this
/// backend when an address is configured; the relay's own listing covers the no-backend case.
/// </summary>
internal static class CollabWebLobby
{
    /// <summary>Result of the last successful refresh. Replaced as a whole, never changed in place.</summary>
    public static IReadOnlyList<LobbyRoom> Rooms => _rooms;

    public static bool IsRefreshing => _refreshing != 0;

    /// <summary>Why the last refresh failed; empty after a success.</summary>
    public static string ErrorText { get; private set; } = "";

    public static DateTime LastRefreshUtc { get; private set; } = DateTime.MinValue;

    private static volatile LobbyRoom[] _rooms = [];
    private static int _refreshing;

    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TiXLCollab/1.0");
        return client;
    }

    /// <summary>The configured backend base URL without a trailing slash; empty when unset.</summary>
    public static string BackendUrl => (UserSettings.Config.CollabBackendUrl ?? "").Trim().TrimEnd('/');

    public static bool IsConfigured => BackendUrl.Length > 0;

    /// <summary>Fetches the backend's session list. Fire-and-forget; results land in Rooms.</summary>
    public static void Refresh()
    {
        if (!IsConfigured || Interlocked.Exchange(ref _refreshing, 1) != 0)
            return;

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var (rooms, error) = await FetchListAsync(CancellationToken.None);
                                 if (error != null)
                                 {
                                     ErrorText = $"Could not load the session list: {error}";
                                 }
                                 else
                                 {
                                     _rooms = rooms;
                                     ErrorText = "";
                                     LastRefreshUtc = DateTime.UtcNow;
                                 }
                             }
                             finally
                             {
                                 Interlocked.Exchange(ref _refreshing, 0);
                             }
                         });
    }

    private static async Task<(LobbyRoom[] Rooms, string? Error)> FetchListAsync(CancellationToken cancellation)
    {
        using var response = await _http.GetAsync(BackendUrl + "?op=list", cancellation);
        var text = await response.Content.ReadAsStringAsync(cancellation);
        JObject json;
        try
        {
            json = JObject.Parse(text);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return ([], "the backend answered with something that is not JSON");
        }

        if (!(json["ok"]?.Value<bool>() ?? false))
            return ([], json["error"]?.Value<string>() ?? "the backend refused the list request");

        var rooms = new List<LobbyRoom>();
        foreach (var token in json["rooms"] ?? new JArray())
        {
            if (token is not JObject entry)
                continue;

            var code = CollabProtocol.GetString(entry, "code");
            if (string.IsNullOrEmpty(code))
                continue;

            CollabProtocol.TryGetBool(entry, "pwd", out var passwordProtected);
            CollabProtocol.TryGetInt(entry, "members", out var members);
            CollabProtocol.TryGetInt(entry, "max", out var maxMembers);
            CollabProtocol.TryGetBool(entry, "editable", out var editable);
            rooms.Add(new LobbyRoom(code,
                                    CollabProtocol.GetString(entry, "title"),
                                    CollabProtocol.GetString(entry, "purpose"),
                                    CollabProtocol.GetString(entry, "project"),
                                    CollabProtocol.GetString(entry, "host"),
                                    CollabProtocol.GetString(entry, "lang"),
                                    false,
                                    members,
                                    maxMembers,
                                    passwordProtected,
                                    CollabProtocol.GetString(entry, "relay"),
                                    CollabProtocol.GetString(entry, "os"),
                                    CollabProtocol.GetString(entry, "region"),
                                    editable));
        }

        return (rooms.ToArray(), null);
    }

    // ---------------------------------------------------------------------------------------
    // Host side: the listing lifecycle of a hosted session
    // ---------------------------------------------------------------------------------------

    private static async Task<(JToken? Body, string? Error)> PostAsync(string op, JObject payload, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BackendUrl}?op={op}");
            request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return (null, "the backend answered with something that is not JSON");
            }

            var ok = json["ok"]?.Value<bool>() ?? false;
            return ok ? (json, null) : (null, json["error"]?.Value<string>() ?? "the backend refused the request");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }

    private static JObject RoomPayload(string room, string key)
    {
        return new JObject
                   {
                       ["room"] = room,
                       ["key"] = key,
                   };
    }

    /// <summary>Registers the hosted session in the backend's list. Fire-and-forget.</summary>
    public static void Register(string room, string key, string relayEndpoint, string title, string purpose, string project,
                                string hostName, string lang, bool passwordProtected, bool listed, bool editable = true)
    {
        if (!IsConfigured)
            return;

        var payload = RoomPayload(room, key);
        payload["relay"] = relayEndpoint ?? "";
        payload["title"] = title;
        payload["purpose"] = purpose;
        payload["project"] = project;
        payload["host"] = hostName;
        payload["lang"] = lang;
        payload["os"] = HostPlatform();
        payload["region"] = RegionFromLocale(string.IsNullOrWhiteSpace(lang)
                                                ? System.Globalization.CultureInfo.CurrentCulture.Name
                                                : lang);
        payload["editable"] = editable;
        payload["pwd"] = passwordProtected;
        payload["listed"] = listed;
        payload["members"] = 1;
        payload["max"] = 18;

        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("register", payload, CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: register failed: {error}");
                         });
    }

    /// <summary>The platform this editor runs on, as the party table's "race" column shows it.</summary>
    public static string HostPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "Win";
        if (OperatingSystem.IsMacOS())
            return "Mac";
        if (OperatingSystem.IsLinux())
            return "Linux";
        return "?";
    }

    /// <summary>The account's region for the "location" column: the locale's country part when it
    /// has one (hu-HU -> HU), else the language code itself (hu -> HU).</summary>
    public static string RegionFromLocale(string locale)
    {
        var text = (locale ?? "").Trim();
        if (text.Length == 0)
            return "";

        var parts = text.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 1 ? parts[1] : parts[0]).ToUpperInvariant();
    }

    /// <summary>Keeps the listing alive; the backend expires entries without heartbeats in 90 s.
    /// Also carries the spectator-mode flag so the table's "type" column stays current.</summary>
    public static void Heartbeat(string room, string key, int members, bool editable)
    {
        if (!IsConfigured)
            return;

        var payload = RoomPayload(room, key);
        payload["members"] = members;
        payload["editable"] = editable;
        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("heartbeat", payload, CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: heartbeat failed: {error}");
                         });
    }

    /// <summary>Change entry: title/purpose/visibility of the running session.</summary>
    public static void Update(string room, string key, string title, string purpose, string project, bool listed, bool? editable = null)
    {
        if (!IsConfigured)
            return;

        var payload = RoomPayload(room, key);
        payload["title"] = title;
        payload["purpose"] = purpose;
        payload["project"] = project;
        payload["listed"] = listed;
        if (editable.HasValue)
            payload["editable"] = editable.Value;
        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("update", payload, CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: update failed: {error}");
                         });
    }

    /// <summary>Delete entry: removes the listing (session ended or unlisted).</summary>
    public static void Unregister(string room, string key)
    {
        if (!IsConfigured)
            return;

        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("unregister", RoomPayload(room, key), CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: unregister failed: {error}");
                         });
    }

    // ---------------------------------------------------------------------------------------
    // Whispers: pre-join contact through the backend
    // ---------------------------------------------------------------------------------------

    /// <summary>A visitor whispers the host of a listed session.</summary>
    public static void SendWhisper(string room, string myName, string text)
    {
        if (!IsConfigured)
            return;

        var payload = new JObject
                          {
                              ["room"] = room,
                              ["name"] = myName,
                              ["text"] = text,
                          };
        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("whisper", payload, CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: whisper failed: {error}");
                         });
    }

    /// <summary>Host: answers a whisper.</summary>
    public static void ReplyWhisper(string room, string key, string to, string text)
    {
        if (!IsConfigured)
            return;

        var payload = new JObject
                          {
                              ["room"] = room,
                              ["key"] = key,
                              ["to"] = to,
                              ["text"] = text,
                          };
        _ = Task.Run(async () =>
                         {
                             var (_, error) = await PostAsync("whisper", payload, CancellationToken.None);
                             if (error != null)
                                 Log.Debug($"Collab backend: whisper reply failed: {error}");
                         });
    }

    /// <summary>Polls the whisper thread. Host passes its key; a visitor passes its own name.</summary>
    public static async Task<IReadOnlyList<(string From, string Text, bool IsHostReply, long At)>> PollWhispersAsync(
        string room, string key, string visitorName, long since, CancellationToken cancellation)
    {
        if (!IsConfigured)
            return [];

        // The key (and name) ride in the JSON body, not the URL: the backend merges body
        // into its input, and the host credential must not land in server access logs.
        var payload = new JObject
                          {
                              ["room"] = room,
                              ["since"] = since,
                          };
        if (key.Length > 0)
            payload["key"] = key;
        else if (visitorName.Length > 0)
            payload["name"] = visitorName;

        var (body, error) = await PostAsync("whispers", payload, cancellation);
        if (error != null || body is not JObject json)
            return [];

        var result = new List<(string, string, bool, long)>();
        foreach (var token in json["whispers"] ?? new JArray())
        {
            if (token is not JObject entry)
                continue;

            CollabProtocol.TryGetBool(entry, "host", out var isHostReply);
            CollabProtocol.TryGetInt(entry, "at", out var at);
            result.Add((CollabProtocol.GetString(entry, "from"),
                        CollabProtocol.GetString(entry, "text"),
                        isHostReply,
                        at));
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Join tickets: the relay-free session rendezvous
    // ---------------------------------------------------------------------------------------

    /// <summary>One signal message in a ticket: the editor's own signal JSON fields.</summary>
    public sealed record TicketMessage(string From, string Type, string Sdp, string Candidate, string Mid);

    /// <summary>One open join ticket as the host sees it. Claim is an optional host-side token
    /// (used by auto-approval) and stays empty when the backend does not send one.</summary>
    public sealed record JoinTicket(string Id, string Name, string Lang, string State, long At, int Messages, string Claim = "");

    private static async Task<(JToken? Body, string? Error)> GetAsync(string op, string query, CancellationToken cancellation)
    {
        try
        {
            using var response = await _http.GetAsync($"{BackendUrl}?op={op}{query}", cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return (null, "the backend answered with something that is not JSON");
            }

            return (json["ok"]?.Value<bool>() ?? false) ? (json, null) : (null, json["error"]?.Value<string>() ?? "refused");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }

    /// <summary>Guest: asks the backend for a join ticket (the rendezvous for one WebRTC handshake).
    /// <paramref name="claim"/> is the one-shot token of a remote-management connect request; a
    /// managed installation auto-approves only the ticket carrying the claim it was given.</summary>
    public static async Task<string> JoinRequestAsync(string room, string myName, string lang, string claim, CancellationToken cancellation)
    {
        var payload = new JObject
                          {
                              ["room"] = room,
                              ["name"] = myName,
                              ["lang"] = lang ?? "",
                          };

        if (!string.IsNullOrEmpty(claim))
            payload["claim"] = claim;

        var (body, error) = await PostAsync("join-request", payload, cancellation);
        if (error != null)
            throw new Exception(error);

        return body?["ticket"]?.Value<string>() ?? throw new Exception("the backend sent no ticket");
    }

    /// <summary>Host: the open join tickets of the session.</summary>
    public static async Task<(IReadOnlyList<JoinTicket> Tickets, string? Error)> FetchTicketsAsync(
        string room, string key, CancellationToken cancellation)
    {
        // The key rides in the JSON body, not the URL (see PollWhispersAsync).
        var (body, error) = await PostAsync("tickets", new JObject
                                                           {
                                                               ["room"] = room,
                                                               ["key"] = key,
                                                           }, cancellation);
        if (error != null)
            return ([], error);

        var tickets = new List<JoinTicket>();
        foreach (var token in body?["tickets"] ?? new JArray())
        {
            if (token is not JObject entry)
                continue;

            CollabProtocol.TryGetInt(entry, "msgs", out var msgs);
            tickets.Add(new JoinTicket(CollabProtocol.GetString(entry, "id"),
                                       CollabProtocol.GetString(entry, "name"),
                                       CollabProtocol.GetString(entry, "lang"),
                                       CollabProtocol.GetString(entry, "state", "pending"),
                                       CollabProtocol.TryGetInt(entry, "at", out var at) ? at : 0,
                                       msgs,
                                       CollabProtocol.GetString(entry, "claim")));
        }

        return (tickets, null);
    }

    /// <summary>Posts one WebRTC signal message into a ticket. The host authenticates with the
    /// room key; a guest with its ticket id.</summary>
    public static async Task<string?> PostTicketMessageAsync(string room, string key, string ticket, byte[] payload, CancellationToken cancellation)
    {
        JObject message;
        try
        {
            message = JObject.Parse(Encoding.UTF8.GetString(payload));
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return "malformed signal";
        }

        var payloadJson = new JObject
                              {
                                  ["room"] = room,
                                  ["ticket"] = ticket,
                                  ["message"] = message,
                              };
        if (key.Length > 0)
            payloadJson["key"] = key;

        var (_, error) = await PostAsync("ticket-put", payloadJson, cancellation);
        return error;
    }

    /// <summary>Reads the ticket's signal messages after the given index, plus its state.</summary>
    public static async Task<(IReadOnlyList<TicketMessage> Messages, string State, string? Error)> FetchTicketMessagesAsync(
        string room, string key, string ticket, long since, CancellationToken cancellation)
    {
        // The key rides in the JSON body, not the URL (see PollWhispersAsync).
        var payload = new JObject
                          {
                              ["room"] = room,
                              ["ticket"] = ticket,
                              ["since"] = since,
                          };
        if (key.Length > 0)
            payload["key"] = key;

        var (body, error) = await PostAsync("ticket-get", payload, cancellation);
        if (error != null)
            return ([], "pending", error);

        var messages = new List<TicketMessage>();
        foreach (var token in body?["msgs"] ?? new JArray())
        {
            if (token is not JObject entry)
                continue;

            messages.Add(new TicketMessage(CollabProtocol.GetString(entry, "from"),
                                           CollabProtocol.GetString(entry, "t"),
                                           CollabProtocol.GetString(entry, "sdp"),
                                           CollabProtocol.GetString(entry, "candidate"),
                                           CollabProtocol.GetString(entry, "mid")));
        }

        return (messages, body is JObject bodyObject ? CollabProtocol.GetString(bodyObject, "state", "pending") : "pending", null);
    }

    /// <summary>Host: sets a join ticket's state (approved starts the WebRTC negotiation).</summary>
    public static async Task<string?> SetTicketStateAsync(string room, string key, string ticket, string state, CancellationToken cancellation)
    {
        var payload = new JObject
                          {
                              ["room"] = room,
                              ["key"] = key,
                              ["ticket"] = ticket,
                              ["state"] = state,
                          };
        var (_, error) = await PostAsync("ticket-state", payload, cancellation);
        return error;
    }
}