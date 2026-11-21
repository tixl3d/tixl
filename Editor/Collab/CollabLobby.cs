#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Collab;

/// <summary>One session a relay or the web backend lists: enough to pick it from a table and join it.
/// <paramref name="Relay"/> is "host:port" when the listing carries the host's relay address.</summary>
internal sealed record LobbyRoom(string Code,
                                 string Title,
                                 string Purpose,
                                 string Project,
                                 string HostName,
                                 string Lang,
                                 bool HostVerified,
                                 int Members,
                                 int MaxMembers,
                                 bool PasswordProtected,
                                 string Relay = "",
                                 string Platform = "",
                                 string Region = "",
                                 bool Editable = true);

/// <summary>
/// The session browser's data: asks a relay which sessions its hosts chose to list. Only hosts
/// that turned on "List in session browser" appear; everything else is reachable by session ID only.
/// The request runs on a pool thread; the UI reads the latest result.
/// </summary>
internal static class CollabLobby
{
    /// <summary>Result of the last successful refresh. Replaced as a whole, never changed in place.</summary>
    public static IReadOnlyList<LobbyRoom> Rooms => _rooms;

    public static bool IsRefreshing => _refreshing != 0;

    /// <summary>Why the last refresh failed; empty after a successful one.</summary>
    public static string ErrorText { get; private set; } = "";

    /// <summary>Relay the listed rooms belong to ("host:port"); joining goes there.</summary>
    public static string RelayEndpoint { get; private set; } = "";

    public static DateTime LastRefreshUtc { get; private set; } = DateTime.MinValue;

    private static volatile LobbyRoom[] _rooms = [];
    private static int _refreshing;

    public static void Refresh(string relayHost, int relayPort)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0)
            return;

        _ = Task.Run(async () =>
                     {
                         try
                         {
                             var list = await CollabRelayWire.FetchRoomListAsync(relayHost, relayPort, CancellationToken.None);
                             var rooms = new List<LobbyRoom>(list.Count);
                             foreach (var token in list)
                             {
                                 if (token is not JObject entry)
                                     continue;

                                 var code = CollabProtocol.GetString(entry, "code");
                                 if (string.IsNullOrEmpty(code))
                                     continue;

                                 CollabProtocol.TryGetBool(entry, "verified", out var verified);
                                 CollabProtocol.TryGetBool(entry, "pwd", out var passwordProtected);
                                 CollabProtocol.TryGetInt(entry, "members", out var members);
                                 CollabProtocol.TryGetInt(entry, "max", out var maxMembers);
                                 rooms.Add(new LobbyRoom(code,
                                                         CollabProtocol.GetString(entry, "title"),
                                                         CollabProtocol.GetString(entry, "purpose"),
                                                         CollabProtocol.GetString(entry, "project"),
                                                         CollabProtocol.GetString(entry, "host"),
                                                         CollabProtocol.GetString(entry, "lang"),
                                                         verified,
                                                         members,
                                                         maxMembers,
                                                         passwordProtected));
                             }

                             _rooms = rooms.ToArray();
                             RelayEndpoint = $"{relayHost}:{relayPort}";
                             ErrorText = "";
                             LastRefreshUtc = DateTime.UtcNow;
                         }
                         catch (Exception e)
                         {
                             ErrorText = $"Could not load the session list: {e.Message}";
                         }
                         finally
                         {
                             Interlocked.Exchange(ref _refreshing, 0);
                         }
                     });
    }
}

/// <summary>
/// A visitor's whisper conversation with one listed session's host: each whisper opens a relay
/// connection, delivers the line to the host's control link and, while the conversation is the
/// active one, stays listening there for the host's replies. Not part of a session chat - the
/// whisperer has not joined (yet).
/// </summary>
internal static class CollabLobbyWhisper
{
    public sealed class Entry
    {
        public DateTime Time = DateTime.Now;
        public string From = "";
        public string Text = "";
        public bool Mine;
    }

    private static readonly object _gate = new();
    private static readonly List<Entry> _messages = [];
    private static CancellationTokenSource? _listenCancel;
    private static string _listenRoom = "";

