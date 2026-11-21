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
/// A minimal Discord REST client for the serverless session browser: a bot token posts one
/// message ("session card") per hosted session into a configured lobby channel, edits it when
/// the listing changes and deletes it when the session ends; the browser lists the channel's
/// cards. No infrastructure of our own: Discord is the directory.
/// </summary>
internal static class CollabDiscordBot
{
    private const string ApiBase = "https://discord.com/api/v10";
    private const int CardVersion = 1;

    /// <summary>One listed session: the human fields the browser shows plus the join payload.</summary>
    public sealed record Card(string Room,
                              string Relay,
                              string Title,
                              string Purpose,
                              string Project,
                              string HostName,
                              string Lang,
                              int Members,
                              int MaxMembers,
                              bool PasswordProtected,
                              string MessageId);

    /// <summary>Everything a card needs at creation.</summary>
    public sealed record CardDraft(string Room,
                                   string Relay,
                                   string Title,
                                   string Purpose,
                                   string Project,
                                   string HostName,
                                   string Lang,
                                   bool PasswordProtected);

    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TiXLCollab/1.0");
        return client;
    }

    public static bool IsConfigured => UserSettings.Config.CollabDiscordBotToken.Length > 0
                                       && UserSettings.Config.CollabDiscordLobbyChannelId.Length > 0;

    /// <summary>
    /// Builds the card embed: the human fields plus the machine payload (room and relay) in the
    /// footer, encoded so a long relay address cannot overflow the footer's 2048 characters.
    /// </summary>
    private static JObject BuildEmbed(CardDraft card)
    {
        var payload = new JObject
                          {
                              ["v"] = CardVersion,
                              ["room"] = card.Room,
                              ["relay"] = card.Relay,
                          };
        var encoded = EncodePayload(payload);
        return new JObject
                   {
                       ["title"] = TitleClamp(card.Title),
                       ["color"] = 0x9C27B0,
                       ["fields"] = new JArray
                                        (
                                            new JObject { ["name"] = "Purpose", ["value"] = FieldClamp(card.Purpose, 24), ["inline"] = true },
                                            new JObject { ["name"] = "Project", ["value"] = FieldClamp(card.Project, 64), ["inline"] = true },
                                            new JObject { ["name"] = "Host", ["value"] = FieldClamp(card.HostName, 48), ["inline"] = true },
                                            new JObject { ["name"] = "Language", ["value"] = FieldClamp(card.Lang, 12), ["inline"] = true },
                                            new JObject { ["name"] = "Access", ["value"] = card.PasswordProtected ? "password" : "open", ["inline"] = true }
                                        ),
                       ["footer"] = new JObject { ["text"] = "TiXL card " + encoded },
                   };
    }

    private static string TitleClamp(string title)
    {
        var cleaned = Clean(title);
        return cleaned.Length == 0 ? "TiXL session" : cleaned;
    }

    private static string FieldClamp(string value, int max)
    {
        var cleaned = Clean(value);
        if (cleaned.Length == 0)
            return "-";

        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }

    /// <summary>Drops control characters and trims; these strings end up in other people's UI.</summary>
    private static string Clean(string value)
    {
        return new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
    }

    /// <summary>base64url without padding; fits the footer and survives copy-paste.</summary>
    private static string EncodePayload(JObject payload)
    {
        var json = payload.ToString(Newtonsoft.Json.Formatting.None);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                      .TrimEnd('=')
                      .Replace('+', '-')
                      .Replace('/', '_');
    }

    private static JObject? DecodePayload(string footerText)
    {
        var marker = "TiXL card ";
        var index = footerText.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var encoded = footerText[(index + marker.Length)..].Trim();
        if (encoded.Length == 0 || encoded.Length > 256)
            return null;

        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            var padding = (4 - base64.Length % 4) % 4;
            base64 += new string('=', padding);
            var bytes = Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);
            return JObject.Parse(text) as JObject;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // REST
    // ---------------------------------------------------------------------------------------

    private static HttpRequestMessage BotRequest(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, ApiBase + path);
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bot", UserSettings.Config.CollabDiscordBotToken);
        return message;
    }

    /// <summary>Sends the request and returns the parsed body, or null with a readable reason.</summary>
    private static async Task<(JToken? Body, string? Error)> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            using var response = await _http.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode.ToString();
                try
                {
                    detail = JObject.Parse(text)["message"]?.Value<string>() ?? detail;
                }
                catch (Newtonsoft.Json.JsonException)
                {
                }

                return (null, $"{(int)response.StatusCode}: {detail}");
            }

            if (text.Length == 0)
                return (new JObject(), null);

            return (JToken.Parse(text), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }

    /// <summary>Posts the card message. Returns its id, or null with the reason.</summary>
    public static async Task<(string? MessageId, string? Error)> PostCardAsync(CardDraft card, CancellationToken cancellation)
    {
        var payload = new JObject
                          {
                              ["content"] = "",
                              ["embeds"] = new JArray(BuildEmbed(card)),
                          };
        return await PostMessageAsync(UserSettings.Config.CollabDiscordLobbyChannelId, payload, cancellation);
    }

    private static async Task<(string? MessageId, string? Error)> PostMessageAsync(string channelId, JObject payload, CancellationToken cancellation)
    {
        var request = BotRequest(HttpMethod.Post, $"/channels/{channelId}/messages");
        request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var (body, error) = await SendAsync(request, cancellation);
        if (error != null)
            return (null, error);

        return (body?["id"]?.Value<string>(), null);
    }

    /// <summary>Rewrites the card's embed (Change entry). Returns null on success.</summary>
    public static async Task<string?> UpdateCardAsync(string messageId, CardDraft card, CancellationToken cancellation)
    {
        var payload = new JObject
                          {
                              ["embeds"] = new JArray(BuildEmbed(card)),
                          };
        var request = BotRequest(HttpMethod.Patch, $"/channels/{UserSettings.Config.CollabDiscordLobbyChannelId}/messages/{messageId}");
        request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var (_, error) = await SendAsync(request, cancellation);
        return error;
    }

    /// <summary>Deletes the card (session ended or unlisted). Best effort; returns null on success.</summary>
    public static async Task<string?> DeleteCardAsync(string messageId, CancellationToken cancellation)
    {
        var request = BotRequest(HttpMethod.Delete, $"/channels/{UserSettings.Config.CollabDiscordLobbyChannelId}/messages/{messageId}");
        var (_, error) = await SendAsync(request, cancellation);
        return error;
    }

    /// <summary>
    /// Reads the lobby channel and decodes every TiXL card. Discord returns newest first; the
    /// list here is oldest first so row numbers match the channel's chronological order.
    /// </summary>
    public static async Task<(IReadOnlyList<Card> Cards, string? Error)> FetchCardsAsync(CancellationToken cancellation)
    {
        var request = BotRequest(HttpMethod.Get, $"/channels/{UserSettings.Config.CollabDiscordLobbyChannelId}/messages?limit=50");
        var (body, error) = await SendAsync(request, cancellation);
        if (error != null)
            return (Array.Empty<Card>(), error);

        if (body is not JArray array)
            return (Array.Empty<Card>(), "unexpected response shape");

        var cards = new List<Card>();
        foreach (var token in array)
        {
            if (token is not JObject message)
                continue;

            var embed = message["embeds"]?.First as JObject;
            if (embed == null)
                continue;

            var payload = DecodePayload(embed["footer"]?["text"]?.Value<string>() ?? "");
            if (payload == null)
                continue;

            var room = CollabProtocol.GetString(payload, "room");
            if (string.IsNullOrEmpty(room))
                continue;

            var relay = CollabProtocol.GetString(payload, "relay");
            var host = FieldClamp(embed["fields"]?[2]?["value"]?.Value<string>() ?? "", 48);
            var members = 1;
            var maxMembers = 18;
            cards.Add(new Card(room,
                               relay,
                               embed["title"]?.Value<string>() ?? "TiXL session",
                               FieldClamp(embed["fields"]?[0]?["value"]?.Value<string>() ?? "", 24),
                               FieldClamp(embed["fields"]?[1]?["value"]?.Value<string>() ?? "", 64),
                               host,
                               FieldClamp(embed["fields"]?[3]?["value"]?.Value<string>() ?? "", 12),
                               members,
                               maxMembers,
                               string.Equals(embed["fields"]?[4]?["value"]?.Value<string>(), "password", StringComparison.OrdinalIgnoreCase),
                               message["id"]?.Value<string>() ?? ""));
        }

        cards.Reverse();
        return (cards, null);
    }
}