    /// <summary>The conversation, oldest first. Guarded by <see cref="_gate"/>; read every frame.</summary>
    public static IReadOnlyList<Entry> Messages
    {
        get
        {
            lock (_gate)
                return _messages.ToArray();
        }
    }

    /// <summary>Sends a whisper to a listed session's host and listens for its reply. Safe to
    /// call from the UI thread; the network work runs on a pool thread.</summary>
    public static void Send(string relayEndpoint, string room, string myName, string lang, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(room))
            return;

        var (host, port) = SplitEndpoint(relayEndpoint);
        if (port <= 0)
            return;

        lock (_gate)
        {
            if (_listenRoom != room)
                StopListening();
            _listenRoom = room;
            _messages.Add(new Entry { From = myName, Text = text, Mine = true });
        }

        var previousListen = _listenCancel;
        var listenCts = new CancellationTokenSource();
        _listenCancel = listenCts;
        previousListen?.Cancel();

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var error = await CollabRelayWire.SendWhisperAsync(host, port, room, myName, lang, text,
                                                                                    onReply: (from, reply) =>
                                                                                    {
                                                                                        lock (_gate)
                                                                                            _messages.Add(new Entry
                                                                                                              {
                                                                                                                  From = from,
                                                                                                                  Text = reply,
                                                                                                              });
                                                                                    },
                                                                                    cancellation: listenCts.Token);
                                 if (error != null)
                                 {
                                     lock (_gate)
                                         _messages.Add(new Entry { From = "", Text = error, Mine = false });
                                 }
                             }
                             catch (Exception e)
                             {
                                 lock (_gate)
                                     _messages.Add(new Entry { From = "", Text = $"whisper failed: {e.Message}", Mine = false });
                             }
                         });
    }

    /// <summary>Drops the conversation and its listener connection (join, room switch, close).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            StopListening();
            _listenRoom = "";
            _messages.Clear();
        }
        _backendSince = 0;
        _lastBackendPollTicks = 0;
    }

    /// <summary>Whisper through the self-hosted backend instead of a relay listener connection.</summary>
    public static void SendViaBackend(string room, string myName, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(room))
            return;

        lock (_gate)
        {
            if (_listenRoom != room)
            {
                StopListening();
                _messages.Clear();
            }
            _listenRoom = room;
            _messages.Add(new Entry { From = myName, Text = text, Mine = true });
        }

        _backendSince = 0;
        CollabWebLobby.SendWhisper(room, myName, text);
    }

    /// <summary>
    /// Drains the backend whisper thread for the active conversation. Called from the UI every
    /// frame; the actual network poll runs at most every few seconds on a pool thread.
    /// </summary>
    public static void PollBackend(string room, string myName)
    {
        if (!CollabWebLobby.IsConfigured || room.Length == 0 || myName.Length == 0)
            return;

        var now = Environment.TickCount64;
        if (now < _lastBackendPollTicks + BackendPollIntervalMs)
            return;

        _lastBackendPollTicks = now;
        var since = _backendSince;
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var whispers = await CollabWebLobby.PollWhispersAsync(room, "", myName, since, CancellationToken.None);
                                 if (whispers.Count == 0)
                                     return;

                                 lock (_gate)
                                 {
                                     foreach (var (from, text, isHostReply, at) in whispers)
                                     {
                                         if (isHostReply)
                                             _messages.Add(new Entry { From = from, Text = text, Mine = false });
                                         _backendSince = Math.Max(_backendSince, at);
                                     }
                                 }
                             }
                             catch (Exception e)
                             {
                                 Log.Debug($"Collab backend: visitor whisper poll failed: {e.Message}");
                             }
                         });
    }

    private const int BackendPollIntervalMs = 3_000;
    private static long _backendSince;
    private static long _lastBackendPollTicks;

    private static void StopListening()
    {
        _listenCancel?.Cancel();
        _listenCancel?.Dispose();
        _listenCancel = null;
    }

    private static (string Host, int Port) SplitEndpoint(string endpoint)
    {
        var text = (endpoint ?? "").Trim();
        var separator = text.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(text[(separator + 1)..], out var port))
            return (text, 0);
        return (text[..separator], port);
    }
}
